using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scout.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Empty JSON");
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}
public enum Screen { Unknown, CardReward, Merchant }
public enum EntityKind { Card, Relic, Potion, Service }
public enum ClaimKind { CardSynergy, RelicSynergy, AntiSynergy, Archetype, DeckNeed, MerchantPriority }
public sealed record CatalogFacts(string NormalizedName, string[] Aliases, string Character, string Rarity,
    string? Type, string? Cost, string? StarCost, string Target, bool RewardPool, bool MultiplayerOnly,
    Dictionary<string, System.Text.Json.JsonElement> Values, Dictionary<string, System.Text.Json.JsonElement> Upgrade,
    string[] Keywords, string GameVersion, string SourceId);
public sealed record CatalogSource(string Id, string Url, string GameVersion, string Branch, string Sha256, string Rights);
public sealed record CatalogMetadata(string Branch, DateTimeOffset GeneratedAt, CatalogSource[] Sources, string StrategyMethod);
public sealed record Entity(string Id, string Name, EntityKind Kind, double Baseline, string[] Tags, double SetupRisk = 0,
    CatalogFacts? Facts = null, string? BaselineReason = null);
public sealed record Provenance(string SourceUrl, DateTimeOffset RetrievedAt, DateTimeOffset? PublishedAt, string GameVersion, string EvidenceType, double Confidence, string EvidenceHash, string License, string EvidenceLocator);
public sealed record Claim(string Id, ClaimKind Kind, string Subject, string? Other, string? Tag, double Weight, string GameVersion, Provenance Source, string ReviewState, string Reason);
public sealed record StrategyPack(int SchemaVersion, string PackVersion, string GameVersion, Entity[] Entities, Claim[] Claims, CatalogMetadata? Catalog = null);
public sealed record Choice(int Slot, string? EntityId, double Confidence, int? Price = null, double PriceConfidence = 0, bool Upgraded = false);
public sealed record Observation(DateTimeOffset At, Screen Screen, double Confidence, Choice[] Choices, string ProfileVersion, string FrameHash, Selection[]? SelectionSignals = null);
public sealed record RunContext(string GameVersion, string[] Deck, string[] Relics, string[] Needs, string[] Archetypes, int Act = 1, int Ascension = 0, int Gold = 0, int ReserveGold = 0);
public sealed record Recommendation(int Slot, string EntityId, double Score, string[] Reasons);
public sealed record Selection(int Slot, string EntityId, double Confidence, string Evidence, bool Confirmed = false);

public static class PackValidation
{
    public static void Validate(StrategyPack pack)
    {
        if (pack.SchemaVersion is not (1 or 2) || string.IsNullOrWhiteSpace(pack.PackVersion) || string.IsNullOrWhiteSpace(pack.GameVersion)) throw new InvalidDataException("Unsupported or missing pack version");
        if (pack.Entities.Any(e => e is null) || pack.Claims.Any(c => c is null)) throw new InvalidDataException("Null pack entries");
        if (pack.Claims.GroupBy(c => (c.Kind, c.Subject, c.Other, c.Tag, c.Weight, c.GameVersion, c.Source.SourceUrl)).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate semantic claims");
        if (pack.Entities.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != pack.Entities.Length) throw new InvalidDataException("Duplicate entity IDs");
        if (pack.Claims.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != pack.Claims.Length) throw new InvalidDataException("Duplicate claim IDs");
        foreach (var e in pack.Entities)
            if (string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Name) || !double.IsFinite(e.Baseline) || Math.Abs(e.Baseline) > 100 || !double.IsFinite(e.SetupRisk) || e.SetupRisk is < 0 or > 10 || !Enum.IsDefined(e.Kind)) throw new InvalidDataException("Invalid entity");
        if (pack.SchemaVersion == 2)
        {
            if (pack.Entities.Length == 0) throw new InvalidDataException("Empty runtime catalog");
            var catalog = pack.Catalog ?? throw new InvalidDataException("Missing catalog provenance");
            if (catalog.Branch != "main" || catalog.GeneratedAt == default || catalog.GeneratedAt > DateTimeOffset.UtcNow.AddMinutes(5) || string.IsNullOrWhiteSpace(catalog.StrategyMethod)) throw new InvalidDataException("Invalid catalog metadata");
            if (catalog.Sources.Length == 0 || catalog.Sources.Select(s => s.Id).Distinct().Count() != catalog.Sources.Length || catalog.Sources.Any(s => s.GameVersion != pack.GameVersion || s.Branch != catalog.Branch || s.Sha256.Length != 64 || !s.Sha256.All(Uri.IsHexDigit) || !Uri.TryCreate(s.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrWhiteSpace(s.Rights))) throw new InvalidDataException("Source/version mismatch");
            foreach (var e in pack.Entities)
            {
                var f = e.Facts ?? throw new InvalidDataException("Missing factual catalog entry");
                if (f.GameVersion != pack.GameVersion || !catalog.Sources.Any(s => s.Id == f.SourceId) || f.NormalizedName != CardNameMatcher.Normalize(e.Name) || string.IsNullOrEmpty(f.NormalizedName) || string.IsNullOrWhiteSpace(e.BaselineReason)) throw new InvalidDataException("Invalid catalog identity/version");
                if (f.Character is not ("ironclad" or "silent" or "defect" or "regent" or "necrobinder" or "colorless" or "shared" or "event" or "curse" or "token" or "status" or "quest")) throw new InvalidDataException("Invalid character");
                if (e.Kind == EntityKind.Card && f.Type is not ("Attack" or "Skill" or "Power" or "Curse" or "Status" or "Quest")) throw new InvalidDataException("Invalid card type");
            }
            // Starter Strike/Defend legitimately share a display name across characters.
            if (pack.Entities.GroupBy(e => (e.Kind, e.Facts!.Character, e.Facts.NormalizedName)).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate normalized names within entity pool");
        }
        foreach (var c in pack.Claims)
        {
            var error = ClaimError(c, pack);
            if (error != null || c.ReviewState != "approved") throw new InvalidDataException(error ?? "Unreviewed claim");
        }
    }
    public static string? ClaimError(Claim c, StrategyPack pack)
    {
        if (!Enum.IsDefined(c.Kind) || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Reason)) return "Invalid claim schema";
        if (!pack.Entities.Any(e => e.Id == c.Subject) || (c.Other != null && !pack.Entities.Any(e => e.Id == c.Other))) return "Unknown entity";
        if (c.Kind is ClaimKind.CardSynergy or ClaimKind.RelicSynergy or ClaimKind.AntiSynergy && c.Other == null) return "Missing partner";
        if (c.Kind is ClaimKind.DeckNeed or ClaimKind.Archetype && string.IsNullOrWhiteSpace(c.Tag)) return "Missing tag";
        if (c.GameVersion != pack.GameVersion || c.Source.GameVersion != pack.GameVersion) return "Patch mismatch";
        if (c.Kind == ClaimKind.AntiSynergy && c.Weight > 0) return "Anti-synergy must not have a positive weight";
        if (c.Kind == ClaimKind.CardSynergy && !pack.Entities.Any(e => e.Id == c.Other && e.Kind == EntityKind.Card)) return "Card synergy partner must be a card";
        if (c.Kind == ClaimKind.RelicSynergy && !pack.Entities.Any(e => e.Id == c.Other && e.Kind == EntityKind.Relic)) return "Relic synergy partner must be a relic";
        var p = c.Source;
        if (!double.IsFinite(c.Weight) || Math.Abs(c.Weight) > 20 || !double.IsFinite(p.Confidence) || p.Confidence is < 0 or > 1) return "Invalid weight/confidence";
        if (!Uri.TryCreate(p.SourceUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "file") || p.RetrievedAt == default || p.RetrievedAt > DateTimeOffset.UtcNow.AddMinutes(5) || p.PublishedAt > p.RetrievedAt || p.EvidenceHash.Length != 64 || !p.EvidenceHash.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(p.License) || string.IsNullOrWhiteSpace(p.EvidenceLocator) || p.EvidenceType is not ("expert-opinion" or "mechanic" or "correlation" or "synthetic")) return "Missing or invalid provenance";
        return null;
    }
}

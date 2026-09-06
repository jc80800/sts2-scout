namespace Scout.Core;

public sealed record CardDetails(string Color, string Type, string Rarity, string Cost, string[] Mechanics,
    string[] UpgradeChanges, string[] UpgradedTags, Provenance Source, string[] Uncertainty, bool MultiplayerOnly = false);
public sealed record CatalogInfo(string Version, DateTimeOffset RetrievedAt, int ExpectedCards, string SourceHash, string StrategyMethod);
public sealed record DeckEntry(string EntityId, int Copies, bool Upgraded);

public static class Deck
{
    // Legacy repeated IDs migrate losslessly to explicit base copies. Never infer old upgrades.
    public static DeckEntry[] Entries(RunContext context) => context.Cards ?? context.Deck
        .GroupBy(id => id, StringComparer.Ordinal).Select(g => new DeckEntry(g.Key, g.Count(), false)).ToArray();
    public static RunContext Migrate(RunContext context) => context with { Cards = Entries(context) };
    public static string[] Ids(RunContext context) => Entries(context).SelectMany(e => Enumerable.Repeat(e.EntityId, e.Copies)).ToArray();
    public static void Validate(RunContext context, StrategyPack pack)
    {
        var entries = Entries(context);
        if (entries.Any(e => e is null || e.Copies is < 1 or > 99 || !pack.Entities.Any(c => c.Id == e.EntityId && c.Kind == EntityKind.Card)) ||
            entries.Sum(e => e.Copies) > 500 || entries.GroupBy(e => (e.EntityId, e.Upgraded)).Any(g => g.Count() > 1))
            throw new InvalidDataException("Deck has unknown cards, duplicate rows, or invalid copy counts");
        if (context.Act is < 1 or > 4 || context.Ascension is < 0 or > 30) throw new InvalidDataException("Invalid act/ascension");
    }
}

public static class CatalogValidation
{
    public static void Validate(StrategyPack pack)
    {
        if (pack.Catalog is not { } info) return; // Legacy strategy packs remain supported for merchants.
        if (string.IsNullOrWhiteSpace(info.Version) || info.ExpectedCards < 1 || pack.Entities.Count(e => e.Kind == EntityKind.Card) != info.ExpectedCards ||
            info.SourceHash.Length != 64 || !info.SourceHash.All(Uri.IsHexDigit) || info.RetrievedAt == default || info.RetrievedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new InvalidDataException("Incomplete or invalid catalog manifest");
        foreach (var entity in pack.Entities.Where(e => e.Kind == EntityKind.Card))
        {
            var c = entity.Card ?? throw new InvalidDataException("Card metadata missing");
            if (string.IsNullOrWhiteSpace(c.Color) || string.IsNullOrWhiteSpace(c.Type) || string.IsNullOrWhiteSpace(c.Cost) || string.IsNullOrWhiteSpace(c.Rarity) ||
                c.Source.GameVersion != pack.GameVersion || c.Source.EvidenceHash != info.SourceHash || !double.IsFinite(c.Source.Confidence) || c.Source.Confidence is < 0 or > 1 ||
                c.Source.RetrievedAt != info.RetrievedAt || !Uri.TryCreate(c.Source.SourceUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                string.IsNullOrWhiteSpace(c.Source.License) || c.Mechanics.Length == 0 || c.UpgradeChanges.Length == 0)
                throw new InvalidDataException("Invalid card mechanics/provenance");
        }
    }
    public static Entity[] Pool(StrategyPack pack, string character) => pack.Entities.Where(e => e.Kind == EntityKind.Card &&
        e.Card is { MultiplayerOnly: false } card && (card.Color == character || card.Color == "colorless") &&
        card.Rarity is "Common" or "Uncommon" or "Rare").ToArray();
}

public sealed record PackLoad(StrategyPack Pack, string Status);
public static class PackCache
{
    public static PackLoad Load(ScoutPaths paths, string bundled, string gameVersion)
    {
        var errors = new List<string>();
        foreach (var name in new[] { "strategy-pack.json", "strategy-pack.last-valid.json" })
        {
            var path = paths.FilePath(name);
            if (!File.Exists(path)) continue;
            try
            {
                var pack = Json.Read<StrategyPack>(path); Check(pack, gameVersion);
                if (name == "strategy-pack.json") paths.AtomicWrite("strategy-pack.last-valid.json", Json.Write(pack));
                return new(pack, string.Join("; ", errors.Append($"Using cached data {pack.Catalog?.RetrievedAt:yyyy-MM-dd}")));
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentException) { errors.Add($"Rejected {name}: {ex.Message}"); }
        }
        var fallback = Json.Read<StrategyPack>(bundled); PackValidation.Validate(fallback);
        return new(fallback, string.Join("; ", errors.Append("Using bundled catalog; exact game version required")));
    }
    public static void Install(ScoutPaths paths, StrategyPack pack, string gameVersion)
    {
        Check(pack, gameVersion);
        if (pack.Catalog == null) throw new InvalidDataException("Installing a reward pack requires a complete catalog");
        var current = paths.FilePath("strategy-pack.json");
        if (File.Exists(current))
        {
            try { var old = Json.Read<StrategyPack>(current); PackValidation.Validate(old); paths.AtomicWrite("strategy-pack.last-valid.json", Json.Write(old)); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { /* Keep earlier valid backup. */ }
        }
        paths.AtomicWrite("strategy-pack.json", Json.Write(pack));
    }
    private static void Check(StrategyPack pack, string gameVersion)
    {
        PackValidation.Validate(pack);
        if (pack.GameVersion != gameVersion) throw new InvalidDataException("Incomplete catalog or incompatible game version");
    }
}

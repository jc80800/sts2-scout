using System.Text.Json;

namespace Scout.Core;

/// <summary>What one play (or one turn, for powers) contributes to each deck job. Units: damage ÷ 6, block ÷ 5.
/// Energy is the gross energy gained per play; the card's own cost is charged in demand.</summary>
public sealed record RoleEffect(
    double Frontload = 0, double Aoe = 0, double Block = 0, double Scaling = 0, double Draw = 0, double Energy = 0,
    double HpLoss = 0, double VulnerableSource = 0, double ExhaustSource = 0, double BlockEvents = 0, RoleCondition? Conditional = null);
/// <summary>An effect that only applies when the deck meets <see cref="Kind"/>, stored at its full-condition value.</summary>
public sealed record RoleCondition(string Kind, RoleEffect Effect);
public sealed record CardRole(
    string Id, string Type, int? Cost, int? UpgradedCost,
    RoleEffect Base, RoleEffect Upgraded,
    RoleEffect? PerTurn, RoleEffect? UpgradedPerTurn,
    bool SelfExhaust, int SetupTurns,
    string CopyBehavior, string UpgradeClass,
    string[] RareDependency, string Modeled, string Notes);
public sealed record RelicRole(string Id, string Name, string Effect, double Value);
public sealed record CardRoleSet(string GameVersion, CardRole[] Cards, RelicRole[] Relics);

/// <summary>Hand-reviewed per-patch role annotations. X-cost cards store Cost 2 (X assumed 2).</summary>
public static class CardRoles
{
    public const string GameVersion = "0.107.1";
    public static readonly string[] ConditionKinds = ["vulnerable", "exhaust", "hpLoss", "strikeNamed", "attacks", "block"];
    public static readonly string[] CopyBehaviors = ["stackable", "nonStacking", "selfLimiting", "uncertain"];
    public static readonly string[] UpgradeClasses = ["large", "normal", "negative"];
    public static readonly string[] ModeledLevels = ["full", "partial", "none"];
    public static readonly string[] RelicEffects = ["colorlessRewards", "vulnerableSource", "vulnerablePayoff", "hpLossRelief", "strikeBonus", "exhaustPayoff", "scaling", "strengthDouble", "blockPerTurn", "bigAttack", "exhaustSource"];
    private static readonly Lazy<CardRoleSet> Embedded = new(() =>
    {
        using var stream = typeof(CardRoles).Assembly.GetManifestResourceStream("Scout.Core.card-roles.json") ?? throw new InvalidDataException("Card role data missing");
        return JsonSerializer.Deserialize<CardRoleSet>(stream, Json.Options) ?? throw new InvalidDataException("Empty card role data");
    });

    public static CardRoleSet Load() => Embedded.Value;
    public static CardRole? Find(CardRoleSet roles, string entityId) => roles.Cards.FirstOrDefault(c => c.Id == entityId);

    public static IEnumerable<double> Values(RoleEffect e) => new[] { e.Frontload, e.Aoe, e.Block, e.Scaling, e.Draw, e.Energy, e.HpLoss, e.VulnerableSource, e.ExhaustSource, e.BlockEvents }
        .Concat(e.Conditional is { } c ? Values(c.Effect) : []);

    public static void Validate(CardRoleSet roles, StrategyPack pack)
    {
        if (roles.GameVersion != GameVersion || roles.GameVersion != pack.GameVersion) throw new InvalidDataException("Card roles are for a different game version");
        if (roles.Cards.Any(c => c is null) || roles.Relics.Any(r => r is null)) throw new InvalidDataException("Null card role entries");
        if (roles.Cards.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != roles.Cards.Length) throw new InvalidDataException("Duplicate card role IDs");
        foreach (var role in roles.Cards)
        {
            var entity = pack.Entities.SingleOrDefault(e => e.Id == role.Id && e.Kind == EntityKind.Card) ?? throw new InvalidDataException($"Card role {role.Id} is not in the catalog");
            var card = entity.Card ?? throw new InvalidDataException($"Card role {role.Id} has no catalog metadata");
            int? Expected(bool upgraded) => card.Cost == "X" ? 2 : DeckRanking.EnergyCost(entity, upgraded);
            if (role.Type != card.Type || role.Cost != Expected(false) || role.UpgradedCost != Expected(true)) throw new InvalidDataException($"Card role {role.Id} type or cost differs from the catalog");
            var effects = new[] { role.Base, role.Upgraded, role.PerTurn, role.UpgradedPerTurn }.OfType<RoleEffect>().ToArray();
            if (role.Base is null || role.Upgraded is null || effects.SelectMany(Values).Any(v => !double.IsFinite(v) || v < 0) ||
                effects.SelectMany(Conditions).Any(c => !ConditionKinds.Contains(c.Kind)) || (role.PerTurn is null) != (role.UpgradedPerTurn is null))
                throw new InvalidDataException($"Card role {role.Id} has invalid effects");
            if (role.SetupTurns is < 0 or > 5 || !CopyBehaviors.Contains(role.CopyBehavior) || !UpgradeClasses.Contains(role.UpgradeClass) || !ModeledLevels.Contains(role.Modeled) ||
                string.IsNullOrWhiteSpace(role.Notes) || role.RareDependency.Any(id => !pack.Entities.Any(e => e.Id == id && e.Card?.Rarity == "Rare")))
                throw new InvalidDataException($"Card role {role.Id} has invalid metadata");
        }
        if (roles.Relics.Any(r => string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Name) || !RelicEffects.Contains(r.Effect) || !double.IsFinite(r.Value) || r.Value is < 0 or > 10) ||
            roles.Relics.GroupBy(r => (r.Id, r.Effect)).Any(g => g.Count() > 1))
            throw new InvalidDataException("Invalid or duplicate relic roles");
    }

    private static IEnumerable<RoleCondition> Conditions(RoleEffect e) => e.Conditional is { } c ? Conditions(c.Effect).Prepend(c) : [];
}

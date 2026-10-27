using Scout.Core;
using Xunit;

namespace Scout.Tests;

public sealed class RoleTests
{
    private static StrategyPack Pack => Json.Read<StrategyPack>(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
    private static CardRole Role(string id) => CardRoles.Find(CardRoles.Load(), "sts2." + id) ?? throw new Xunit.Sdk.XunitException("No role for " + id);
    private static IEnumerable<RoleEffect> Effects(CardRole r) => new[] { r.Base, r.Upgraded, r.PerTurn, r.UpgradedPerTurn }.OfType<RoleEffect>()
        .SelectMany(e => e.Conditional is { } c ? new[] { e, c.Effect } : [e]);

    [Fact]
    public void RolesLoadAndMatchPatch()
    {
        var roles = CardRoles.Load();
        Assert.Equal("0.107.1", roles.GameVersion);
        CardRoles.Validate(roles, Pack);
        Assert.All(roles.Cards, r => Assert.Contains(Pack.Entities, e => e.Id == r.Id));
        Assert.Null(CardRoles.Find(roles, "sts2.break"));
    }

    [Fact]
    public void EverySinglePlayerIroncladRewardCardHasARole()
    {
        var pool = Pack.Entities.Where(e => e.Card is { Color: "ironclad", MultiplayerOnly: false } c && c.Rarity is "Common" or "Uncommon" or "Rare").Select(e => e.Id).ToArray();
        Assert.Equal(80, pool.Length);
        var roles = CardRoles.Load();
        Assert.All(pool, id => Assert.NotNull(CardRoles.Find(roles, id)));
        // Beyond the reward pool only the starter cards are annotated (they define the job targets).
        Assert.Equal(new[] { "sts2.bash", "sts2.defend_ironclad", "sts2.strike_ironclad" }, roles.Cards.Select(c => c.Id).Except(pool).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UnitsAreSane()
    {
        foreach (var role in CardRoles.Load().Cards)
        {
            foreach (var e in Effects(role))
            {
                Assert.All(CardRoles.Values(e), v => Assert.True(double.IsFinite(v), role.Id));
                Assert.True(e.Frontload is >= 0 and <= 10 && e.Aoe is >= 0 and <= 10 && e.Block is >= 0 and <= 10, role.Id);
                Assert.True(e.Draw is >= 0 and <= 6 && e.Energy is >= 0 and <= 4, role.Id);
            }
            Assert.Contains(role.CopyBehavior, CardRoles.CopyBehaviors);
            Assert.Contains(role.UpgradeClass, CardRoles.UpgradeClasses);
            Assert.Contains(role.Modeled, CardRoles.ModeledLevels);
        }
    }

    [Fact]
    public void SpotChecks()
    {
        Assert.Equal(1.0, Role("strike_ironclad").Base.Frontload); Assert.Equal(1.5, Role("strike_ironclad").Upgraded.Frontload);
        Assert.Equal(1.0, Role("defend_ironclad").Base.Block);
        Assert.Equal(5.33, Role("bludgeon").Base.Frontload, 2);
        Assert.Equal(1, Role("pommel_strike").Base.Draw); Assert.Equal(2, Role("pommel_strike").Upgraded.Draw);
        Assert.Equal("nonStacking", Role("barricade").CopyBehavior);
        Assert.Equal("selfLimiting", Role("battle_trance").CopyBehavior);
        Assert.Equal("vulnerable", Role("dismantle").Base.Conditional?.Kind);
        Assert.Equal("exhaust", Role("feel_no_pain").PerTurn?.Conditional?.Kind);
        Assert.True(Role("feel_no_pain").PerTurn!.Conditional!.Effect.Block > 0);
        Assert.Equal(2, Role("bloodletting").Base.Energy); Assert.Equal(3, Role("bloodletting").Base.HpLoss);
        Assert.Equal("negative", Role("feed").UpgradeClass);
        Assert.Equal(2, Role("whirlwind").Cost);
        Assert.Equal((2, 1), (Role("dark_embrace").Cost!.Value, Role("dark_embrace").UpgradedCost!.Value));
        Assert.True(Role("tremble").SelfExhaust && Role("impervious").SelfExhaust && !Role("true_grit").SelfExhaust);
        Assert.Contains(CardRoles.Load().Relics, r => r.Id == "dingy_rug" && r.Effect == "colorlessRewards");
    }

    [Fact]
    public void ValidationRejectsBadData()
    {
        var roles = CardRoles.Load(); var pack = Pack;
        Assert.Throws<InvalidDataException>(() => CardRoles.Validate(roles with { Cards = roles.Cards.Append(roles.Cards[0] with { Id = "sts2.unknown" }).ToArray() }, pack));
        Assert.Throws<InvalidDataException>(() => CardRoles.Validate(roles with { GameVersion = "0.109.0" }, pack));
        Assert.Throws<InvalidDataException>(() => CardRoles.Validate(roles, pack with { GameVersion = "0.109.0" }));
        Assert.Throws<InvalidDataException>(() => CardRoles.Validate(roles with { Cards = roles.Cards.Append(roles.Cards[0]).ToArray() }, pack));
        Assert.Throws<InvalidDataException>(() => CardRoles.Validate(roles with { Cards = [roles.Cards[0] with { Cost = 3 }] }, pack));
        Assert.Throws<InvalidDataException>(() => CardRoles.Validate(roles with { Cards = [roles.Cards[0] with { Base = new(Frontload: double.NaN) }] }, pack));
        Assert.Throws<InvalidDataException>(() => CardRoles.Validate(roles with { Cards = [roles.Cards[0] with { Base = new(Conditional: new("moon", new(Block: 1))) }] }, pack));
        Assert.Throws<InvalidDataException>(() => CardRoles.Validate(roles with { Cards = [roles.Cards[0] with { CopyBehavior = "sometimes" }] }, pack));
        Assert.Throws<InvalidDataException>(() => CardRoles.Validate(roles with { Relics = roles.Relics.Append(roles.Relics[0]).ToArray() }, pack));
    }
}

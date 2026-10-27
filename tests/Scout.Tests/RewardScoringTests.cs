using System.Globalization;
using Scout.Core;
using Scout.Seeder;
using Xunit;

namespace Scout.Tests;

public sealed class RewardScoringTests
{
    private static readonly StrategyPack Pack = Json.Read<StrategyPack>(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
    private static readonly Scenario[] Dev = ScenarioEvaluation.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ironclad-scenarios.dev.json"));
    private static Recommendation[] Rank(Scenario s) => new RecommendationEngine(Pack).Rank(ScenarioEvaluation.Observation(s), ScenarioEvaluation.Context(s));
    private static Observation Offer(params string[] ids) => new(DateTimeOffset.Parse("2026-09-06T07:00:00Z", CultureInfo.InvariantCulture), Screen.CardReward, 1,
        ids.Select((id, slot) => new Choice(slot, "sts2." + id, 1)).ToArray(), "test", "hash");
    private static RunContext Context(int act, params (string Id, int Copies)[] cards) =>
        new("0.107.1", [], [], [], [], act, Character: "ironclad", Cards: cards.Select(c => new DeckEntry("sts2." + c.Id, c.Copies, false)).ToArray());
    private static readonly (string, int)[] Starter = [("strike_ironclad", 5), ("defend_ironclad", 4), ("bash", 1)];

    [Fact]
    public void SkipIsRankedAndCanWin()
    {
        var ranked = Rank(Dev.Single(s => s.Id == "S03"));
        Assert.Equal(RewardChoices.Skip, ranked[0].EntityId);
        Assert.Equal(RewardChoices.SkipSlot, ranked[0].Slot);
        Assert.StartsWith("No offer improves the deck enough for Act 2; skipping keeps", ranked[0].Reasons[0], StringComparison.Ordinal);
        Assert.Equal(4, ranked.Length);
    }

    [Fact]
    public void FloorOneNeverSkipsAFrontloadAttack()
    {
        var top = Rank(Dev.Single(s => s.Id == "S01"))[0];
        Assert.NotEqual(RewardChoices.Skip, top.EntityId);
        Assert.Contains(top.EntityId, new[] { "sts2.anger", "sts2.twin_strike" });
    }

    private static readonly (string, int)[] LateDeck = [("strike_ironclad", 4), ("defend_ironclad", 4), ("bash", 1), ("pommel_strike", 2), ("shrug_it_off", 2), ("twin_strike", 2),
        ("iron_wave", 2), ("uppercut", 2), ("bludgeon", 1), ("flame_barrier", 2), ("inflame", 1), ("hemokinesis", 2), ("taunt", 2), ("headbutt", 1)];

    [Fact]
    public void DeckSizeNowChangesDecisionAgainstSkip()
    {
        var offer = Offer("iron_wave", "sword_boomerang", "thunderclap");
        var engine = new RecommendationEngine(Pack);
        var early = Context(1, [.. Starter, ("pommel_strike", 1), ("shrug_it_off", 1)]); var late = Context(3, LateDeck);
        Assert.Equal((12, 28), (Deck.Ids(early).Length, Deck.Ids(late).Length));
        Assert.NotEqual(RewardChoices.Skip, engine.Rank(offer, early)[0].EntityId);
        Assert.Equal(RewardChoices.Skip, engine.Rank(offer, late)[0].EntityId);
    }

    [Fact]
    public void DegenerateDecksAreFinite()
    {
        var offer = Offer("pommel_strike", "demon_form", "bloodletting");
        var decks = new[]
        {
            Context(1, ("strike_ironclad", 1)), Context(1, ("bash", 2)), Context(2, ("strike_ironclad", 1), ("defend_ironclad", 1), ("bash", 1)),
            Context(2, [.. Starter.Select(s => (s.Item1, s.Item2 * 4))]), Context(3, [.. Starter.Select(s => (s.Item1, s.Item2 * 6))]),
            Context(2, ("inflame", 3), ("demon_form", 2), ("barricade", 2), ("pyre", 2)), Context(1, ("anger", 4), ("bloodletting", 3), ("battle_trance", 3)),
            Context(1, ("offering", 3), ("battle_trance", 3)), Context(4, ("ascenders_bane", 5)),
        };
        var engine = new RecommendationEngine(Pack);
        foreach (var deck in decks)
        {
            var ranked = engine.Rank(offer, deck);
            Assert.Equal(4, ranked.Length);
            Assert.All(ranked, r => Assert.True(double.IsFinite(r.Score), Json.Write(deck)));
            Assert.All(ranked.SelectMany(r => r.Reasons), reason => Assert.DoesNotContain("NaN", reason, StringComparison.Ordinal));
            var expected = Json.Write(ranked);
            for (var i = 0; i < 3; i++) Assert.Equal(expected, Json.Write(engine.Rank(offer, deck)));
            var old = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Assert.Equal(expected, Json.Write(engine.Rank(offer, deck))); }
            finally { CultureInfo.CurrentCulture = old; }
        }
        var profile = RewardScoring.Profile(CardRoles.Load(), [], Stage.Act1, Context(1));
        Assert.All(profile.Jobs.Values, v => Assert.True(double.IsFinite(v)));
    }

    [Fact]
    public void UnannotatedDeckCardsAreFiller()
    {
        var ranked = new RecommendationEngine(Pack).Rank(Offer("anger", "twin_strike", "iron_wave"), Context(1, [.. Starter, ("break", 1), ("ascenders_bane", 1)]));
        Assert.Equal(4, ranked.Length);
        Assert.Contains(ranked[0].Reasons, r => r.Contains("no role data (Break, Ascender's Bane)", StringComparison.Ordinal) || r.Contains("no role data (Ascender's Bane, Break)", StringComparison.Ordinal));
        Assert.Contains(ranked[0].Reasons, r => r.Contains("filler", StringComparison.Ordinal));
    }

    [Fact]
    public void SkipNeverReachesEntityLookup()
    {
        var skip = Rank(Dev.Single(s => s.Id == "S03"))[0];
        Assert.Equal("Skip (take no card)", DeckRanking.DisplayName(Pack, skip));
        Assert.Equal("Anger", DeckRanking.DisplayName(Pack, new Recommendation(0, "sts2.anger", 1, [])));
    }

    [Fact]
    public void StageFollowsActFloorAndDeckSize()
    {
        Assert.Equal(Stage.Act1Early, RewardScoring.StageOf(Context(1), 13));
        Assert.Equal(Stage.Act1, RewardScoring.StageOf(Context(1), 14));
        Assert.Equal(Stage.Act1Early, RewardScoring.StageOf(Context(1) with { Floor = 4 }, 20));
        Assert.Equal(Stage.Act1, RewardScoring.StageOf(Context(1) with { Floor = 5 }, 10));
        Assert.Equal(Stage.Act2, RewardScoring.StageOf(Context(2), 10));
        Assert.Equal(Stage.Act3, RewardScoring.StageOf(Context(4), 10));
    }

    [Fact]
    public void IroncladUsesLedgerAndOtherCharactersKeepLegacyPath()
    {
        var engine = new RecommendationEngine(Pack);
        var ironclad = engine.Rank(Offer("anger", "twin_strike", "iron_wave"), Context(1, Starter));
        Assert.Contains(ironclad, r => r.EntityId == RewardChoices.Skip);
        Assert.StartsWith("Needs: ", ironclad.First(r => r.EntityId != RewardChoices.Skip).Reasons[0], StringComparison.Ordinal);
        var silent = new RunContext("0.107.1", [], [], [], [], Character: "silent", Cards: [new("sts2.strike_silent", 5, false), new("sts2.defend_silent", 5, false)]);
        var pool = CatalogValidation.Pool(Pack, "silent").Where(e => e.Card!.Color == "silent").Take(3).Select(e => e.Id[5..]).ToArray();
        var legacy = engine.Rank(Offer(pool), silent);
        Assert.Equal(3, legacy.Length);
        Assert.DoesNotContain(legacy, r => r.EntityId == RewardChoices.Skip);
        Assert.All(legacy, r => Assert.Contains(r.Reasons, reason => reason.Contains("neutral baseline", StringComparison.Ordinal)));
    }

    private static int Position(Recommendation[] ranked, string id) => Array.FindIndex(ranked, r => r.EntityId == id);

    [Fact]
    public void UnsupportedVulnerablePayoffLoses()
    {
        var ranked = Rank(Dev.Single(s => s.Id == "S07"));
        Assert.True(Position(ranked, "sts2.pommel_strike") < Position(ranked, "sts2.vicious"));
    }

    [Fact]
    public void SupportedPayoffWins() => Assert.Equal("sts2.dominate", Rank(Dev.Single(s => s.Id == "S11"))[0].EntityId);

    [Fact]
    public void NonStackingDuplicateIsNotTaken()
    {
        var ranked = Rank(Dev.Single(s => s.Id == "S06"));
        Assert.True(Position(ranked, "sts2.impervious") < Position(ranked, "sts2.barricade"));
        Assert.True(Position(ranked, RewardChoices.Skip) < Position(ranked, "sts2.barricade"));
        Assert.Contains(ranked.Single(r => r.EntityId == "sts2.barricade").Reasons, r => r.Contains("does not stack", StringComparison.Ordinal));
    }

    [Fact]
    public void UsefulDuplicateIsTaken() => Assert.Equal("sts2.dismantle", Rank(Dev.Single(s => s.Id == "S05"))[0].EntityId);

    [Fact]
    public void UnmodeledNeverWinsByDefault()
    {
        var s = Dev.Single(x => x.Id == "S11"); var roles = CardRoles.Load();
        var stripped = roles with { Cards = roles.Cards.Where(c => c.Id != "sts2.dominate").ToArray() };
        var ranked = RewardScoring.Rank(Pack, stripped, ScenarioEvaluation.Observation(s), ScenarioEvaluation.Context(s));
        var dominate = ranked.Single(r => r.EntityId == "sts2.dominate");
        Assert.True(Position(ranked, "sts2.dominate") >= 1);
        Assert.True(dominate.CloseCall);
        Assert.StartsWith("Scout can't fully evaluate Dominate; judge it yourself", dominate.Reasons[0], StringComparison.Ordinal);
        // A colorless offer is unmodeled in this version and is scored at the median of the modeled offers.
        var colorless = Rank(Dev.Single(x => x.Id == "S20"));
        Assert.NotEqual("sts2.shockwave", colorless[0].EntityId);
        Assert.Contains(colorless.Single(r => r.EntityId == "sts2.shockwave").Reasons, r => r.StartsWith("Scout can't fully evaluate Shockwave", StringComparison.Ordinal));
    }

    [Fact]
    public void CloseCallFlaggedForNearTie()
    {
        var ranked = Rank(Dev.Single(s => s.Id == "S05"));
        Assert.True(ranked[0].CloseCall && ranked[1].CloseCall);
        Assert.Contains(ranked[0].Reasons, r => r.StartsWith("Close call vs " + DeckRanking.DisplayName(Pack, ranked[1]) + ": ", StringComparison.Ordinal));
        Assert.False(ranked[2].CloseCall && ranked[3].CloseCall);
        // A clear decision is not flagged: Dominate with four Vulnerable sources leads under every perturbation.
        Assert.DoesNotContain(Rank(Dev.Single(s => s.Id == "S11")), r => r.CloseCall);
    }

    [Fact]
    public void PartlyModeledCardsSayWhatIsMissing()
    {
        var ranked = Rank(Dev.Single(s => s.Id == "S18"));
        Assert.Contains(ranked.Single(r => r.EntityId == "sts2.juggling").Reasons, r => r.StartsWith("Partly modeled: ", StringComparison.Ordinal));
    }

    [Fact]
    public void LateBaseCopiesOfLargeUpgradeCardsAreDiscounted()
    {
        var ranked = new RecommendationEngine(Pack).Rank(Offer("pommel_strike", "iron_wave", "twin_strike"), Context(3, LateDeck));
        Assert.Contains(ranked.Single(r => r.EntityId == "sts2.pommel_strike").Reasons, r => r.StartsWith("Act 3: fewer upgrades remain", StringComparison.Ordinal));
        Assert.DoesNotContain(ranked.Single(r => r.EntityId == "sts2.iron_wave").Reasons, r => r.StartsWith("Act 3:", StringComparison.Ordinal));
    }

    [Fact]
    public void SupportedDominateBeatsDemonFormInAct1()
    {
        // Research §10.2: in Act 1, Dominate with Vulnerable support (here Bash, Tremble, Uppercut) is preferred over the slow Demon Form.
        // Inflame is not asserted: its consensus over Demon Form is weaker in §10.2, and the model rates per-turn Strength highly.
        var deck = Context(1, [.. Starter, ("tremble", 1), ("pommel_strike", 1), ("twin_strike", 1), ("shrug_it_off", 1), ("uppercut", 1)]) with { Floor = 8 };
        var ranked = new RecommendationEngine(Pack).Rank(Offer("dominate", "inflame", "demon_form"), deck);
        Assert.Equal(Stage.Act1, RewardScoring.StageOf(deck, 15));
        Assert.True(ranked.Single(r => r.EntityId == "sts2.dominate").Score > ranked.Single(r => r.EntityId == "sts2.demon_form").Score,
            string.Join(" ", ranked.Select(r => r.EntityId + "=" + r.Score.ToString(CultureInfo.InvariantCulture))));
    }

    [Fact]
    public void LowHpPrefersImmediateBlock()
    {
        var s10 = Dev.Single(s => s.Id == "S10");
        Assert.Equal((30, 80), (s10.Hp, s10.MaxHp));
        var low = Rank(s10);
        Assert.Equal("sts2.flame_barrier", low[0].EntityId);
        Assert.Contains(low[0].Reasons, r => r.StartsWith("HP 30/80: block need ×1.5, setup ×0.7, self-damage weight 1.5", StringComparison.Ordinal));
        var high = Rank(s10 with { Hp = 75 });
        double Margin(Recommendation[] r) => r.Single(x => x.EntityId == "sts2.flame_barrier").Score - r.Single(x => x.EntityId == "sts2.inflame").Score;
        Assert.True(Margin(low) > Margin(high), $"{Margin(low)} vs {Margin(high)}");
    }

    [Fact]
    public void RelicAddsVulnerableSource()
    {
        var engine = new RecommendationEngine(Pack); var offer = Offer("vicious", "dominate", "iron_wave");
        var plain = engine.Rank(offer, Context(1, Starter));
        var marbles = engine.Rank(offer, Context(1, Starter) with { Relics = ["bag_of_marbles"] });
        foreach (var id in new[] { "sts2.vicious", "sts2.dominate" })
            Assert.True(marbles.Single(r => r.EntityId == id).Score > plain.Single(r => r.EntityId == id).Score, id);
        Assert.Contains(marbles.Single(r => r.EntityId == "sts2.vicious").Reasons, r => r.Contains("Vulnerable sources in deck: 1.4 (Bash, Bag of Marbles (relic))", StringComparison.Ordinal));
        Assert.Contains(marbles[0].Reasons, r => r.StartsWith("Relics applied: Bag of Marbles (vulnerableSource 0.6)", StringComparison.Ordinal));
        var unknown = engine.Rank(offer, Context(1, Starter) with { Relics = ["mystery_box"] });
        Assert.Equal(plain.Select(r => r.Score), unknown.Select(r => r.Score));
        Assert.Contains(unknown[0].Reasons, r => r == "Relic(s) not modeled, ignored: mystery_box");
    }

    [Fact]
    public void HpLossReliefReducesSelfDamageCost()
    {
        var engine = new RecommendationEngine(Pack); var offer = Offer("hemokinesis", "bloodletting", "iron_wave");
        double Score(RunContext c, string id) => engine.Rank(offer, c).Single(r => r.EntityId == id).Score;
        var deck = Context(2, [.. Starter, ("pommel_strike", 1), ("bludgeon", 1), ("uppercut", 1)]);
        foreach (var id in new[] { "sts2.hemokinesis", "sts2.bloodletting" })
        {
            Assert.True(Score(deck with { Relics = ["self_forming_clay"] }, id) > Score(deck, id), id);
            Assert.True(Score(deck with { Relics = ["demon_tongue"] }, id) > Score(deck with { Relics = ["self_forming_clay"] }, id), id);
        }
        Assert.Equal(Score(deck, "sts2.iron_wave"), Score(deck with { Relics = ["demon_tongue"] }, "sts2.iron_wave"));
    }

    [Fact]
    public void ColorlessExcludedFromOcrPoolWithoutDingyRug()
    {
        Assert.DoesNotContain(CatalogValidation.RewardPool(Pack, "ironclad", []), e => e.Card!.Color == "colorless");
        Assert.DoesNotContain(CatalogValidation.RewardPool(Pack, "ironclad", ["bag_of_marbles"]), e => e.Card!.Color == "colorless");
        Assert.Contains(CatalogValidation.RewardPool(Pack, "ironclad", ["dingy_rug"]), e => e.Id == "sts2.shockwave");
        Assert.Contains(CatalogValidation.RewardPool(Pack, "ironclad", []), e => e.Id == "sts2.anger");
        // Manually entered colorless offers are still accepted without the relic.
        var s20 = Dev.Single(s => s.Id == "S20");
        Assert.Null(DeckRanking.BlockReason(Pack, ScenarioEvaluation.Observation(s20), ScenarioEvaluation.Context(s20) with { Relics = [] }));
    }

    [Fact]
    public void InvalidHpRejected()
    {
        var engine = new RecommendationEngine(Pack); var offer = Offer("anger", "twin_strike", "iron_wave");
        foreach (var bad in new[] { Context(1, Starter) with { Hp = 0, MaxHp = 80 }, Context(1, Starter) with { Hp = -5, MaxHp = 80 }, Context(1, Starter) with { Hp = 81, MaxHp = 80 },
            Context(1, Starter) with { MaxHp = 0 }, Context(1, Starter) with { MaxHp = 1000 } })
        {
            var ex = Assert.Throws<InvalidDataException>(() => Deck.ValidateRun(bad));
            Assert.Equal("HP must be between 1 and max HP", ex.Message);
            Assert.Equal("HP must be between 1 and max HP", DeckRanking.BlockReason(Pack, offer, bad));
            Assert.Empty(engine.Rank(offer, bad));
        }
        Assert.Throws<InvalidDataException>(() => Deck.ValidateRun(Context(1) with { Floor = 0 }));
        Assert.Throws<InvalidDataException>(() => Deck.ValidateRun(Context(1) with { Floor = 61 }));
        Deck.ValidateRun(Context(1) with { Hp = 80, MaxHp = 80, Floor = 60 });
        Deck.ValidateRun(Context(1));
    }

    [Theory]
    [InlineData("strike_dummy", "twin_strike", "iron_wave")]
    [InlineData("pen_nib", "bludgeon", "iron_wave")]
    [InlineData("charons_ashes", "true_grit", "iron_wave")]
    [InlineData("toasty_mittens", "feel_no_pain", "iron_wave")]
    [InlineData("ruined_helmet", "inflame", "iron_wave")]
    [InlineData("paper_phrog", "dismantle", "iron_wave")]
    public void RelicEffectsFavorTheirCards(string relic, string favored, string neutral)
    {
        var engine = new RecommendationEngine(Pack); var offer = Offer(favored, neutral, "anger");
        var deck = Context(2, [.. Starter, ("pommel_strike", 1), ("tremble", 1), ("shrug_it_off", 1)]);
        double Gap(RunContext c) { var r = engine.Rank(offer, c); return r.Single(x => x.EntityId == "sts2." + favored).Score - r.Single(x => x.EntityId == "sts2." + neutral).Score; }
        Assert.True(Gap(deck with { Relics = [relic] }) > Gap(deck), relic);
    }

    [Fact]
    public void FlatRelicsAreFiniteAndDeterministic()
    {
        var engine = new RecommendationEngine(Pack); var offer = Offer("inflame", "shrug_it_off", "anger");
        var deck = Context(3, LateDeck) with { Relics = [.. CardRoles.Load().Relics.Select(r => r.Id).Distinct()] };
        var expected = Json.Write(engine.Rank(offer, deck));
        Assert.All(engine.Rank(offer, deck), r => Assert.True(double.IsFinite(r.Score)));
        Assert.Equal(expected, Json.Write(engine.Rank(offer, deck)));
    }
}

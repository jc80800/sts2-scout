using Scout.Core;
using Xunit;
namespace Scout.Tests;

public sealed class AutomaticTests
{
    private static StrategyPack Pack => Json.Read<StrategyPack>(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
    [Fact] public void PopulatedCatalogCoversAllFiveSoloPools()
    {
        var p = Pack; PackValidation.Validate(p);
        Assert.Equal("v0.107.1", p.GameVersion); Assert.Equal(577, p.Entities.Count(e => e.Kind == EntityKind.Card));
        Assert.Equal(286, p.Entities.Count(e => e.Kind == EntityKind.Relic)); Assert.Equal(63, p.Entities.Count(e => e.Kind == EntityKind.Potion));
        foreach (var c in new[] { "ironclad", "silent", "defect", "regent", "necrobinder" })
            Assert.Equal(80, p.Entities.Count(e => e.Facts is { RewardPool: true, MultiplayerOnly: false } f && f.Character == c));
        Assert.All(p.Entities.Where(e => e.Facts!.RewardPool), e => { Assert.True(e.Baseline > 0); Assert.NotEmpty(e.BaselineReason!); });
        Assert.DoesNotContain(p.Entities, e => e.Id == "card:abundance");
        Assert.Equal("Uncommon", p.Entities.Single(e => e.Id == "card:taunt").Facts!.Rarity);
    }
    [Theory] [InlineData("id")] [InlineData("name")] [InlineData("normalized")] [InlineData("character")] [InlineData("type")] [InlineData("version")] [InlineData("source")] [InlineData("duplicateName")]
    public void CatalogRejectsInvalidEntries(string mutation)
    {
        var p = Pack; var e = p.Entities[0]; var f = e.Facts!;
        e = mutation switch
        {
            "id" => e with { Id = p.Entities[1].Id }, "name" => e with { Name = "" },
            "normalized" => e with { Facts = f with { NormalizedName = "wrong" } },
            "character" => e with { Facts = f with { Character = "watcher" } },
            "type" => e with { Facts = f with { Type = "Wrong" } },
            "version" => e with { Facts = f with { GameVersion = "beta" } },
            "source" => e with { Facts = f with { SourceId = "missing" } },
            "duplicateName" => e with { Name = p.Entities[1].Name, Facts = f with { NormalizedName = p.Entities[1].Facts!.NormalizedName, Character = p.Entities[1].Facts!.Character } },
            _ => e
        };
        Assert.Throws<InvalidDataException>(() => PackValidation.Validate(p with { Entities = [e, .. p.Entities.Skip(1)] }));
    }
    [Fact] public void CatalogRejectsEmptyAndMixedBranchSources()
    {
        var p = Pack;
        Assert.Throws<InvalidDataException>(() => PackValidation.Validate(p with { Entities = [] }));
        Assert.Throws<InvalidDataException>(() => PackValidation.Validate(p with { Catalog = p.Catalog! with { Sources = [p.Catalog.Sources[0] with { Branch = "beta" }] } }));
    }
    [Theory] [InlineData("  Pommel  Strike+ ", "card:pommel_strike")] [InlineData("Well–Laid Plans", "card:well_laid_plans")] [InlineData("PommeI Strike", "card:pommel_strike")]
    public void NamesNormalizeAndTolerateLimitedOcrErrors(string text, string expected)
    {
        Assert.Equal(expected, new CardNameMatcher(Pack).Match(text, .96)?.EntityId);
    }
    [Theory] [InlineData("random garbage", .99)] [InlineData("Bash", .2)] [InlineData("Strike", .99)] [InlineData("Ba5h", .99)]
    public void NamesRefuseUnknownLowConfidenceAndAmbiguous(string text, double confidence) => Assert.Null(new CardNameMatcher(Pack).Match(text, confidence));
    [Fact] public void CharacterDisambiguatesSharedStarterName()
    {
        var match = new CardNameMatcher(Pack).Match("Strike", .99, "ironclad"); Assert.NotNull(match);
        Assert.Equal("ironclad", Pack.Entities.Single(e => e.Id == match.EntityId).Facts!.Character);
    }
    private sealed class ScriptedOcr(string[] text) : IOfflineOcr
    {
        public int Calls { get; private set; }
        public OcrLine[] Read(GrayFrame crop) { crop.Validate(); var index = Calls++ / 3; return index < text.Length ? [new(text[index], .96, new(0, 0, 1, 1))] : []; }
    }
    private static GrayFrame Frame => new(1280, 720, Enumerable.Range(0, 1280 * 720).Select(i => (byte)(i % 255)).ToArray());
    [Fact] public void RecognitionMapsNamesStabilizesAndRanksWithoutTemplates()
    {
        var p = Pack; var ocr = new ScriptedOcr(["Choose a Card", "Skip", "Pommel Strike", "Breakthrough", "Juggling"]);
        var observation = new CardRewardRecognizer(ocr, p).Recognize(Frame, DateTimeOffset.UtcNow);
        Assert.Equal(Screen.CardReward, observation.Screen); Assert.Equal(["card:pommel_strike", "card:breakthrough", "card:juggling"], observation.Choices.Select(c => c.EntityId));
        var filter = new StabilityFilter(); Assert.Null(filter.Push(observation)); Assert.Null(filter.Push(observation)); Assert.NotNull(filter.Push(observation));
        Assert.Equal(3, new RecommendationEngine(p).Rank(observation, new(p.GameVersion, [], [], [], [])).Length);
        Assert.Equal(15, ocr.Calls);
    }
    [Theory] [InlineData("Choose a Card", "Skip", "unreadable")] [InlineData("Upgrade a Card", "Skip", "Pommel Strike")] [InlineData("Choose a Card", "Cancel", "Pommel Strike")]
    public void RecognitionRequiresRewardAnchorsAndAllThreeNames(string header, string footer, string name)
    {
        Assert.Equal(Screen.Unknown, new CardRewardRecognizer(new ScriptedOcr([header, footer, name, "Breakthrough", "Juggling"]), Pack).Recognize(Frame, DateTimeOffset.UtcNow).Screen);
    }
    [Fact] public void BlankFrameDoesNotInvokeOcr()
    {
        var ocr = new ScriptedOcr([]); Assert.Equal(Screen.Unknown, new CardRewardRecognizer(ocr, Pack).Recognize(new(1280,720,new byte[1280*720]), DateTimeOffset.UtcNow).Screen); Assert.Equal(0,ocr.Calls);
    }
    [Fact] public void StrategyUsesKnownNeedsSupportAndRedundancy()
    {
        var p = Pack; var engine = new RecommendationEngine(p);
        var obs = new Observation(DateTimeOffset.UtcNow, Screen.CardReward, .99, [new(0,"card:accelerant",.99),new(1,"card:dagger_spray",.99)], "test","hash");
        var baseDeck = new[] { "card:neutralize", "card:survivor", "card:backflip", "card:leg_sweep", "card:prepared" };
        var emptySupport = new RunContext(p.GameVersion, baseDeck, [], [], []);
        var support = emptySupport with { Deck = [.. baseDeck, "card:deadly_poison", "card:poisoned_stab"] };
        var a = engine.Rank(obs,emptySupport); var b = engine.Rank(obs,support);
        Assert.True(b.Single(r=>r.EntityId=="card:accelerant").Score > a.Single(r=>r.EntityId=="card:accelerant").Score);
        Assert.Contains(a.Single(r=>r.EntityId=="card:dagger_spray").Reasons,r=>r.Contains("short on aoe"));
        var copies=engine.Rank(obs, support with { Deck=[.. support.Deck,"card:dagger_spray","card:dagger_spray","card:dagger_spray"] });
        Assert.True(copies.Single(r=>r.EntityId=="card:dagger_spray").Score < b.Single(r=>r.EntityId=="card:dagger_spray").Score);
        Assert.Empty(engine.Rank(obs,support with { GameVersion="v0.111.0" }));
    }
}

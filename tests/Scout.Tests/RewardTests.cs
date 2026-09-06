using System.Text.Json;
using Scout.Core;
using Xunit;

namespace Scout.Tests;

public sealed class RewardTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static StrategyPack Pack => Json.Read<StrategyPack>(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
    private static RunContext Context => new("0.107.1", [], [], [], [], Character: "ironclad", Cards: [new("sts2.bash", 3, false), new("sts2.pommel_strike", 2, true), new("sts2.defend_ironclad", 5, false)]);
    private static Observation Reward => new(DateTimeOffset.Parse("2026-09-06T07:00:00Z"), Screen.CardReward, 1,
        [new(0, "sts2.pommel_strike", 1, Upgraded: true), new(1, "sts2.vicious", 1, Upgraded: true), new(2, "sts2.cinder", 1, Upgraded: true)], "synthetic", "hash");
    [Fact]
    public void CatalogHasCompleteProvenancedMechanics()
    {
        PackValidation.Validate(Pack); Assert.Equal(577, Pack.Entities.Length);
        Assert.All(Pack.Entities, e => { Assert.NotNull(e.Card); Assert.NotEmpty(e.Card!.Mechanics); Assert.NotEmpty(e.Card.UpgradeChanges); });
        Assert.Contains("vulnerable-payoff", Pack.Entities.Single(e => e.Id == "sts2.vicious").Tags);
        Assert.DoesNotContain("vulnerable", Pack.Entities.Single(e => e.Id == "sts2.vicious").Tags);
    }
    [Fact]
    public void PartialDuplicateAndBadProvenanceCatalogsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => PackValidation.Validate(Pack with { Entities = Pack.Entities.Skip(1).ToArray() }));
        Assert.Throws<InvalidDataException>(() => PackValidation.Validate(Pack with { Entities = Pack.Entities.Append(Pack.Entities[0]).ToArray() }));
        Assert.Throws<InvalidDataException>(() => PackValidation.Validate(Pack with { Catalog = Pack.Catalog! with { SourceHash = "bad" } }));
    }
    [Theory]
    [InlineData(" POMMEL   STRIKE + ", "sts2.pommel_strike", true)]
    [InlineData("Pommel Slrike+", "sts2.pommel_strike", true)]
    [InlineData("Vicious", "sts2.vicious", false)]
    [InlineData("vicious+", "sts2.vicious", true)]
    public void NamesNormalizeSafely(string text, string id, bool upgraded)
    {
        var match = CardNames.Match(new(text, .95, "test"), CatalogValidation.Pool(Pack, "ironclad"));
        Assert.Equal(id, match.EntityId); Assert.Equal(upgraded, match.Upgraded);
    }
    [Theory]
    [InlineData("Unrelated nonsense")]
    [InlineData("Vic")]
    [InlineData("Cinder+ junk")]
    [InlineData("Afterimage")]
    public void UnknownAndOutsidePoolNamesAreRejected(string text) => Assert.Null(CardNames.Match(new(text, 1, "test"), CatalogValidation.Pool(Pack, "ironclad")).EntityId);
    [Fact]
    public void AmbiguityAndLowEngineConfidenceAreRejected()
    {
        var pool = new[] { new Entity("a", "Alpha Strike", EntityKind.Card, 5, []), new Entity("b", "Alphi Strike", EntityKind.Card, 5, []) };
        Assert.Null(CardNames.Match(new("Alphe Strike", 1, "test"), pool).EntityId);
        Assert.Null(CardNames.Match(new("Cinder", .59, "test"), CatalogValidation.Pool(Pack, "ironclad")).EntityId);
    }
    [Fact]
    public void UpgradedOwnedCardsUseTheirActualEnergyCost()
    {
        var card = Pack.Entities.Single(e => e.Id == "sts2.dark_embrace");
        Assert.Equal(2, DeckRanking.EnergyCost(card, false));
        Assert.Equal(1, DeckRanking.EnergyCost(card, true));
    }
    [Fact]
    public void LegacyDeckMigrationPreservesCopiesAndSeparateUpgrades()
    {
        var legacy = new RunContext("0.107.1", ["sts2.bash", "sts2.bash"], [], [], []);
        var migrated = Deck.Migrate(JsonSerializer.Deserialize<RunContext>(Json.Write(legacy), Json.Options)!);
        Assert.Equal(new DeckEntry("sts2.bash", 2, false), Assert.Single(migrated.Cards!));
        var mixed = migrated with { Cards = [new("sts2.bash", 2, false), new("sts2.bash", 1, true)] };
        Deck.Validate(mixed, Pack); Assert.Equal(3, Deck.Ids(mixed).Length);
        Assert.Throws<InvalidDataException>(() => Deck.Validate(mixed with { Cards = [new("sts2.bash", 0, true)] }, Pack));
    }
    [Fact]
    public void DeckFeaturesChangeRankingWithConcreteReasons()
    {
        var ranked = new RecommendationEngine(Pack).Rank(Reward, Context);
        Assert.Equal("sts2.vicious", ranked[0].EntityId);
        Assert.Contains(ranked[0].Reasons, r => r.Contains("3 Vulnerable sources"));
        var unsupported = Context with { Cards = [new("sts2.defend_ironclad", 10, false)] };
        Assert.NotEqual("sts2.vicious", new RecommendationEngine(Pack).Rank(Reward, unsupported)[0].EntityId);
        Assert.Contains(ranked.Single(r => r.EntityId == "sts2.pommel_strike").Reasons, r => r.Contains("2 copies"));
    }
    [Fact]
    public void RankingsAreDeterministicAndUpgradesMatter()
    {
        var engine = new RecommendationEngine(Pack); var expected = Json.Write(engine.Rank(Reward, Context));
        for (var i = 0; i < 10; i++) Assert.Equal(expected, Json.Write(engine.Rank(Reward, Context)));
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try { System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR"); Assert.Equal(expected, Json.Write(engine.Rank(Reward, Context))); }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        Assert.Contains(engine.Rank(Reward, Context)[0].Reasons, r => r.Contains("copy is upgraded"));
        var baseCards = Reward with { Choices = Reward.Choices.Select(c => c with { Upgraded = false }).ToArray() };
        Assert.NotEqual(expected, Json.Write(engine.Rank(baseCards, Context)));
    }
    [Fact]
    public void AnyUnknownMissingSlotDeckOrVersionWithholdsAllRecommendations()
    {
        var engine = new RecommendationEngine(Pack);
        Assert.Empty(engine.Rank(Reward with { Choices = [Reward.Choices[0], Reward.Choices[1], new(2, null, 0)] }, Context));
        Assert.Empty(engine.Rank(Reward with { Choices = Reward.Choices.Take(2).ToArray() }, Context));
        Assert.Empty(engine.Rank(Reward, Context with { GameVersion = "0.110.0" }));
        Assert.Empty(engine.Rank(Reward, Context with { Cards = [] }));
        Assert.Empty(engine.Rank(Reward, Context with { Character = "silent" }));
    }
    [Fact]
    public void InvalidUpdatesKeepLastValidCache()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var paths = new ScoutPaths(temp); PackCache.Install(paths, Pack, "0.107.1");
            PackCache.Load(paths, Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"), "0.107.1");
            Assert.Throws<InvalidDataException>(() => PackCache.Install(paths, Pack with { GameVersion = "bad" }, "0.107.1"));
            paths.Write("strategy-pack.json", "{malformed");
            var loaded = PackCache.Load(paths, "does-not-exist", "0.107.1");
            Assert.Equal(Pack.PackVersion, loaded.Pack.PackVersion); Assert.Contains("Rejected", loaded.Status);
        }
        finally { Directory.Delete(temp, true); }
    }
    [Theory]
    [InlineData("ocr-plain.pgm")]
    [InlineData("ocr-outlined.pgm")]
    public void NativeOcrReadsThreeSyntheticSlotsWithoutCardTemplates(string fixture)
    {
        using var ocr = new TesseractOcr(Path.Combine(AppContext.BaseDirectory, "data", "ocr"));
        var recognizer = new RewardRecognizer(Json.Read<Calibration>(Fixture("ocr-calibration.json")), Pack, "ironclad", ocr);
        var replay = recognizer.Replay(GrayFrame.ReadPgm(Fixture(fixture)), DateTimeOffset.UnixEpoch);
        Assert.Equal(Screen.CardReward, replay.Observation.Screen);
        Assert.Equal(new[] { "sts2.pommel_strike", "sts2.vicious", "sts2.cinder" }, replay.Observation.Choices.Select(c => c.EntityId));
        Assert.All(replay.Observation.Choices, c => Assert.True(c.Upgraded));
        Assert.Equal(3, replay.Slots.Length); Assert.All(replay.Slots, s => Assert.NotEmpty(s.Readings));
        var blank = new GrayFrame(960, 540, new byte[960 * 540]);
        Assert.Equal(Screen.Unknown, recognizer.Replay(blank, DateTimeOffset.UnixEpoch).Observation.Screen);
    }
    [Fact]
    public void PgmRoundTripAndMalformedInput()
    {
        var frame = GrayFrame.ReadPgm(Fixture("ocr-plain.pgm")); Assert.Equal(960, frame.Width); Assert.Equal(540, frame.Height); Assert.Equal(64, frame.Hash.Length);
        Assert.Throws<InvalidDataException>(() => new GrayFrame(10, 10, []).Validate());
    }
    [Fact]
    public void DuplicateRegionsAndConflictingOcrAreRejected()
    {
        var calibration = Json.Read<Calibration>(Fixture("ocr-calibration.json"));
        Assert.Throws<InvalidDataException>(() => new RewardRecognizer(calibration with { RewardNameRegions = [calibration.RewardNameRegions![0], calibration.RewardNameRegions[0], calibration.RewardNameRegions[2]] }, Pack, "ironclad", new ConflictingOcr()));
        var replay = new RewardRecognizer(calibration, Pack, "ironclad", new ConflictingOcr()).Replay(GrayFrame.ReadPgm(Fixture("ocr-plain.pgm")), DateTimeOffset.UnixEpoch);
        Assert.All(replay.Observation.Choices, c => Assert.Null(c.EntityId));
    }
    [Fact]
    public void ExistingSettingsLoadWithoutLosingPreferences()
    {
        var text = """
            {"processName":"SlayTheSpire2","opacity":0.8,"context":{"gameVersion":"0.107.1","deck":["sts2.bash","sts2.bash"],"relics":[],"needs":[],"archetypes":[]}}
            """;
        var settings = JsonSerializer.Deserialize<Scout.Windows.Settings>(text, Json.Options)!;
        settings.Validate();
        var migrated = settings with { Context = Deck.Migrate(settings.Context) };
        Assert.Equal(.8, migrated.Opacity);
        Assert.Equal(2, Assert.Single(migrated.Context.Cards!).Copies);
        Assert.Equal("unconfigured", migrated.Context.Character);
        Assert.Equal(migrated, JsonSerializer.Deserialize<Scout.Windows.Settings>(Json.Write(migrated), Json.Options)! with { Context = migrated.Context });
    }
    [Fact]
    public void NativeNamesRemainRecognizableWhenMovedBetweenSlots()
    {
        var profile = Json.Read<Calibration>(Fixture("ocr-calibration.json"));
        var frame = GrayFrame.ReadPgm(Fixture("ocr-outlined.pgm")); var pixels = frame.Pixels.ToArray();
        var starts = new[] { 70, 360, 650 };
        for (var slot = 0; slot < 3; slot++)
            for (var y = 200; y < 245; y++)
                Array.Copy(frame.Pixels, y * frame.Width + starts[(slot + 1) % 3], pixels, y * frame.Width + starts[slot], 240);
        using var ocr = new TesseractOcr(Path.Combine(AppContext.BaseDirectory, "data", "ocr"));
        var result = new RewardRecognizer(profile, Pack, "ironclad", ocr).Replay(frame with { Pixels = pixels }, DateTimeOffset.UnixEpoch);
        Assert.Equal(new[] { "sts2.vicious", "sts2.cinder", "sts2.pommel_strike" }, result.Observation.Choices.Select(c => c.EntityId));
    }
    private sealed class ConflictingOcr : INameOcr { public OcrReading[] Read(GrayFrame crop) => [new("Cinder", 1, "a"), new("Vicious+", 1, "b")]; }
}

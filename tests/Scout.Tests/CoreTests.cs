using System.Globalization;
using Microsoft.Data.Sqlite;
using Scout.Core;
using Scout.Seeder;
using Xunit;
using Xunit.Abstractions;

namespace Scout.Tests;

public sealed class CoreTests(ITestOutputHelper output)
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static StrategyPack Pack => Json.Read<StrategyPack>(Fixture("catalog.json"));
    private static Calibration Calibration => Json.Read<Calibration>(Fixture("calibration.json"));
    private static Observation Reward => new(DateTimeOffset.UnixEpoch, Screen.CardReward, .99, [new(0, "fixture.guard", .99), new(1, "fixture.spark", .99)], "test", "hash");
    private static RunContext Context => new("fixture-v1", ["fixture.spark"], [], ["block"], [], 2);

    [Fact]
    public void FixtureMetricsAndPerturbations()
    {
        var recognizer = new TemplateRecognizer(Calibration, Pack);
        var cases = new[] { ("reward.pgm", Screen.CardReward), ("merchant.pgm", Screen.Merchant), ("reward-noisy.pgm", Screen.CardReward), ("merchant-noisy.pgm", Screen.Merchant), ("unknown.pgm", Screen.Unknown) };
        int tp = 0, fp = 0, fn = 0, unknown = 0;
        foreach (var (file, expected) in cases)
        {
            var actual = recognizer.Recognize(GrayFrame.ReadPgm(Fixture(file)), DateTimeOffset.UnixEpoch);
            if (actual.Screen == Screen.Unknown) unknown++;
            if (actual.Screen != Screen.Unknown && actual.Screen == expected) tp++;
            if (actual.Screen != Screen.Unknown && actual.Screen != expected) fp++;
            if (expected != Screen.Unknown && actual.Screen != expected) fn++;
            Assert.Equal(expected, actual.Screen);
            if (expected == Screen.CardReward) Assert.Equal(["fixture.guard", "fixture.spark"], actual.Choices.Select(c => c.EntityId));
            if (expected == Screen.Merchant) { Assert.Equal("fixture.charm", actual.Choices[0].EntityId); Assert.Equal(75, actual.Choices[0].Price); }
        }
        output.WriteLine($"Synthetic-only screen precision={tp / (double)(tp + fp):P1}, recall={tp / (double)(tp + fn):P1}, unknown={unknown / (double)cases.Length:P1}; n=5; 2 screen types; 96x54. Not STS2 accuracy.");
        Assert.Equal(4, tp); Assert.Equal(0, fp); Assert.Equal(0, fn); Assert.Equal(1, unknown);
    }
    [Fact]
    public void AmbiguousEntitiesAndWrongAspectRemainUnknown()
    {
        var profile = Calibration;
        var probe = profile.Probes.Single(p => p.Kind == "entity" && p.Screen == Screen.CardReward && p.Slot == 0);
        var ambiguous = probe with { Templates = [probe.Templates[0], probe.Templates[0] with { Label = "fixture.spark" }] };
        profile = profile with { Probes = profile.Probes.Select(p => p == probe ? ambiguous : p).ToArray() };
        var r = new TemplateRecognizer(profile, Pack);
        Assert.Null(r.Recognize(GrayFrame.ReadPgm(Fixture("reward.pgm")), DateTimeOffset.UnixEpoch).Choices[0].EntityId);
        Assert.Equal(Screen.Unknown, r.Recognize(new(54, 96, GrayFrame.ReadPgm(Fixture("reward.pgm")).Pixels), DateTimeOffset.UnixEpoch).Screen);
    }
    [Fact]
    public void ScalingAndLowConfidencePrice()
    {
        var f = GrayFrame.ReadPgm(Fixture("merchant.pgm"));
        var scaled = new GrayFrame(192, 108, TemplateRecognizer.Sample(f, new(0, 0, 1, 1), 192, 108));
        var r = new TemplateRecognizer(Calibration, Pack);
        Assert.Equal(Screen.Merchant, r.Recognize(scaled, DateTimeOffset.UnixEpoch).Screen);
        var pixels = f.Pixels.ToArray();
        for (var y = 18; y < 36; y++) for (var x = 48; x < 96; x++) pixels[y * 96 + x] = 100;
        Assert.Null(r.Recognize(f with { Pixels = pixels }, DateTimeOffset.UnixEpoch).Choices[0].Price);
    }
    [Fact]
    public void StabilityResetsAndDoesNotSpam()
    {
        var filter = new StabilityFilter(); Assert.Null(filter.Push(Reward)); Assert.Null(filter.Push(Reward)); Assert.NotNull(filter.Push(Reward)); Assert.Null(filter.Push(Reward));
        filter.Push(Reward with { Screen = Screen.Unknown }); Assert.Null(filter.Push(Reward));
    }
    [Fact]
    public void DeterministicGoldenRanking()
    {
        var engine = new RecommendationEngine(Pack);
        var ranked = engine.Rank(Reward, Context);
        Assert.Equal(new[] { 6.36, 1.46 }, ranked.Select(r => r.Score));
        Assert.Equal(new[] { "fixture.guard", "fixture.spark" }, ranked.Select(r => r.EntityId));
        var golden = Json.Write(ranked);
        for (var i = 0; i < 20; i++) Assert.Equal(golden, Json.Write(engine.Rank(Reward, Context)));
        var old = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Assert.Equal(golden, Json.Write(engine.Rank(Reward, Context))); }
        finally { CultureInfo.CurrentCulture = old; }
        Assert.Empty(engine.Rank(Reward, Context with { GameVersion = "other" }));
        Assert.Empty(engine.Rank(Reward with { Confidence = .4 }, Context));
    }
    [Fact]
    public void MerchantRequiresKnownAffordablePriceAndPricesReserve()
    {
        var engine = new RecommendationEngine(Pack);
        var shop = Reward with { Screen = Screen.Merchant, Choices = [new(0, "fixture.charm", .99, 75, .99)] };
        Assert.Empty(engine.Rank(shop, Context));
        Assert.Empty(engine.Rank(shop with { Choices = [new(0, "fixture.charm", .99, 75, .2)] }, Context with { Gold = 100 }));
        Assert.Equal(-1.5, engine.Rank(shop, Context with { Gold = 100, ReserveGold = 50 })[0].Score);
    }
    [Fact]
    public void DisappearanceIsNeverConfirmedAndCardExitIsNotASelection()
    {
        var tracker = new DecisionTracker(); var shop = Reward with { Screen = Screen.Merchant };
        Assert.Empty(tracker.Observe(shop));
        var selection = Assert.Single(tracker.Observe(shop with { Choices = [new(0, null, 0), shop.Choices[1]] }));
        Assert.False(selection.Confirmed); Assert.True(selection.Confidence < .9);
        tracker.Reset(); tracker.Observe(Reward); Assert.Empty(tracker.Observe(Reward with { Screen = Screen.Unknown }));
    }
    [Fact]
    public void DatabaseMigratesIdempotentlyAndNeverConfirmsUnknowns()
    {
        using var temp = new Temp(); var paths = new ScoutPaths(temp.Path);
        using (var db = new Scout.Storage.ScoutDatabase(paths)) db.Save(Reward with { Choices = [new(0, "fixture.guard", .2, 25, .2)] }, Pack, Context, []);
        using (var db = new Scout.Storage.ScoutDatabase(paths)) db.Save(Reward with { Confidence = .3 }, Pack, Context, []);
        using var connection = new SqliteConnection($"Data Source={paths.FilePath("scout.db")}"); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM choices WHERE entity_id IS NOT NULL OR price IS NOT NULL"; Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "PRAGMA user_version"; Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM observations"; Assert.Equal(2L, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM provenance"; Assert.Equal(1L, command.ExecuteScalar());
    }
    [Theory]
    [InlineData("../game/save.json")]
    [InlineData("C:\\Steam\\game.save")]
    [InlineData("/tmp/game")]
    [InlineData("..")]
    public void WriteCapabilityRejectsGamePaths(string name)
    {
        using var temp = new Temp(); var paths = new ScoutPaths(temp.Path); Assert.Throws<ArgumentException>(() => paths.Write(name, "x"));
    }
    [Fact]
    public void SeederRetainsConflictsAndRequiresExactReview()
    {
        using var temp = new Temp(); var evidence = Path.Combine(temp.Path, "evidence");
        var candidates = new SavedResponseExtractor().Extract(Fixture("ai-response.json"));
        var batch = Pipeline.Ingest(Pack, Json.Read<SourceManifest>(Fixture("sources.json")), candidates, Path.GetDirectoryName(Fixture("sources.json"))!, evidence);
        Assert.Equal(2, batch.Claims.Length); Assert.Empty(batch.Quarantine); Assert.Single(batch.Conflicts);
        Assert.Throws<InvalidDataException>(() => Pipeline.Compile(Pack, batch, [], "new", evidence));
        var reviews = batch.Claims.Select(c => new Review(c.Id, Pipeline.ClaimHash(c), "approved", "fixture-reviewer", DateTimeOffset.UtcNow.AddMinutes(-1))).ToArray();
        var promoted = Pipeline.Compile(Pack, batch, reviews, "new", evidence); Assert.Equal(2, promoted.Claims.Length);
        var score = new RecommendationEngine(promoted).Rank(Reward, Context)[0].Score; Assert.Equal(7.36, score);
        Assert.Throws<InvalidDataException>(() => Pipeline.Compile(Pack, batch with { Claims = [batch.Claims[0] with { Weight = 3 }, batch.Claims[1]] }, reviews, "new", evidence));
        File.WriteAllText(Path.Combine(evidence, batch.Claims[0].Source.EvidenceHash + ".evidence"), "tampered");
        Assert.Throws<InvalidDataException>(() => Pipeline.Compile(Pack, batch, reviews, "new", evidence));
    }
    [Fact]
    public void SeederQuarantinesDuplicatesUnknownsVersionsAndProvenance()
    {
        using var temp = new Temp(); var c = new SavedResponseExtractor().Extract(Fixture("ai-response.json"))[0];
        Candidate[] bad = [c, c, c with { Id = "bad-id", Subject = "missing" }, c with { Id = "bad-patch", GameVersion = "other" }, c with { Id = "bad-source", SourceUrl = "https://unapproved.example" }, c with { Id = "bad-confidence", Confidence = 2, Weight = 3 }];
        var result = Pipeline.Ingest(Pack, Json.Read<SourceManifest>(Fixture("sources.json")), bad, Path.GetDirectoryName(Fixture("sources.json"))!, temp.Path);
        Assert.Empty(result.Claims); Assert.Equal(bad.Length, result.Quarantine.Length);
        var manifest = Json.Read<SourceManifest>(Fixture("sources.json"));
        Assert.Throws<InvalidDataException>(() => Pipeline.ValidateSource(manifest with { Sources = [manifest.Sources[0] with { AllowRawRetention = false }] }, c.SourceUrl, temp.Path));
    }
    [Fact]
    public void UnknownJsonPropertiesAndUnreviewedPacksAreRejected()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<StrategyPack>("{\"schemaVersion\":1,\"unexpected\":true}", Json.Options));
        Assert.Throws<InvalidDataException>(() => PackValidation.Validate(Pack with { SchemaVersion = 2 }));
    }
    [Fact]
    public void VisualSelectionRequiresMatchingOfferAndStaysUnconfirmed()
    {
        var source = Calibration.Probes.Single(p => p.Kind == "entity" && p.Screen == Screen.CardReward && p.Slot == 0);
        var profile = Calibration with { Probes = Calibration.Probes.Append(source with { Kind = "selection" }).ToArray() };
        var observed = new TemplateRecognizer(profile, Pack).Recognize(GrayFrame.ReadPgm(Fixture("reward.pgm")), DateTimeOffset.UnixEpoch);
        Assert.Single(observed.SelectionSignals!);
        var tracker = new DecisionTracker();
        var selection = Assert.Single(tracker.Observe(observed));
        Assert.Equal("fixture.guard", selection.EntityId);
        Assert.False(selection.Confirmed);
        Assert.Empty(tracker.Observe(observed with { Choices = [new(0, null, 0)] }));
    }
    [Fact]
    public void StableChoicesTolerateMatchScoreJitter()
    {
        var filter = new StabilityFilter();
        Assert.Null(filter.Push(Reward));
        Assert.Null(filter.Push(Reward with { Confidence = .98, Choices = [new(0, "fixture.guard", .97), new(1, "fixture.spark", .98)] }));
        Assert.NotNull(filter.Push(Reward));
        Assert.True(filter.IsStable);
    }
    [Fact]
    public void DatabaseRollsBackOnPackCollisionAndRejectsFutureSchema()
    {
        using var temp = new Temp(); var paths = new ScoutPaths(temp.Path);
        using (var db = new Scout.Storage.ScoutDatabase(paths))
        {
            db.Save(Reward, Pack, Context, []);
            Assert.Throws<InvalidDataException>(() => db.Save(Reward, Pack with { Entities = [Pack.Entities[0]] }, Context, []));
        }
        using (var connection = new SqliteConnection($"Data Source={paths.FilePath("scout.db")};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM observations"; Assert.Equal(1L, command.ExecuteScalar());
            command.CommandText = "PRAGMA user_version=99"; command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => new Scout.Storage.ScoutDatabase(paths));
    }
    [Fact]
    public void GameSentinelIsUnchangedByRuntimePersistence()
    {
        using var temp = new Temp();
        var game = Path.Combine(temp.Path, "Steam", "STS2"); Directory.CreateDirectory(game);
        var save = Path.Combine(game, "save.json"); File.WriteAllText(save, "game-owned sentinel");
        var before = File.ReadAllBytes(save);
        var paths = new ScoutPaths(temp.Path);
        using (var db = new Scout.Storage.ScoutDatabase(paths)) db.Save(Reward, Pack, Context, []);
        Assert.Throws<ArgumentException>(() => paths.Write(save, "forbidden"));
        Assert.Equal(before, File.ReadAllBytes(save));
        Assert.Single(Directory.GetFiles(game));
    }
    [Fact]
    public void LinkedScoutOutputCannotWriteIntoGameDirectory()
    {
        if (OperatingSystem.IsWindows()) return; // Creating symlinks requires privileges on some Windows runners.
        using var temp = new Temp(); var paths = new ScoutPaths(temp.Path);
        var game = Path.Combine(temp.Path, "game-save"); File.WriteAllText(game, "sentinel");
        File.CreateSymbolicLink(Path.Combine(paths.Root, "scout.log"), game);
        Assert.Throws<IOException>(() => paths.Log("forbidden"));
        Assert.Equal("sentinel", File.ReadAllText(game));
    }
    [Fact]
    public void StrictJsonRejectsMissingFieldsNullsAndUnknownEnums()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<StrategyPack>("{}", Json.Options));
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<Entity>("{\"id\":null,\"name\":\"a\",\"kind\":\"Card\",\"baseline\":1,\"tags\":[]}", Json.Options));
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<Screen>("123", Json.Options));
    }

    private sealed class Temp : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scout-test-" + Guid.NewGuid().ToString("N"));
        public Temp() => Directory.CreateDirectory(Path);
        public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(Path, true); }
    }
}

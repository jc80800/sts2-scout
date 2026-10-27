using Scout.Core;
using Scout.Seeder;
using Xunit;
using Xunit.Abstractions;

namespace Scout.Tests;

public sealed class ScenarioTests(ITestOutputHelper output)
{
    // Acceptable hits on the dev set (legacy engine 8; job ledger, Task 4: 17). Later tasks raise it; never lower it.
    public const int DevRatchet = 17;
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static StrategyPack Pack => Json.Read<StrategyPack>(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));

    [Fact]
    public void DevScenariosMeetRatchet()
    {
        var scenarios = ScenarioEvaluation.Load(Fixture("ironclad-scenarios.dev.json"));
        Assert.Equal(20, scenarios.Length);
        var report = ScenarioEvaluation.Run(Pack, scenarios);
        output.WriteLine(ScenarioEvaluation.Table(scenarios, report));
        Assert.DoesNotContain(report.Results, r => r.Picked == ScenarioEvaluation.None);
        Assert.True(report.AcceptableHits >= DevRatchet, $"Dev acceptable hits {report.AcceptableHits} < ratchet {DevRatchet}");
        Assert.Equal(Json.Write(report), Json.Write(ScenarioEvaluation.Run(Pack, scenarios)));
    }

    [Fact]
    public void HeldOutScenariosReport()
    {
        // Held-out answers are written independently; this only reports and never tunes against them.
        var path = Fixture("ironclad-scenarios.heldout.json");
        if (!File.Exists(path)) { output.WriteLine("Held-out scenario file not present; nothing to report."); return; }
        var scenarios = ScenarioEvaluation.Load(path);
        output.WriteLine(ScenarioEvaluation.Summary(ScenarioEvaluation.Run(Pack, scenarios)));
    }

    [Fact]
    public void ScenarioFileIsStrictAndMetricsAreFinite()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(temp, "[]");
            Assert.Throws<InvalidDataException>(() => ScenarioEvaluation.Load(temp));
            File.WriteAllText(temp, """[{"id":"x","act":1,"ascension":0,"relics":[],"deck":[],"offers":[],"preferred":["skip"],"acceptable":[],"tags":[],"rationale":"","extra":1}]""");
            Assert.Throws<System.Text.Json.JsonException>(() => ScenarioEvaluation.Load(temp));
        }
        finally { File.Delete(temp); }
        var valid = ScenarioEvaluation.Load(Fixture("ironclad-scenarios.dev.json"))[0];
        Assert.Throws<InvalidDataException>(() => ScenarioEvaluation.Run(Pack, [valid with { Offers = valid.Offers.Take(2).ToArray() }]));
        Assert.Throws<InvalidDataException>(() => ScenarioEvaluation.Run(Pack, [valid with { Offers = [valid.Offers[0], valid.Offers[1], valid.Offers[2] with { Slot = 5 }] }]));
        Assert.Throws<InvalidDataException>(() => ScenarioEvaluation.Run(Pack, [valid with { Offers = [valid.Offers[0], valid.Offers[1], valid.Offers[2] with { EntityId = "sts2.nope" }] }]));
        Assert.Throws<InvalidDataException>(() => ScenarioEvaluation.Run(Pack, [valid with { Deck = [new("sts2.nope", 1, false)] }]));
        Assert.Throws<InvalidDataException>(() => ScenarioEvaluation.Run(Pack, [valid with { Preferred = ["sts2.bludgeon"] }]));
        Assert.Throws<InvalidDataException>(() => ScenarioEvaluation.Evaluate(Pack, valid with { Acceptable = ["sts2.bludgeon"] }));
        var summary = ScenarioEvaluation.Summary(ScenarioEvaluation.Run(Pack, [valid]));
        Assert.DoesNotContain("sts2.", summary, StringComparison.Ordinal); Assert.Contains("S01:", summary, StringComparison.Ordinal);
        var empty = ScenarioEvaluation.Run(Pack, []);
        Assert.Equal(0, empty.Total); Assert.True(double.IsFinite(empty.SkipPrecision) && double.IsFinite(empty.SkipRecall));
    }
}

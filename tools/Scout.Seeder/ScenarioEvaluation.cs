using System.Globalization;
using System.Text;
using Scout.Core;

namespace Scout.Seeder;

/// <summary>A hypothetical card-reward decision with reasoned (not empirically proven) answers. Preferred/Acceptable hold entity IDs or "skip".</summary>
public sealed record Scenario(string Id, int Act, int Ascension, int? Floor, int? Hp, int? MaxHp, string[] Relics, DeckEntry[] Deck, Choice[] Offers, string[] Preferred, string[] Acceptable, string[] Tags, string Rationale);
public sealed record ScenarioResult(string Id, string Picked, bool PreferredHit, bool AcceptableHit, bool CloseCallFlag, string[] TopReasons);
public sealed record EvaluationReport(int Total, int PreferredHits, int AcceptableHits, double SkipPrecision, double SkipRecall, ScenarioResult[] Results);

public static class ScenarioEvaluation
{
    public const string Skip = "skip";
    public const string None = "none";
    // Fixed timestamp keeps the freshness multiplier, and therefore the report, reproducible.
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-09-06T07:00:00Z", CultureInfo.InvariantCulture);

    public static Scenario[] Load(string path)
    {
        var scenarios = Json.Read<Scenario[]>(path);
        if (scenarios.Length == 0 || scenarios.Any(s => s is null) || scenarios.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != scenarios.Length)
            throw new InvalidDataException("Scenario file is empty or has duplicate IDs");
        foreach (var s in scenarios) Check(s);
        return scenarios;
    }
    private static void Check(Scenario s)
    {
        if (string.IsNullOrWhiteSpace(s.Id) || s.Preferred.Length == 0 || s.Offers.Length != 3 || !s.Offers.Select(o => o.Slot).Order().SequenceEqual([0, 1, 2]) || s.Offers.Any(o => o.EntityId == null))
            throw new InvalidDataException($"Scenario {s.Id} needs a preferred answer and exactly three offers in slots 0, 1 and 2");
        if (s.Preferred.Concat(s.Acceptable).Any(a => a != Skip && !s.Offers.Any(o => o.EntityId == a)))
            throw new InvalidDataException($"Scenario {s.Id} has an answer that is neither an offer nor \"skip\"");
    }
    private static void Check(StrategyPack pack, Scenario s)
    {
        Check(s);
        var unknown = s.Deck.Select(e => e.EntityId).Concat(s.Offers.Select(o => o.EntityId!)).FirstOrDefault(id => !pack.Entities.Any(e => e.Id == id && e.Kind == EntityKind.Card));
        if (unknown != null) throw new InvalidDataException($"Scenario {s.Id} uses {unknown}, which is not a card in the pack");
    }

    public static RunContext Context(Scenario s) => new("0.107.1", s.Deck.SelectMany(e => Enumerable.Repeat(e.EntityId, e.Copies)).ToArray(), s.Relics, [], [],
        s.Act, s.Ascension, Character: "ironclad", Cards: s.Deck, Hp: s.Hp, MaxHp: s.MaxHp, Floor: s.Floor);
    public static Observation Observation(Scenario s) => new(At, Screen.CardReward, 1, s.Offers, "scenario", s.Id);

    public static ScenarioResult Evaluate(StrategyPack pack, Scenario s)
    {
        Check(pack, s);
        var context = Context(s); var observation = Observation(s);
        var ranked = new RecommendationEngine(pack).Rank(observation, context);
        if (ranked.Length == 0) return new(s.Id, None, false, false, false, [DeckRanking.BlockReason(pack, observation, context) ?? "No recommendation"]);
        var top = ranked[0]; var picked = top.EntityId;
        var preferred = s.Preferred.Contains(picked, StringComparer.Ordinal);
        // The neutral-baseline line is identical for every card, so it is not a useful top reason.
        return new(s.Id, picked, preferred, preferred || s.Acceptable.Contains(picked, StringComparer.Ordinal), top.CloseCall,
            top.Reasons.Where(r => !r.Contains("neutral baseline", StringComparison.Ordinal)).Take(4).ToArray());
    }

    public static EvaluationReport Run(StrategyPack pack, IEnumerable<Scenario> scenarios)
    {
        var list = scenarios.ToArray();
        foreach (var s in list) Check(pack, s);
        var results = list.Select(s => Evaluate(pack, s)).ToArray();
        var skipPicks = results.Where(r => r.Picked == Skip).ToArray();
        var skipExpected = list.Where(s => s.Preferred.Contains(Skip, StringComparer.Ordinal)).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        // Empty denominators report 0 rather than NaN so the report stays finite and comparable.
        double Ratio(int hits, int total) => total == 0 ? 0 : (double)hits / total;
        return new(results.Length, results.Count(r => r.PreferredHit), results.Count(r => r.AcceptableHit),
            Ratio(skipPicks.Count(r => r.AcceptableHit), skipPicks.Length), Ratio(results.Count(r => skipExpected.Contains(r.Id) && r.Picked == Skip), skipExpected.Count), results);
    }

    public static string Table(Scenario[] scenarios, EvaluationReport report)
    {
        var byId = scenarios.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var text = new StringBuilder();
        text.AppendLine("| Scenario | Picked | Preferred | Acceptable | Hit | Close | Top reason |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var r in report.Results)
        {
            var s = byId[r.Id];
            text.AppendLine(CultureInfo.InvariantCulture, $"| {r.Id} | {r.Picked} | {string.Join(", ", s.Preferred)} | {string.Join(", ", s.Acceptable)} | {(r.PreferredHit ? "preferred" : r.AcceptableHit ? "acceptable" : "miss")} | {(r.CloseCallFlag ? "yes" : "no")} | {r.TopReasons.FirstOrDefault()?.Replace('|', '/') ?? ""} |");
        }
        return text.Append(Totals(report)).AppendLine().ToString();
    }

    /// <summary>Aggregate metrics and per-ID hit/miss only: never answers or reasons, so held-out sets stay unseen.</summary>
    public static string Summary(EvaluationReport report) =>
        Totals(report) + Environment.NewLine + string.Join(" ", report.Results.Select(r => r.Id + ":" + (r.AcceptableHit ? "hit" : "miss"))) + Environment.NewLine;
    private static string Totals(EvaluationReport report) =>
        FormattableString.Invariant($"Total {report.Total}; preferred hits {report.PreferredHits}; acceptable hits {report.AcceptableHits}; skip precision {report.SkipPrecision:0.00}; skip recall {report.SkipRecall:0.00}; close calls {report.Results.Count(r => r.CloseCallFlag)}/{report.Total} (hits {report.Results.Count(r => r.CloseCallFlag && r.AcceptableHit)}/{report.Results.Count(r => r.AcceptableHit)}, misses {report.Results.Count(r => r.CloseCallFlag && !r.AcceptableHit)}/{report.Results.Count(r => !r.AcceptableHit)})");
}

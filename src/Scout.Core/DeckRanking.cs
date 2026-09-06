namespace Scout.Core;

public static class DeckRanking
{
    public static int? EnergyCost(Entity entity, bool upgraded)
    {
        if (entity.Card is not { } card) return null;
        var value = upgraded ? card.UpgradeChanges.FirstOrDefault(c => c.StartsWith("cost:", StringComparison.Ordinal))?[5..] ?? card.Cost : card.Cost;
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var cost) && cost >= 0 ? cost : null;
    }
    public static string? BlockReason(StrategyPack pack, Observation observation, RunContext context)
    {
        if (context.GameVersion != pack.GameVersion) return "Strategy data is for a different game version";
        if (context.Character == "unconfigured") return "Character not configured — review deck to continue";
        if (Deck.Entries(context).Length == 0) return "Deck not configured — review deck to continue";
        try { Deck.Validate(context, pack); } catch (InvalidDataException ex) { return ex.Message; }
        var pool = CatalogValidation.Pool(pack, context.Character).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        if (observation.Screen != Screen.CardReward || !double.IsFinite(observation.Confidence) || observation.Confidence is < .9 or > 1 ||
            observation.Choices.Length != 3 || !observation.Choices.Select(c => c.Slot).Order().SequenceEqual(new[] { 0, 1, 2 }) ||
            observation.Choices.Any(c => c.EntityId == null || !pool.Contains(c.EntityId) || !double.IsFinite(c.Confidence) || c.Confidence is < .9 or > 1))
            return "Card name uncertain — confirm all three offers before recommendation";
        return null;
    }
    public static Recommendation[] Rank(StrategyPack pack, Observation observation, RunContext context)
    {
        if (BlockReason(pack, observation, context) != null) return [];
        var owned = Deck.Entries(context).SelectMany(entry => Enumerable.Repeat((Entity: pack.Entities.Single(e => e.Id == entry.EntityId), entry.Upgraded), entry.Copies)).ToArray();
        string[] Tags((Entity Entity, bool Upgraded) c) => c.Upgraded ? c.Entity.Card!.UpgradedTags : c.Entity.Tags;
        int Count(string tag) => owned.Count(c => Tags(c).Contains(tag));
        var result = new List<Recommendation>();
        foreach (var choice in observation.Choices.OrderBy(c => c.Slot))
        {
            var entity = pack.Entities.Single(e => e.Id == choice.EntityId); var card = entity.Card!;
            var tags = choice.Upgraded ? card.UpgradedTags : entity.Tags;
            var copies = owned.Count(c => c.Entity.Id == entity.Id);
            var reasons = new List<string>(); double score = 0;
            void Add(double value, string reason)
            {
                score += value; reasons.Add(FormattableString.Invariant($"{value:+0.##;-0.##;0}: {reason}"));
            }
            Add(entity.Baseline * card.Source.Confidence, $"neutral baseline; mechanics confidence {card.Source.Confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} (heuristic, not win probability)");
            Add(-copies * 1.5, $"your deck already contains {copies} copies of {entity.Name}");
            Add(-owned.Length * .04, $"adding to your {owned.Length}-card deck slows access to existing cards");
            if (choice.Upgraded) Add(1, "this offered copy is upgraded");
            foreach (var tag in new[] { "damage", "block", "draw", "selection", "energy", "scaling", "vulnerable", "weak" })
            {
                if (!tags.Contains(tag)) continue;
                var count = Count(tag);
                var target = tag is "damage" or "block" ? Math.Max(3, owned.Length / 4d) : tag == "scaling" ? context.Act : 2;
                var weight = Math.Clamp((target - count) / target, -.5, 1) * 2;
                // Conditional draw, e.g. Vicious, is evaluated through its trigger below.
                if (tag == "draw" && tags.Contains("conditional")) weight *= .25;
                Add(weight, $"local inference: {count}/{owned.Length} owned cards have {tag} support; act {context.Act} target {target.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}");
            }
            var expensive = owned.Count(c => EnergyCost(c.Entity, c.Upgraded) >= 2);
            if (tags.Contains("energy")) Add(Math.Min(2, expensive * .3), $"your deck has {expensive} cards costing at least 2 energy");
            if (EnergyCost(entity, choice.Upgraded) >= 2) Add(-Math.Max(0, expensive - Count("energy")) * .2, $"energy demand: {expensive} expensive cards and {Count("energy")} energy sources");
            if (tags.Contains("vulnerable-payoff")) Add(Count("vulnerable") == 0 ? -4 : Math.Min(4, Count("vulnerable") * 1.2), $"your deck has {Count("vulnerable")} Vulnerable sources to trigger {entity.Name}");
            if (tags.Contains("vulnerable")) Add(Math.Min(3, Count("vulnerable-payoff")), $"your deck has {Count("vulnerable-payoff")} Vulnerable payoff cards");
            if (tags.Contains("exhaust-payoff")) Add(Count("exhaust") == 0 ? -3 : Math.Min(3, Count("exhaust") * .5), $"your deck has {Count("exhaust")} Exhaust enablers");
            if (tags.Contains("exhaust"))
            {
                Add(Math.Min(3, Count("exhaust-payoff")), $"your deck has {Count("exhaust-payoff")} Exhaust payoffs");
                if (Count("exhaust-payoff") == 0 && copies == 0) Add(-.5, "local conflict: exhausting a card can remove a needed repeatable tool");
            }
            if (tags.Contains("status-handling")) Add(Math.Min(2, Count("burden") * .5 + Count("status-generation") * .4), $"your deck has {Count("burden")} status/curse cards and {Count("status-generation")} status generators");
            if (tags.Contains("status-generation")) Add(Count("status-handling") == 0 ? -1.5 : .5, $"your deck has {Count("status-handling")} ways to discard/exhaust unwanted cards");
            Add(-entity.SetupRisk * (1 + context.Ascension / 20d), $"local setup-risk heuristic at ascension {context.Ascension}; no sourced ascension win-rate claim");
            foreach (var claim in pack.Claims.Where(c => c.Subject == entity.Id).OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                var applies = claim.Tag == "research-prior" || context.Needs.Contains(claim.Tag) || context.Archetypes.Contains(claim.Tag) || owned.Any(c => c.Entity.Id == claim.Other) || context.Relics.Contains(claim.Other);
                if (applies) Add(claim.Weight * claim.Source.Confidence, $"sourced global claim {claim.Id}: {claim.Reason}; {claim.Source.EvidenceType}; {claim.Source.SourceUrl}" + (claim.Source.EvidenceType == "correlation" ? "; association only, sample size unavailable" : ""));
            }
            // Freshness uses observation timestamp, not wall clock: replay remains reproducible.
            var age = Math.Max(0, (observation.At - pack.Catalog!.RetrievedAt).TotalDays);
            if (age > 30) { score *= .9; reasons.Add($"Strategy older than 30 days at observation; score discounted 10%. Cached {pack.Catalog.RetrievedAt:yyyy-MM-dd}."); }
            reasons.Add($"Mechanic source {card.Source.EvidenceLocator}; catalog {pack.Catalog.Version}; strategy {pack.PackVersion}. Complex timing is uncertain.");
            result.Add(new(choice.Slot, entity.Id, Math.Round(score, 4), reasons.ToArray()));
        }
        return result.OrderByDescending(r => r.Score).ThenBy(r => r.EntityId, StringComparer.Ordinal).ThenBy(r => r.Slot).ToArray();
    }
}

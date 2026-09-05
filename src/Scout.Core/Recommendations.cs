namespace Scout.Core;

public sealed class RecommendationEngine(StrategyPack pack)
{
    public Recommendation[] Rank(Observation observation, RunContext context)
    {
        PackValidation.Validate(pack);
        if (context.GameVersion != pack.GameVersion || observation.Screen == Screen.Unknown || observation.Confidence < .9) return [];
        var results = new List<Recommendation>();
        foreach (var choice in observation.Choices.Where(c => c.EntityId != null && c.Confidence >= .9))
        {
            var entity = pack.Entities.SingleOrDefault(e => e.Id == choice.EntityId);
            if (entity == null) continue;
            if (observation.Screen == Screen.Merchant && (choice.Price == null || choice.PriceConfidence < .9 || choice.Price < 0 || choice.Price > context.Gold)) continue;
            var score = entity.Baseline;
            var reasons = new List<string> { FormattableString.Invariant($"Baseline {entity.Baseline:0.##} (heuristic, not win probability)") };
            void Add(double value, string reason) { score += value; reasons.Add(FormattableString.Invariant($"{value:+0.##;-0.##;0}: {reason}")); }
            var copies = context.Deck.Count(id => id == entity.Id);
            if (entity.Kind == EntityKind.Card)
            {
                Add(-copies * 1.5, "redundancy from known copies");
                Add(-context.Deck.Length * .04, "deck dilution");
                Add(choice.Upgraded ? 1 : 0, "recognized upgrade");
            }
            Add(-entity.SetupRisk * (1 + context.Ascension / 20.0), "setup risk scaled by ascension");
            Add(entity.Tags.Intersect(context.Needs).Count() * (1 + Math.Clamp(context.Act, 1, 3) * .2), "declared deck needs and act");
            foreach (var claim in pack.Claims.OrderBy(c => c.Id, StringComparer.Ordinal).Where(c => c.Subject == entity.Id))
            {
                var applies = claim.Kind switch
                {
                    ClaimKind.CardSynergy or ClaimKind.AntiSynergy => context.Deck.Contains(claim.Other) || context.Relics.Contains(claim.Other),
                    ClaimKind.RelicSynergy => context.Relics.Contains(claim.Other),
                    ClaimKind.DeckNeed => context.Needs.Contains(claim.Tag),
                    ClaimKind.Archetype => context.Archetypes.Contains(claim.Tag),
                    ClaimKind.MerchantPriority => observation.Screen == Screen.Merchant,
                    _ => false
                };
                if (applies) Add(claim.Weight * claim.Source.Confidence, $"{claim.Reason} [claim {claim.Id}; {claim.Source.EvidenceType}; {claim.Source.SourceUrl}]" + (claim.Source.EvidenceType == "correlation" ? " — association only; no causal win-rate claim" : ""));
            }
            if (observation.Screen == Screen.Merchant)
            {
                Add(-choice.Price!.Value / 50.0, "gold price");
                if (context.Gold - choice.Price < context.ReserveGold) Add(-3, "opportunity cost: spending reserved gold");
            }
            results.Add(new(choice.Slot, entity.Id, Math.Round(score, 4), reasons.ToArray()));
        }
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.EntityId, StringComparer.Ordinal).ThenBy(r => r.Slot).ToArray();
    }
}

public sealed class DecisionTracker
{
    private Observation? previous;
    public Selection[] Observe(Observation current)
    {
        var old = previous;
        previous = current.Screen != Screen.Unknown && current.Confidence >= .9 ? current : null;
        if (current.Screen == Screen.Unknown || current.Confidence < .9) return [];
        var signals = (current.SelectionSignals ?? []).Where(s => !s.Confirmed && s.Confidence >= .9 &&
            current.Choices.Any(c => c.Slot == s.Slot && c.EntityId == s.EntityId && c.Confidence >= .9)).ToArray();
        if (old == null || current.Screen != Screen.Merchant || old.Screen != current.Screen || old.ProfileVersion != current.ProfileVersion) return signals;
        // Disappearance is ambiguous: purchase, hover animation, or scrolling. Never a confirmed decision.
        return signals.Concat(old.Choices.Where(c => c.EntityId != null && c.Confidence >= .9 && current.Choices.Any(n => n.Slot == c.Slot && n.EntityId == null))
            .Select(c => new Selection(c.Slot, c.EntityId!, .5, "Merchant slot disappeared between stable observations; unconfirmed purchase hypothesis"))).ToArray();
    }

    public void Reset() => previous = null;
}

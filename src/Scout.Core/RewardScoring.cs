using System.Globalization;

namespace Scout.Core;

public sealed record DeckProfile(int Size, double HandSize, double EnergySupply, double EnergyDemand, double Feasibility, IReadOnlyDictionary<string, double> Jobs,
    double VulnerableSources, double ExhaustSources, int HpLossSources, int StrikeNamed, double AttackFraction, double BlockDensity);

/// <summary>Job-ledger reward scoring: each offer is worth the change in deck utility it causes; Skip is ranked alongside.</summary>
public static class RewardScoring
{
    private sealed record Item(string Id, string Name, string Type, bool Upgraded, CardRole? Role, double Copy = 1)
    {
        public RoleEffect Effect => Upgraded ? Role!.Upgraded : Role!.Base;
        public RoleEffect? Turn => Role!.Type == "Power" ? Upgraded ? Role.UpgradedPerTurn : Role.PerTurn : null;
        public double Cost => (Upgraded ? Role!.UpgradedCost : Role!.Cost) ?? 0;
        public string Label => Name + (Upgraded ? "+" : "");
        public bool HasCondition => Effect.Conditional != null || Turn?.Conditional != null;
    }
    private sealed record Ledger(DeckProfile Profile, double[] Jobs, IReadOnlyList<Item> Items, Func<Item, string, double> Reliability);
    /// <summary>Fixed per screen and variant: stage, reliability shift and job targets.</summary>
    /// <summary>Weights, SetupScale and HpWeight carry the HP rules (N1); Relics are the context's known relic roles.</summary>
    private sealed record Model(CardRoleSet Roles, RunContext Context, Stage Stage, double Shift, double[] Targets, double[] Weights, double SetupScale, double HpWeight, RelicRole[] Relics)
    {
        public double Relic(string effect) => Relics.Where(r => r.Effect == effect).Sum(r => r.Value);
        public bool Has(string effect) => Relics.Any(r => r.Effect == effect);
    }
    private sealed record Offer(Choice Choice, Entity Entity, double Score, Ledger After, bool Unmodeled, List<string> Reasons);
    private sealed record ScreenScore(Model Model, Ledger Deck, Offer[] Offers, double Theta);

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Percent(double value) => (value * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    private static string StageName(Stage stage) => stage switch { Stage.Act1Early => "early Act 1", Stage.Act1 => "Act 1", Stage.Act2 => "Act 2", _ => "Act 3" };

    public static Stage StageOf(RunContext context, int deckSize) => context.Act switch
    {
        >= 3 => Stage.Act3,
        2 => Stage.Act2,
        _ => context.Floor is { } floor ? floor <= ScoringWeights.EarlyFloorMax ? Stage.Act1Early : Stage.Act1 : deckSize <= ScoringWeights.EarlyDeckSizeMax ? Stage.Act1Early : Stage.Act1,
    };

    public static DeckProfile Profile(CardRoleSet roles, IReadOnlyList<(Entity Entity, bool Upgraded)> cards, Stage stage, RunContext context)
    {
        var items = Expand(roles, cards);
        return Compute(items, BuildModel(roles, context, stage, 0)).Profile;
    }

    private static Item ItemOf(CardRoleSet roles, Entity entity, bool upgraded) =>
        new(entity.Id, entity.Name, entity.Card?.Type ?? "", upgraded, CardRoles.Find(roles, entity.Id));

    private static List<Item> Expand(CardRoleSet roles, IEnumerable<(Entity Entity, bool Upgraded)> cards) => Copies(cards.Select(c => ItemOf(roles, c.Entity, c.Upgraded)));

    // Copy behavior (step 6): the first copy (upgraded copies first) counts fully; later copies follow the role's copy behavior.
    private static List<Item> Copies(IEnumerable<Item> items) => items.GroupBy(i => i.Id, StringComparer.Ordinal)
        .SelectMany(g => g.OrderByDescending(i => i.Upgraded).Select((item, k) => item with
        {
            Copy = k == 0 || item.Role == null ? 1 : item.Role.CopyBehavior switch
            {
                "nonStacking" => 0,
                "selfLimiting" => Math.Pow(ScoringWeights.SelfLimitingCopyFactor, k),
                _ => 1,
            },
        })).ToList();

    private static Model BuildModel(CardRoleSet roles, RunContext context, Stage stage, double shift)
    {
        var starter = ScoringWeights.StarterDeck.SelectMany(s => Enumerable.Repeat(CardRoles.Find(roles, s.Id), s.Copies))
            .Select(role => role == null ? null : new Item(role.Id, role.Id, role.Type, false, role)).ToArray();
        if (starter.Any(i => i == null)) throw new InvalidDataException("Card roles are missing a starter card");
        var floor = Enumerable.Repeat(ScoringWeights.MinTarget, ScoringWeights.Jobs.Length).ToArray();
        var weights = ScoringWeights.Jobs.Select(j => ScoringWeights.Need(stage, j)).ToArray();
        double setupScale = 1, hpWeight = ScoringWeights.HpWeight;
        if (context.Hp is { } hp && context.MaxHp is > 0 and var max)
        {
            var ratio = (double)hp / max;
            if (ratio < ScoringWeights.LowHpRatio) { weights[2] *= ScoringWeights.LowHpBlockWeight; setupScale = ScoringWeights.LowHpSetupScale; hpWeight = ScoringWeights.LowHpWeight; }
            else if (ratio >= ScoringWeights.HighHpRatio) hpWeight = ScoringWeights.HighHpWeight;
        }
        var relics = roles.Relics.Where(r => context.Relics.Contains(r.Id, StringComparer.Ordinal)).ToArray();
        hpWeight = relics.Where(r => r.Effect == "hpLossRelief").Aggregate(hpWeight, (w, r) => w * Math.Max(0, 1 - r.Value));
        // Targets come from the starter deck under the same HP rules but without relics, so relics count as gains.
        var targets = Compute(Copies(starter!), new Model(roles, context, stage, 0, floor, weights, setupScale, hpWeight, []), blockGated: false).Jobs.Select(j => Math.Max(ScoringWeights.MinTarget, j)).ToArray();
        return new(roles, context, stage, shift, targets, weights, setupScale, hpWeight, relics);
    }

    private static double JobValue(RoleEffect e, int job, double hpWeight) => job switch
    {
        0 => e.Frontload + ScoringWeights.AoeToFrontload * e.Aoe,
        1 => e.Aoe,
        2 => e.Block - e.HpLoss / ScoringWeights.BlockUnit * hpWeight,
        3 => e.Scaling,
        4 => e.Draw,
        _ => e.Energy,
    };

    private static Ledger Compute(IReadOnlyList<Item> items, Model model, bool blockGated = true)
    {
        var stage = model.Stage; var jobCount = ScoringWeights.Jobs.Length;
        var n = items.Count; var roled = items.Where(i => i.Role != null).ToArray();
        var persistence = ScoringWeights.Persistence(stage); var setup = ScoringWeights.SetupFactor(stage) * model.SetupScale; var hpWeight = model.HpWeight;
        bool IsPower(Item i) => i.Role!.Type == "Power";
        var draw = roled.Where(i => !IsPower(i)).Sum(i => i.Copy * i.Effect.Draw);
        var hand = n == 0 ? ScoringWeights.BaseHandSize : Math.Min(ScoringWeights.MaxHandSize, ScoringWeights.BaseHandSize / Math.Max(ScoringWeights.MinUndrawnFraction, 1 - draw / n));
        var p = n == 0 ? 0 : Math.Min(1, hand / n); // a card cannot be seen more than once per turn
        var demand = roled.Sum(i => p * i.Cost);
        var vulnerable = roled.Sum(i => i.Copy * (i.Effect.VulnerableSource + (i.Turn?.VulnerableSource ?? 0))) + model.Relic("vulnerableSource");
        var exhaust = roled.Sum(i => i.Copy * (i.Effect.ExhaustSource + (i.Turn?.ExhaustSource ?? 0))) + model.Relic("exhaustSource");
        var vulnerablePayoff = model.Relics.Where(r => r.Effect == "vulnerablePayoff").Aggregate(1.0, (m, r) => m * r.Value);
        var bigAttack = model.Has("bigAttack");
        var hpLoss = roled.Count(i => i.Effect.HpLoss > 0 || i.Turn?.HpLoss > 0);
        var strikes = items.Count(i => i.Name.Contains("Strike", StringComparison.OrdinalIgnoreCase));
        var attackFraction = n == 0 ? 0 : (double)items.Count(i => i.Type == "Attack") / n;
        var damageMultiplier = 1 + ScoringWeights.VulnerableDamageBonus * Math.Min(1, vulnerable / ScoringWeights.VulnerableSourcesForFull);
        // The attacks condition first uses unconditional supply; once conditional energy is known it uses the full feasibility.
        var feasibility = demand <= 0 ? 1 : Math.Min(1, (ScoringWeights.BaseEnergy + roled.Sum(i => p * i.Copy * (i.Effect.Energy + persistence * (i.Turn?.Energy ?? 0)))) / demand);
        var blockReliability = 0.0;
        double Reliability(Item item, string kind)
        {
            var r = kind switch
            {
                "vulnerable" => Math.Min(1, vulnerable / ScoringWeights.VulnerableSourcesForFull),
                "exhaust" => Math.Min(1, exhaust / ScoringWeights.ExhaustSourcesForFull),
                "hpLoss" => Math.Min(1, hpLoss / ScoringWeights.HpLossSourcesForFull),
                "strikeNamed" => Math.Min(1, strikes / ScoringWeights.StrikeNamedForFull),
                "attacks" => Math.Min(1, ScoringWeights.BaseHandSize * attackFraction * feasibility / ScoringWeights.AttacksPerTurnForFull),
                "block" => blockReliability,
                _ => 0,
            };
            r = Math.Clamp(r + model.Shift, 0, 1);
            return RareMissing(item) ? r * ScoringWeights.RareDependencyFactor : r;
        }
        bool RareMissing(Item item) => item.Role!.RareDependency is { Length: > 0 } rare && !items.Any(i => rare.Contains(i.Id, StringComparer.Ordinal));
        // A card whose supporting rares are all missing and that has no conditional is halved as a whole (F4).
        double Whole(Item item) => RareMissing(item) && !item.HasCondition ? ScoringWeights.RareDependencyFactor : 1;
        // Job value of one effect; block-gated conditionals are evaluated only in the block pass.
        double Value(Item item, RoleEffect e, int job, bool blockKind) =>
            (blockKind ? 0 : JobValue(e, job, hpWeight) * (job <= 1 && item.Type == "Attack" ? damageMultiplier : 1) * (job == 0 && bigAttack && e.Frontload >= ScoringWeights.BigAttackFrontload ? ScoringWeights.BigAttackMultiplier : 1)) +
            (e.Conditional is { } c ? (c.Kind == "block") == blockKind ? Reliability(item, c.Kind) * (c.Kind == "vulnerable" ? vulnerablePayoff : 1) * Value(item, c.Effect, job, false) : 0 : 0);
        // Supply includes reliability-weighted conditional energy (F2); block-gated energy would need the block job and is left out.
        var supply = ScoringWeights.BaseEnergy + roled.Sum(i => Whole(i) * p * i.Copy * (Value(i, i.Effect, 5, false) + persistence * (i.Turn is { } t ? Value(i, t, 5, false) : 0)));
        feasibility = demand <= 0 ? 1 : Math.Min(1, supply / demand);
        var jobs = new double[jobCount];
        void Pass(bool blockKind)
        {
            foreach (var item in roled)
            {
                var turn = item.Turn; var whole = Whole(item);
                for (var j = 0; j < jobCount; j++)
                {
                    var value = Value(item, item.Effect, j, blockKind);
                    // The energy job is surplus energy: a card's own cost is subtracted from what it gives back.
                    if (j == 5 && !blockKind) value = Math.Max(0, value - item.Cost);
                    jobs[j] += whole * feasibility * p * item.Copy * (j == 3 ? setup : 1) * value;
                    if (turn != null) jobs[j] += whole * p * persistence * item.Copy * (item.Role!.SetupTurns >= 1 || j == 3 ? setup : 1) * (j == 3 ? ScoringWeights.PerTurnScalingPersistenceFactor : 1) * Value(item, turn, j, blockKind);
                }
            }
        }
        Pass(false);
        // Relics, applied generically by effect kind.
        foreach (var item in roled.Where(i => !IsPower(i)))
        {
            if (item.Name.Contains("Strike", StringComparison.OrdinalIgnoreCase)) jobs[0] += feasibility * p * item.Copy * model.Relic("strikeBonus");
            jobs[1] += feasibility * p * item.Copy * item.Effect.ExhaustSource * model.Relic("exhaustPayoff");
        }
        jobs[2] += model.Relic("blockPerTurn");
        jobs[3] += model.Relic("scaling") + (model.Has("strengthDouble") && roled.Length > 0 ? setup * roled.Max(i => i.Effect.Scaling) : 0);
        // Block-gated conditions read the block job, so they are added in a second pass.
        if (blockGated) { blockReliability = Math.Min(1, Math.Max(0, jobs[2]) / model.Targets[2]); Pass(true); }
        for (var j = 0; j < jobCount; j++) jobs[j] = Math.Max(0, jobs[j]);
        var blockDensity = n == 0 ? 0 : (double)roled.Count(i => i.Effect.Block > 0 || i.Effect.Conditional?.Effect.Block > 0 || i.Turn?.Block > 0 || i.Turn?.Conditional?.Effect.Block > 0) / n;
        var profile = new DeckProfile(n, hand, supply, demand, feasibility,
            ScoringWeights.Jobs.Select((name, j) => (name, j)).ToDictionary(x => x.name, x => jobs[x.j], StringComparer.Ordinal),
            vulnerable, exhaust, hpLoss, strikes, attackFraction, blockDensity);
        return new(profile, jobs, items, Reliability);
    }

    private static double Utility(double[] jobs, Model model) =>
        jobs.Select((value, j) => model.Weights[j] * Math.Log(1 + value / model.Targets[j])).Sum();

    private static bool IsUnmodeled(Entity entity, CardRole? role) => role == null || role.Modeled == "none" || entity.Card?.Color == "colorless";

    private static ScreenScore Evaluate(StrategyPack pack, CardRoleSet roles, Observation observation, RunContext context, (Entity Entity, bool Upgraded)[] owned, Stage stage, double shift)
    {
        var deckItems = Expand(roles, owned); var model = BuildModel(roles, context, stage, shift);
        var deck = Compute(deckItems, model); var before = Utility(deck.Jobs, model);
        var offers = new List<Offer>();
        foreach (var choice in observation.Choices.OrderBy(c => c.Slot))
        {
            var entity = pack.Entities.Single(e => e.Id == choice.EntityId); var role = CardRoles.Find(roles, entity.Id);
            double Score(bool upgraded, out Ledger ledger)
            {
                ledger = Compute(Expand(roles, owned.Append((entity, upgraded))), model);
                return ScoringWeights.ScoreScale * (Utility(ledger.Jobs, model) - before);
            }
            var score = Score(choice.Upgraded, out var after);
            var reasons = Explain(entity, choice.Upgraded, deck, after, model);
            if (choice.Upgraded) reasons.Insert(reasons.FindIndex(r => r.StartsWith("Tradeoff", StringComparison.Ordinal)) + 1, $"This offered copy is upgraded: worth {F(score - Score(false, out _))} more than the base card");
            else if (stage == Stage.Act3 && role?.UpgradeClass == "large")
            {
                var lost = (1 - ScoringWeights.LateLargeUpgradeRetention) * Math.Max(0, Score(true, out _) - score);
                score -= lost;
                reasons.Add($"Act 3: fewer upgrades remain, so this base copy loses {F(lost)} of its upgrade value");
            }
            foreach (var claim in pack.Claims.Where(c => c.Subject == entity.Id && c.Tag == "research-prior").OrderBy(c => c.Id, StringComparer.Ordinal))
            {
                score += claim.Weight * claim.Source.Confidence;
                reasons.Add(FormattableString.Invariant($"{claim.Weight * claim.Source.Confidence:+0.##;-0.##;0}: sourced global claim {claim.Id}: {claim.Reason}; {claim.Source.EvidenceType}; {claim.Source.SourceUrl}"));
            }
            offers.Add(new(choice, entity, score, after, IsUnmodeled(entity, role), reasons));
        }
        // R13: an unmodeled offer takes the median modeled score on this screen (0 if none) instead of its own.
        var modeled = offers.Where(o => !o.Unmodeled).Select(o => o.Score).Order().ToArray();
        var median = modeled.Length == 0 ? 0 : modeled.Length % 2 == 1 ? modeled[modeled.Length / 2] : (modeled[modeled.Length / 2 - 1] + modeled[modeled.Length / 2]) / 2;
        var final = offers.Select(o => o.Unmodeled ? o with { Score = median } : o).ToArray();
        var boss = observation.Choices.All(c => pack.Entities.Single(e => e.Id == c.EntityId).Card?.Rarity == "Rare");
        return new(model, deck, final, ScoringWeights.Theta(stage) + (boss ? ScoringWeights.BossThetaShift : 0));
    }

    public static Recommendation[] Rank(StrategyPack pack, CardRoleSet roles, Observation observation, RunContext context)
    {
        var owned = Deck.Entries(context).SelectMany(e => Enumerable.Repeat((Entity: pack.Entities.Single(x => x.Id == e.EntityId), e.Upgraded), e.Copies)).ToArray();
        var stage = StageOf(context, owned.Length);
        var main = Evaluate(pack, roles, observation, context, owned, stage, 0);
        var shift = ScoringWeights.CloseCallReliabilityShift;
        var variants = new[] { (stage, -shift), (stage, shift) }.Concat(new[] { stage - 1, stage + 1 }.Where(s => Enum.IsDefined(s)).Select(s => (s, 0.0)))
            .Select(v => Evaluate(pack, roles, observation, context, owned, v.Item1, v.Item2)).ToArray();
        // Freshness uses observation timestamp, not wall clock: replay remains reproducible.
        var stale = (observation.At - pack.Catalog!.RetrievedAt).TotalDays > 30;
        double Final(double score) => Math.Round(stale ? score * .9 : score, 4);
        // Score of an option within one variant; Skip's θ follows that variant's stage.
        double VariantScore(ScreenScore v, int slot) => slot < 0 ? Math.Round(v.Theta, 4) : Final(v.Offers.Single(o => o.Choice.Slot == slot).Score);
        var unmodeled = main.Offers.Where(o => o.Unmodeled).Select(o => o.Choice.Slot).ToHashSet();
        var recs = main.Offers.Select(o =>
        {
            var reasons = o.Reasons;
            if (o.Unmodeled) reasons.Insert(0, $"Scout can't fully evaluate {o.Entity.Name}; judge it yourself (scored at the median of the other offers, ±{F(ScoringWeights.UnmodeledBand)})");
            if (stale) reasons.Add("Strategy older than 30 days at observation; score discounted 10%. Cached " + pack.Catalog.RetrievedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".");
            reasons.Add($"Mechanic source {o.Entity.Card!.Source.EvidenceLocator}; catalog {pack.Catalog.Version}; strategy {pack.PackVersion}; card roles {roles.GameVersion}. Complex timing is uncertain.");
            return new Recommendation(o.Choice.Slot, o.Entity.Id, Final(o.Score), reasons.ToArray());
        }).Append(new(RewardChoices.SkipSlot, RewardChoices.Skip, Math.Round(main.Theta, 4), [])).ToList();
        Recommendation[] Sort() => recs.OrderByDescending(r => r.Score).ThenBy(r => unmodeled.Contains(r.Slot) ? 1 : 0).ThenBy(r => r.EntityId, StringComparer.Ordinal).ThenBy(r => r.Slot).ToArray();
        var ranked = Sort();
        var close = new HashSet<int>();
        var (top, second) = (ranked[0], ranked[1]);
        // R14: a close call when the base margin is small, or when any single perturbed re-ranking no longer puts the top option above the runner-up.
        bool StillAhead(ScreenScore v)
        {
            var (a, b) = (VariantScore(v, top.Slot), VariantScore(v, second.Slot));
            if (a != b) return a > b;
            var (ua, ub) = (unmodeled.Contains(top.Slot) ? 1 : 0, unmodeled.Contains(second.Slot) ? 1 : 0);
            return ua != ub ? ua < ub : string.CompareOrdinal(top.EntityId, second.EntityId) < 0;
        }
        if (top.Score - second.Score < ScoringWeights.CloseCallMargin || !variants.All(StillAhead)) { close.Add(top.Slot); close.Add(second.Slot); }
        foreach (var slot in unmodeled)
        {
            var best = ranked.First(r => r.Slot != slot);
            if (ranked.Single(r => r.Slot == slot).Score + ScoringWeights.UnmodeledBand >= best.Score) { close.Add(slot); close.Add(best.Slot); }
        }
        var stageText = StageName(stage);
        string[] skipReasons =
        [
            ranked[0].EntityId == RewardChoices.Skip ? $"No offer improves the deck enough for {stageText}; skipping keeps {KeyCard(main)} drawn more often"
                : $"Skip is worth {F(main.Theta)} in {stageText}; at least one offer improves the deck by more",
            FormattableString.Invariant($"Skip threshold θ = {main.Theta:0.##} ({stageText}{(main.Theta < ScoringWeights.Theta(stage) ? ", boss reward" : "")}); offers score 10 × utility change"),
        ];
        return ranked.Select(r =>
        {
            var reasons = r.EntityId == RewardChoices.Skip ? skipReasons.ToList() : r.Reasons.ToList();
            if (!close.Contains(r.Slot)) return r with { Reasons = reasons.ToArray() };
            var other = close.Contains(top.Slot) && r.Slot != top.Slot ? top : ranked.First(x => x.Slot != r.Slot && close.Contains(x.Slot));
            reasons.Insert(Math.Min(1, reasons.Count), $"Close call vs {DeckRanking.DisplayName(pack, other)}: {Difference(main, r.Slot, other.Slot, pack)}");
            return r with { Reasons = reasons.ToArray(), CloseCall = true };
        }).ToArray();
    }

    private static double[] Gains(ScreenScore screen, int slot) => slot < 0 ? new double[ScoringWeights.Jobs.Length] :
        ScoringWeights.Jobs.Select((job, j) => ScoringWeights.ScoreScale * screen.Model.Weights[j] *
            (Math.Log(1 + screen.Offers.Single(o => o.Choice.Slot == slot).After.Jobs[j] / screen.Model.Targets[j]) - Math.Log(1 + screen.Deck.Jobs[j] / screen.Model.Targets[j]))).ToArray();

    private static string Difference(ScreenScore screen, int a, int b, StrategyPack pack)
    {
        var (ga, gb) = (Gains(screen, a), Gains(screen, b));
        var j = Enumerable.Range(0, ga.Length).OrderByDescending(j => Math.Abs(ga[j] - gb[j])).ThenBy(j => j).First();
        string Name(int slot) => slot < 0 ? "Skip" : screen.Offers.Single(o => o.Choice.Slot == slot).Entity.Name;
        return FormattableString.Invariant($"{ScoringWeights.Jobs[j]} {ga[j]:+0.##;-0.##;0} vs {gb[j]:+0.##;-0.##;0} for {Name(b)}");
    }

    private static string KeyCard(ScreenScore screen)
    {
        var deck = screen.Deck; var full = Utility(deck.Jobs, screen.Model);
        var best = deck.Items.Where(i => i.Role != null).Select(i => i.Label).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(label =>
            {
                var index = deck.Items.ToList().FindIndex(i => i.Label == label);
                var rest = Copies(deck.Items.Where((_, k) => k != index).Select(i => i with { Copy = 1 }));
                return (Label: label, Drop: full - Utility(Compute(rest, screen.Model).Jobs, screen.Model));
            }).OrderByDescending(x => x.Drop).FirstOrDefault();
        return best.Label ?? "your current cards";
    }

    private static List<string> Explain(Entity entity, bool upgraded, Ledger deck, Ledger after, Model model)
    {
        var reasons = new List<string>();
        var jobs = ScoringWeights.Jobs; var targets = model.Targets; var stage = model.Stage;
        var need = Enumerable.Range(0, jobs.Length).OrderByDescending(j => model.Weights[j] / (1 + deck.Jobs[j] / targets[j])).ThenBy(j => j).First();
        var gains = Enumerable.Range(0, jobs.Length).Select(j => (Job: j, Gain: model.Weights[j] * (Math.Log(1 + after.Jobs[j] / targets[j]) - Math.Log(1 + deck.Jobs[j] / targets[j])))).ToArray();
        var best = gains.OrderByDescending(g => g.Gain).ThenBy(g => g.Job).First();
        reasons.Add($"Needs: {jobs[need]} ({F(deck.Jobs[need])}/{F(targets[need])}); " + (best.Gain > 0 ? $"this card adds {F(after.Jobs[best.Job] - deck.Jobs[best.Job])} {jobs[best.Job]}" : "this card adds nothing the deck lacks; it only dilutes draws"));
        var item = after.Items.First(i => i.Id == entity.Id && i.Upgraded == upgraded);
        if (item.Role is { } role)
        {
            var kinds = new[] { item.Effect, item.Turn }.OfType<RoleEffect>().Where(e => e.Conditional != null).Select(e => e.Conditional!.Kind).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var kind in kinds) reasons.Add(Condition(kind, after, item, model));
            if (role.RareDependency.Length > 0 && !after.Items.Any(i => role.RareDependency.Contains(i.Id, StringComparer.Ordinal)))
                reasons.Add(item.HasCondition ? "No supporting rare card in the deck; its conditional value is halved" : "No supporting rare card in the deck; its value is halved");
            var copy = after.Items.Where(i => i.Id == entity.Id).Min(i => i.Copy);
            if (copy < 1) reasons.Add(role.CopyBehavior == "nonStacking" ? $"Another copy of {entity.Name} adds nothing: its effect does not stack" : $"Extra copies of {entity.Name} are worth less (×{F(copy)})");
            if (role.CopyBehavior == "uncertain" && after.Items.Count(i => i.Id == entity.Id) > 1) reasons.Add($"Whether extra copies of {entity.Name} stack is unverified");
            if (role.Modeled == "partial") reasons.Add($"Partly modeled: {role.Notes}");
        }
        else reasons.Add($"Scout has no role data for {entity.Name}; it is counted as filler that only occupies a draw");
        reasons.Add($"Tradeoff: deck {deck.Profile.Size} → {after.Profile.Size} cards; energy demand {after.Profile.EnergyDemand.ToString("0.0", CultureInfo.InvariantCulture)} vs {after.Profile.EnergySupply.ToString("0.0", CultureInfo.InvariantCulture)} supply");
        var filler = deck.Items.Where(i => i.Role == null).Select(i => i.Label).Distinct(StringComparer.Ordinal).ToArray();
        if (model.Context.Hp is { } hp && model.Context.MaxHp is > 0 and var max)
            reasons.Add($"HP {hp}/{max}: block need ×{F(model.Weights[2] / ScoringWeights.Need(stage, "block"))}, setup ×{F(model.SetupScale)}, self-damage weight {F(model.HpWeight)}");
        if (model.Relics.Length > 0) reasons.Add("Relics applied: " + string.Join(", ", model.Relics.Select(r => $"{r.Name} ({r.Effect} {F(r.Value)})")));
        var unknown = model.Context.Relics.Where(id => !model.Roles.Relics.Any(r => r.Id == id)).Distinct(StringComparer.Ordinal).ToArray();
        if (unknown.Length > 0) reasons.Add($"Relic(s) not modeled, ignored: {string.Join(", ", unknown)}");
        if (filler.Length > 0) reasons.Add($"{filler.Length} deck card type(s) have no role data ({string.Join(", ", filler)}); counted as filler that occupies a draw");
        reasons.Add("Job changes: " + string.Join(", ", gains.Select(g => $"{jobs[g.Job]} {F(deck.Jobs[g.Job])}→{F(after.Jobs[g.Job])}")) + $"; stage {StageName(stage)}");
        return reasons;
    }

    private static string Condition(string kind, Ledger after, Item item, Model model)
    {
        var p = after.Profile; var met = Percent(after.Reliability(item, kind)); var targets = model.Targets;
        string Names(Func<Item, bool> where, string relicEffect = "") => string.Join(", ", after.Items.Where(i => i.Role != null && where(i)).GroupBy(i => i.Label, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key)
            .Concat(model.Relics.Where(r => r.Effect == relicEffect).Select(r => r.Name + " (relic)")));
        return kind switch
        {
            "vulnerable" => $"Vulnerable sources in deck: {F(p.VulnerableSources)} ({Names(i => i.Effect.VulnerableSource > 0, "vulnerableSource")}) → condition met ~{met}",
            "exhaust" => $"Exhaust sources in deck: {F(p.ExhaustSources)} ({Names(i => i.Effect.ExhaustSource > 0, "exhaustSource")}) → condition met ~{met}",
            "hpLoss" => $"HP-loss sources in deck: {p.HpLossSources} ({Names(i => i.Effect.HpLoss > 0 || i.Turn?.HpLoss > 0)}) → condition met ~{met}",
            "strikeNamed" => $"Strike-named cards in deck: {p.StrikeNamed} → condition met ~{met}",
            "attacks" => $"Attacks are {Percent(p.AttackFraction)} of the deck; energy feasibility {Percent(p.Feasibility)} → condition met ~{met}",
            _ => $"Block capability {F(p.Jobs["block"])}/{F(targets[2])} → condition met ~{met}",
        };
    }
}

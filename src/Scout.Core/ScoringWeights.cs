namespace Scout.Core;

public enum Stage { Act1Early, Act1, Act2, Act3 }

/// <summary>
/// Every number here is an initial tuning hypothesis taken from docs/ironclad-reward-research.md §4–5, not a measured fact.
/// Change values only with a scenario-evaluation report; never to fix a single scenario.
/// </summary>
public static class ScoringWeights
{
    public static readonly string[] Jobs = ["frontload", "aoe", "block", "scaling", "draw", "energy"];
    public const double BaseEnergy = 3;
    public const double BaseHandSize = 5;
    public const double MaxHandSize = 10;
    public const double MinUndrawnFraction = .5;
    public const double XCost = 2;
    public const double HpWeight = .5;
    public const double BlockUnit = 5;
    // Job targets are the starter deck's capability, floored so a job the starter lacks (AoE, scaling, draw) can't dominate.
    public const double MinTarget = 1.0;
    // Cross-job mapping: AoE damage also hits the primary target, so it counts toward frontload at this weight (and toward AoE in full).
    public const double AoeToFrontload = 1.0;
    // Global multiplier on recurring per-turn Scaling from powers (Demon Form-style), relative to immediate Strength.
    public const double PerTurnScalingPersistenceFactor = .6;
    public const double ScoreScale = 10;
    public const double SelfLimitingCopyFactor = .7;
    public const double RareDependencyFactor = .5;
    public const double BossThetaShift = -.5;
    // Reliability saturation points (R4): sources needed for a condition to be met ~100%.
    public const double VulnerableSourcesForFull = 3;
    public const double ExhaustSourcesForFull = 3.5;
    public const double HpLossSourcesForFull = 3;
    public const double StrikeNamedForFull = 6;
    public const double AttacksPerTurnForFull = 3;
    // Act 1 is "early" while the floor is at most this, or (floor unknown) the deck has at most this many cards.
    public const int EarlyFloorMax = 4;
    public const int EarlyDeckSizeMax = 13;

    // Reference deck for job targets T_j: the 0.107.1 Ironclad starter (reference data, not a scoring special case).
    // Vulnerable as a damage multiplier (T5a): attack damage × (1 + bonus × min(1, S / VulnerableSourcesForFull)).
    public const double VulnerableDamageBonus = .5;
    // Close calls (R14): reliability shift used by the perturbed re-rankings, and the minimum base-case margin (tuning hypothesis).
    public const double CloseCallReliabilityShift = .2;
    public const double CloseCallMargin = .3;
    // Unmodeled offers get the median modeled score with this uncertainty band (R13).
    public const double UnmodeledBand = 1.5;
    // Act 3 base copies of large-upgrade cards keep only this share of their upgrade value (R11; fewer upgrades remain).
    public const double LateLargeUpgradeRetention = .8;

    // HP rules (N1): below LowHpRatio block need ×1.5, setup ×0.7 and self-damage weighs 1.5; at or above HighHpRatio self-damage weighs 0.3.
    public const double LowHpRatio = .4;
    public const double HighHpRatio = .75;
    public const double LowHpBlockWeight = 1.5;
    public const double LowHpSetupScale = .7;
    public const double LowHpWeight = 1.5;
    public const double HighHpWeight = .3;
    // Pen Nib-style relics (bigAttack): single-play frontload at or above this threshold is worth ×BigAttackMultiplier.
    public const double BigAttackFrontload = 3;
    public const double BigAttackMultiplier = 1.1;

    public static readonly (string Id, int Copies)[] StarterDeck = [("sts2.strike_ironclad", 5), ("sts2.defend_ironclad", 4), ("sts2.bash", 1)];

    public static double Persistence(Stage stage) => stage switch { Stage.Act1Early => 1.5, Stage.Act1 => 2.0, Stage.Act2 => 2.5, _ => 3.0 };
    public static double SetupFactor(Stage stage) => stage switch { Stage.Act1Early => .5, Stage.Act1 => .7, Stage.Act2 => .9, _ => 1.0 };
    /// <summary>Skip value θ (R1): an offer must beat this to be recommended.</summary>
    public static double Theta(Stage stage) => stage switch { Stage.Act1Early => -.5, Stage.Act1 => 0, Stage.Act2 => .5, _ => 1.0 };
    /// <summary>Need weights by stage (R2 table), in <see cref="Jobs"/> order.</summary>
    public static double Need(Stage stage, string job) => (stage switch
    {
        Stage.Act1Early => new[] { 1.0, .5, .5, .2, .4, .3 },
        Stage.Act1 => new[] { .8, .5, .7, .5, .5, .4 },
        Stage.Act2 => new[] { .5, .5, .9, .8, .6, .5 },
        _ => new[] { .3, .4, .9, 1.0, .7, .5 },
    })[Array.IndexOf(Jobs, job)];
}

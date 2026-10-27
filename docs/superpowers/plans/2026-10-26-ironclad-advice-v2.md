# Ironclad Reward Advice v2 Implementation Plan

> **For agentic workers:** Execution is coordinated by the main session. A **generator** subagent implements each task; an independent **evaluator** subagent reviews each task against this plan before the next one starts. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Replace Scout's tag-counting card-reward heuristic with an interpretable, deck-aware job ledger. Each card is scored by the marginal improvement it brings to the deck's weakest jobs. Skip is a ranked option, conditional synergies are weighted by how reliably the deck enables them, and the advice says "take this because your deck needs X; tradeoff Y".

**Architecture:** Hand-reviewed per-patch **card role annotations** (an embedded JSON resource in `Scout.Core`) describe what each Ironclad reward card contributes to each job (frontload damage, AoE, block, scaling, draw, energy) and which conditions it depends on. A new `RewardScoring` computes deck capability per job from those roles, applies a concave (logarithmic) utility, and scores each offer as `V(deck + card) − V(deck)` against Skip = 0. `DeckRanking.Rank` delegates to it for Ironclad. Other characters keep the old heuristic, unchanged.

**Tech Stack:** C# / .NET 10 (`global.json` 10.0.100), xUnit 2.9, System.Text.Json (strict: `UnmappedMemberHandling.Disallow`, `RespectNullableAnnotations`), WPF for `Scout.Windows` (Windows-only).

**Spec:** `docs/ironclad-reward-research.md`. Sections 4–7 give the rules (R1–R14, N1–N7), the scoring approach and the evaluation scenarios; §10 adds the video and Reddit evidence. Executors must read §4, §5, §6 and §10 before starting.

## Global Constraints

- Game version is exactly `0.107.1`. Role data must declare `"gameVersion": "0.107.1"`, and loading must fail closed (no Ironclad v2 ranking) on mismatch.
- Use only stable 0.107.1 card text. Never use beta (v0.109–v0.111) numbers. Card text source for authoring: `/private/tmp/claude-501/-Users-jc8080-Documents-Projects-sts2-scout/4306be83-9513-44bd-9a09-a940ababbad6/scratchpad/desc.txt` (one line per card: color|rarity|type|cost|name: text || UP: upgraded text). The raw API copy is `cards.json` in the same folder. Neither file may be copied into the repository. Role `notes` must be original short summaries, not copied card text (see `docs/catalog-research.md` licensing).
- Determinism: identical inputs must give identical JSON output, in any culture. Use `CultureInfo.InvariantCulture` for every number formatted in reasons. Sort ties by entity ID (ordinal), then slot.
- No runtime networking, no new NuGet packages, no game-file access.
- `TreatWarningsAsErrors` is on. `dotnet format Sts2Scout.sln --verify-no-changes` must pass (CI runs it).
- Every numeric weight lives in one `ScoringWeights` static class, with a comment saying it is an initial tuning hypothesis, not a measured fact.
- **No card-ID special-casing in scoring code.** Card-specific knowledge lives only in the role data file. Scoring code reads roles generically. The evaluator rejects any `if (id == "sts2.x")` in `RewardScoring.cs`.
- Merchant ranking, the non-Ironclad card-reward path, OCR thresholds and storage schema are unchanged unless a task says otherwise.
- **Do not commit.** Leave changes in the working tree on branch `feature/ironclad-advice-v2`. The coordinator handles commits with the user.
- Toolchain on this Mac: run `export DOTNET_ROOT=/private/tmp/claude-501/-Users-jc8080-Documents-Projects-sts2-scout/4306be83-9513-44bd-9a09-a940ababbad6/scratchpad/dotnet10 PATH=$DOTNET_ROOT:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1` before any `dotnet` command. The baseline is 50/50 tests passing (`dotnet test tests/Scout.Tests/Scout.Tests.csproj`).

## Review Focus

1. **A deck containing cards that have no role annotation** (non-Ironclad, curses, statuses, event/ancient cards such as Break or Corruption): scoring must not crash. Those cards count as neutral filler (they occupy a draw and contribute nothing), and the explanation says so. Test it in Task 4.
2. **An offer that is a colorless card, or an Ironclad card without a role entry:** the offer gets a neutral prior with an "unmodeled" flag and can never beat a modeled card by default (R13). Test it in Task 5.
3. **Degenerate decks** (all powers, all 0-cost cards, deck size 1–3, 40+ cards): no NaN, infinity or division by zero, and deterministic output. Test it in Task 4 with a property-style loop over generated decks.
4. **Skip in the UI and Seeder output:** a `Recommendation` with `EntityId == "skip"` must never be passed to `pack.Entities.Single(...)`. Test it in Task 4 (Core helper) and verify by inspection in `OverlayWindow`.
5. **Old settings files without the new HP/Floor/Relics fields** must still load, and out-of-range HP (0, negative, above max) must be rejected with a clear message. Test it in Task 6.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Scout.Core/Data/card-roles.0.107.1.json` (create, EmbeddedResource) | Per-card role annotations for all 80 single-player Ironclad Common/Uncommon/Rare reward cards, plus a directional-relic list |
| `src/Scout.Core/CardRoles.cs` (create) | Role records, loader from embedded resource, validation against the pack |
| `src/Scout.Core/ScoringWeights.cs` (create) | All tunable constants (hypotheses), stage tables |
| `src/Scout.Core/RewardScoring.cs` (create) | Deck capability, reliability, utility, marginal scoring, Skip, close calls, explanations |
| `src/Scout.Core/DeckRanking.cs` (modify) | Delegate Ironclad rewards to `RewardScoring`; keep the legacy path for other characters |
| `src/Scout.Core/Models.cs` (modify) | `Recommendation.CloseCall`; `RunContext` gains `Hp`, `MaxHp`, `Floor`; `RewardChoices.Skip` constant |
| `src/Scout.Core/Scout.Core.csproj` (modify) | EmbeddedResource for the role JSON |
| `src/Scout.Core/Recommendations.cs` | Unchanged except `DisplayName` helper usage if needed |
| `src/Scout.Windows/OverlayWindow.cs`, `DeckEditor.cs`, `Settings.cs` (modify) | Skip/close-call display; HP, Floor and relic inputs; settings validation |
| `tools/Scout.Seeder/Program.cs` (modify) | `evaluate` command printing a scenario report |
| `tests/Scout.Tests/Fixtures/ironclad-scenarios.dev.json` (create) | 20 dev scenarios from research §6 |
| `tests/Scout.Tests/Fixtures/ironclad-scenarios.heldout.json` (create, **evaluator only**) | Held-out scenarios. The generator must never open this file |
| `tests/Scout.Tests/ScenarioEvaluation.cs` (create) | Scenario loader, scoring harness, metrics |
| `tests/Scout.Tests/ScenarioTests.cs`, `RoleTests.cs`, `RewardScoringTests.cs` (create) | Tests |
| `docs/card-reward-mvp.md`, `docs/architecture.md` (modify) | User-facing behavior and limits |

---

### Task 1: Scenario evaluation harness and dev set

**Files:** Create `tests/Scout.Tests/Fixtures/ironclad-scenarios.dev.json`, `tests/Scout.Tests/ScenarioEvaluation.cs`, `tests/Scout.Tests/ScenarioTests.cs`.

**Interfaces:**
- Produces: `record Scenario(string Id, int Act, int Ascension, int? Floor, int? Hp, int? MaxHp, string[] Relics, DeckEntry[] Deck, Choice[] Offers, string[] Preferred, string[] Acceptable, string[] Tags, string Rationale)`. `Preferred` and `Acceptable` contain entity IDs or `"skip"`.
- Produces: `static class ScenarioEvaluation { Scenario[] Load(string path); ScenarioResult Evaluate(StrategyPack pack, Scenario s); EvaluationReport Run(StrategyPack pack, IEnumerable<Scenario> scenarios); }`
- `record ScenarioResult(string Id, string Picked, bool PreferredHit, bool AcceptableHit, bool CloseCallFlag, string[] TopReasons)`
- `record EvaluationReport(int Total, int PreferredHits, int AcceptableHits, double SkipPrecision, double SkipRecall, ScenarioResult[] Results)`. `Picked` is the top-ranked entity ID, or `"skip"` when the engine ranks Skip first.

- [ ] **Step 1:** Translate research §6 scenarios S01–S20 into `ironclad-scenarios.dev.json`. Deck lists are in `/private/tmp/claude-501/-Users-jc8080-Documents-Projects-sts2-scout/4306be83-9513-44bd-9a09-a940ababbad6/scratchpad/scenarios.json` (card names → map to `sts2.*` IDs via the pack's `Name`, preferring `card.color == "ironclad"`). Take `preferred`, `acceptable` and `tags` from the §6 table. "Close: A / B" means both are listed in `Preferred`. S10 sets `Hp: 30, MaxHp: 80`. S20 sets `Relics: ["dingy_rug"]`. Every offer has `Confidence: 1`.
- [ ] **Step 2:** Write `ScenarioEvaluation`. Build the `RunContext` with `GameVersion "0.107.1"`, `Character "ironclad"`, `Cards` = the deck, `Deck` = the IDs, and the act, ascension and relics. Build the `Observation` with a fixed `At = 2026-09-06T07:00:00Z`, `Screen.CardReward`, confidence 1. Call `new RecommendationEngine(pack).Rank(...)`.
- [ ] **Step 3:** In `ScenarioTests`, add a `[Fact] DevScenariosMeetRatchet` that runs the dev set, writes a per-scenario table through `ITestOutputHelper`, and asserts `AcceptableHits >= ScenarioTests.DevRatchet`. Start with `const int DevRatchet = 8;`, the current-engine baseline measured in research §3. Later tasks raise it. Add a second `[Fact] HeldOutScenariosReport` that runs the held-out file if it exists and **only reports** (no assertion beyond "does not throw").
- [ ] **Step 4:** Add a `dotnet run --project tools/Scout.Seeder -- evaluate <pack> <scenarios.json>` command that prints the same table. Put the shared logic in `tools/Scout.Seeder`, or call through `Scout.Core` if it fits better. The test project already references Seeder.
- [ ] **Step 5:** Run the tests. They are expected to pass with baseline ≥ 8. Record the baseline count in the task report.

### Task 2 (evaluator only, in parallel with Task 1): Held-out scenarios

Create `tests/Scout.Tests/Fixtures/ironclad-scenarios.heldout.json` with **at least 20** new scenarios in the Task 1 schema, written independently from research §2, §4 and §10 (not from the dev set). Coverage: at least 4 skip, 3 good duplicates, 3 bad duplicates, 4 unsupported synergies, 3 supported synergies, 3 low-HP, 3 energy-curve, and at least 3 close calls. Use only 0.107.1 reward-pool cards (check names against `desc.txt`). The generator never reads this file. The coordinator reports held-out results separately.

### Task 3: Card role annotations

**Files:** Create `src/Scout.Core/Data/card-roles.0.107.1.json`, `src/Scout.Core/CardRoles.cs`, `tests/Scout.Tests/RoleTests.cs`. Modify `src/Scout.Core/Scout.Core.csproj` (`<EmbeddedResource Include="Data/card-roles.0.107.1.json" LogicalName="Scout.Core.card-roles.json" />`).

**Interfaces (produces):**

```csharp
public sealed record RoleEffect(
    double Frontload = 0,       // single-target damage in units of 6 damage (a Strike = 1.0)
    double Aoe = 0,             // damage to ALL enemies in units of 6
    double Block = 0,           // block in units of 5 (a Defend = 1.0); Weak and strength-down mitigation also go here
    double Scaling = 0,         // lasting fight-long gain in scaling units (1.0 ≈ +1 Strength or an equivalent, permanent)
    double Draw = 0,            // net extra cards drawn per play (Pommel Strike = 1, Pommel+ = 2, Battle Trance = 3)
    double Energy = 0,          // net energy gained per play (Bloodletting = 2)
    double HpLoss = 0,          // HP lost on your own turn per play
    double VulnerableSource = 0,// Vulnerable uptime weight: Tremble 1.0, Bash 0.8 (+0.2 upgraded), Taunt/Uppercut/Thunderclap 0.6, Dominate 0.5
    double ExhaustSource = 0,   // other cards exhausted per play (+1 if the card self-exhausts): True Grit 1, Burning Pact 1, Second Wind 2, Fiend Fire 4, Stoke 4
    double BlockEvents = 0,     // separate block gains per play or per turn (Juggernaut/Rage enablers)
    RoleCondition? Conditional = null);
public sealed record RoleCondition(string Kind, RoleEffect Effect); // Kind: vulnerable|exhaust|hpLoss|strikeNamed|attacks|block
public sealed record CardRole(
    string Id, string Type, int? Cost, int? UpgradedCost,
    RoleEffect Base, RoleEffect Upgraded,
    RoleEffect? PerTurn, RoleEffect? UpgradedPerTurn, // powers: contribution each turn after played
    bool SelfExhaust, int SetupTurns,                  // 0 = does its job the turn it is played
    string CopyBehavior,                               // stackable|nonStacking|selfLimiting|uncertain
    string UpgradeClass,                               // large|normal|negative
    string[] RareDependency, string Modeled, string Notes);
public sealed record RelicRole(string Id, string Name, string Effect, double Value);
public sealed record CardRoleSet(string GameVersion, CardRole[] Cards, RelicRole[] Relics);
public static class CardRoles
{
    public static CardRoleSet Load();                                  // embedded resource, strict Json.Options
    public static void Validate(CardRoleSet roles, StrategyPack pack); // throws InvalidDataException
    public static CardRole? Find(CardRoleSet roles, string entityId);
}
```

**Authoring rules** (the evaluator checks a sample of 25 cards against card text):

- Units: damage ÷ 6, block ÷ 5. Multi-hit = total damage. X-cost cards assume X = 2.
- Count only what is unconditional in `Base`/`Upgraded`. Anything gated ("if Vulnerable", "if you exhausted", "if you lost HP", "for each Strike", "cost 1 less per Attack") goes in `Conditional` at its **full-condition** value.
- Powers: `Base` is the immediate effect (Inflame: Scaling 2). `PerTurn` is the recurring effect (Crimson Mantle PerTurn: Block 1.6, HpLoss 1; Demon Form PerTurn: Scaling 2; Pyre PerTurn: Energy 1; Dark Embrace: Conditional exhaust → Draw 1 per exhaust, written as PerTurn Draw 1 conditional on exhaust).
- Strength gained permanently → `Scaling` equal to the Strength amount. Temporary strength (Setup Strike) → `Frontload` equivalent, 0.5 per point.
- Weak 1 stack ≈ `Block 0.6`; Uppercut+ (2 Weak) ≈ 1.2. Mangle's strength-down ≈ `Block 2.0`. Colossus's 50% reduction ≈ `Conditional vulnerable Block 3.0`.
- `CopyBehavior`:
  - `nonStacking`: Barricade, Corruption-style booleans.
  - `selfLimiting`: Battle Trance.
  - `stackable`: Unmovable, Feel No Pain, Stone Armor and most others.
  - `uncertain`: Pyre (if stacking is unverified).
- `UpgradeClass`:
  - `large`: Pommel Strike (draw 1→2), Vicious, Whirlwind, Infernal Blade (cost →0), Havoc (cost →0), Armaments (all cards), Barricade/Dark Embrace/Hellraiser/Stampede/Unmovable (cost −1), Stone Armor, Stoke, Uppercut, True Grit.
  - `negative`: Feed.
  - `normal`: everything else.
- `RareDependency`: rare-card IDs whose absence makes this card weak (Conflagration → strength rares). Use sparingly.
- `Modeled`: `full`; or `partial` when a meaningful part of the text cannot be represented (One-Two Punch, Juggling, Stampede, Cascade, Primal Force, Havoc, Infernal Blade, Stoke, Aggression, Hellraiser); or `none`.
- `Relics`: at least Dingy Rug (colorlessRewards), Bag of Marbles (vulnerableSource 0.6), Paper Phrog (vulnerablePayoff 1.17), Self-Forming Clay (hpLossRelief 0.6), Demon Tongue (hpLossRelief 1.0), Strike Dummy (strikeBonus 0.5), Charon's Ashes (exhaustPayoff 0.5 aoe per exhaust), Red Skull (scaling 1.5), Vajra (scaling 1), Ruined Helmet (strengthDouble), Gorget (blockPerTurn 0.8), Pen Nib (bigAttack), Toasty Mittens (exhaustSource 1, scaling 1), Brimstone (scaling 2).

- [ ] **Step 1:** Write the failing `RoleTests`:
  - `RolesLoadAndMatchPatch`: version `0.107.1`; every role ID exists in the pack.
  - `EverySinglePlayerIroncladRewardCardHasARole`: pool = `card.color == "ironclad"`, rarity in {Common, Uncommon, Rare}, `!multiplayerOnly`, expecting exactly 80.
  - `UnitsAreSane`: for every card: finite; `Frontload`, `Aoe` and `Block` within 0–10; `Draw` within 0–6; `Energy` within 0–3; `CopyBehavior` and `UpgradeClass` from the allowed sets.
  - `SpotChecks` against card text:
    - Strike Frontload 1.0, upgraded 1.5;
    - Defend Block 1.0;
    - Bludgeon Frontload ≈ 5.33;
    - Pommel Strike Draw 1, upgraded Draw 2;
    - Barricade CopyBehavior nonStacking;
    - Battle Trance selfLimiting;
    - Dismantle has a Conditional of kind vulnerable;
    - Feel No Pain has a conditional exhaust PerTurn;
    - Bloodletting Energy 2, HpLoss 3;
    - Feed UpgradeClass negative.
  - `ValidationRejectsBadData`: unknown ID, version mismatch, duplicate ID.
- [ ] **Step 2:** Confirm the tests fail (no `CardRoles` type yet).
- [ ] **Step 3:** Implement `CardRoles` (load, validate, find) and author all 80 entries plus the relics.
- [ ] **Step 4:** Run the full test suite. All tests pass.

### Task 4: Job ledger scoring with Skip

**Files:** Create `src/Scout.Core/ScoringWeights.cs`, `src/Scout.Core/RewardScoring.cs`, `tests/Scout.Tests/RewardScoringTests.cs`. Modify `src/Scout.Core/DeckRanking.cs`, `src/Scout.Core/Models.cs`, `src/Scout.Windows/OverlayWindow.cs`, `tools/Scout.Seeder/Program.cs`.

**Interfaces:**
- Consumes: `CardRoles` (Task 3).
- Produces:
  - `Models.cs`: `public sealed record Recommendation(int Slot, string EntityId, double Score, string[] Reasons, bool CloseCall = false);` plus `public static class RewardChoices { public const string Skip = "skip"; public const int SkipSlot = -1; }`. The Skip recommendation uses `Slot = -1` and `EntityId = "skip"`.
  - `public static class RewardScoring { public static Recommendation[] Rank(StrategyPack pack, CardRoleSet roles, Observation observation, RunContext context); public static Stage StageOf(RunContext context, int deckSize); public static DeckProfile Profile(CardRoleSet roles, IReadOnlyList<(Entity Entity, bool Upgraded)> cards, Stage stage, RunContext context); }`
  - `public enum Stage { Act1Early, Act1, Act2, Act3 }`. Act 4 is treated as Act3.
  - `public sealed record DeckProfile(int Size, double HandSize, double EnergySupply, double EnergyDemand, double Feasibility, IReadOnlyDictionary<string,double> Jobs, double VulnerableSources, double ExhaustSources, int HpLossSources, int StrikeNamed, double AttackFraction, double BlockDensity);`
  - `public static string DisplayName(StrategyPack pack, Recommendation r)` in `DeckRanking`, returning `"Skip (take no card)"` for Skip.
  - *(Amended 2026-10-26)* `RunContext` gains `int? Hp = null, int? MaxHp = null, int? Floor = null` at the end of its positional parameters **in this task**, so `StageOf` can use `Floor`. The scenario harness passes them through, and `ScenarioResult.CloseCallFlag` reads `Recommendation.CloseCall`. Task 6 adds HP validation, HP-based scoring and the UI.

**Amendments from the Task 3 review (2026-10-26):** These rulings override steps 3, 4 and 8 of the model below where they conflict.

- **M1 — target floor.** The floor for every target is 1.0, so `T_j = max(starter J_j, 1.0)`.
- **M2 — energy job.** The energy job counts only surplus: Σ p·max(0, Energy − cost).
- **M3 — AoE.** AoE damage also adds to the frontload job at weight 1.0.
- **M4 — per-turn scaling.** `PerTurnScalingPersistenceFactor` is one global setting. If Demon Form-style per-turn scaling outranks immediate scaling against the evidence, lower this factor rather than adding a card-specific rule.
- **Role data.**
  - Engine powers (Feel No Pain, Dark Embrace, Juggernaut, Rupture, Vicious, Cruelty, Juggling, Stampede, Hellraiser) get `SetupTurns 1`. Crimson Mantle gets 0.
  - Rage's block events move into its conditional.
  - Exhaust payoffs assume 1.5 exhausts per turn at full condition.
  - Energy values are gross gain.

**Model** (implement exactly; constants go in `ScoringWeights`):

1. Expand the deck to instances, plus the candidate when scoring `D ∪ c`. Cards without a role are **filler**: they count in `N` and contribute nothing.
2. `N` = instance count. `D` = Σ `Draw` over instances (attacks and skills only).
   - Hand size `H = min(10, 5 / max(0.5, 1 − D/N))`.
   - Each instance is seen per turn with probability `p = H/N`.
   - Powers are drawn once. Their `PerTurn` effect counts with persistence `P(stage)` = {Act1Early 1.5, Act1 2.0, Act2 2.5, Act3 3.0} and multiplier `p`.
3. Energy:
   - `supply = 3 + Σ p·Energy + Σ p·P·PerTurn.Energy`;
   - `demand = Σ p·cost` (X-cost = 2; null cost = 0);
   - `feasibility = demand ≤ 0 ? 1 : min(1, supply/demand)`.
4. Job capability for each job `j` in {frontload, aoe, block, scaling, draw, energy}:
   `J_j = feasibility · Σ p · (effect_j + r·conditional_j) + Σ powers p·P·setupFactor·(perTurn_j + r·perTurnConditional_j)`.
   - Draw and energy jobs come from the same fields, so they are not double counted.
   - `HpLoss` becomes negative block: `−HpLoss/5 · hpWeight` (hpWeight 0.5 by default; low HP is handled in Task 6).
   - Clamp every job at ≥ 0.
5. Reliability `r` by condition kind, from the deck **including** the candidate:
   - vulnerable: `min(1, S/3)` with `S = Σ VulnerableSource`;
   - exhaust: `min(1, X/3.5)` with `X = Σ ExhaustSource`;
   - hpLoss: `min(1, L/3)` with `L` = instances with `HpLoss > 0` or a `PerTurn.HpLoss`;
   - strikeNamed: `min(1, k/6)`, where `k` = instances whose pack `Name` contains "Strike" (ordinal, case-insensitive);
   - attacks: `min(1, 5·attackFraction·feasibility/3)`;
   - block: `min(1, J_block / T_block)`.
   - `RareDependency` present but none owned → `r × 0.5`.
6. Copy behavior: `nonStacking` copies beyond the first contribute 0. `selfLimiting` copies beyond the first multiply by 0.7 per extra copy. `stackable` and `uncertain` count in full.
7. Setup factor (powers with `SetupTurns ≥ 1`, and the scaling job): {Act1Early 0.5, Act1 0.7, Act2 0.9, Act3 1.0}.
8. Utility `V(D) = Σ_j w_j(stage) · ln(1 + J_j / T_j)`, where `T_j` is the job capability of the 0.107.1 starter deck (5 Strike, 4 Defend, 1 Bash) at the same stage, floored at 0.25.
   - Need weights `w_j` come from research §4 R2 (stage rows).
   - The draw and energy weights apply to their own jobs.
9. Card score `= 10 · (V(D ∪ c) − V(D))`. Upgraded offers use `Upgraded`/`UpgradedPerTurn` and the upgraded cost.
10. Skip score `= −θ(stage)`, with θ = {Act1Early −0.5, Act1 0, Act2 0.5, Act3 1.0}. A boss reward (all three offers Rare) subtracts a further 0.5 from θ.
    - Rank Skip alongside the offers.
    - Ties are broken by EntityId (ordinal) then slot; "skip" sorts by its literal ID.
11. Stage: Act ≥ 3 → Act3; Act 2 → Act2; Act 1 → Act1Early when `Floor` is null and deck size ≤ 13, or when `Floor ≤ 4`; otherwise Act1.
12. Keep existing behavior:
    - `BlockReason` still gates ranking.
    - Research-prior claims add `claim.Weight · confidence` as a separate reason line.
    - The 30-day freshness multiplier still applies.
13. Explanations (all invariant culture; at most 4 reason lines for the overlay):
    - Line 1: `"Needs: <weakest job> (<value>/<target>); this card adds <delta> <job>"`.
    - Line 2: the reliability fact when a conditional applies, e.g. `"Vulnerable sources in deck: 3 (Bash+, Tremble, Taunt) → condition met ~100%"`.
    - Line 3: the tradeoff, e.g. `"Tradeoff: deck 17 → 18 cards; energy demand 3.4 vs 3.0 supply"`.
    - Skip reason: `"No offer improves the deck enough for <stage>; skipping keeps <key card> drawn more often"`.
    - Full detail goes in the remaining reasons.

- `DeckRanking.Rank`: if `context.Character == "ironclad"` and roles load and validate, return `RewardScoring.Rank(...)`. Otherwise run the existing code unchanged. Do not delete the legacy path.
- `OverlayWindow`: use `DeckRanking.DisplayName` everywhere it currently calls `pack.Entities.Single(e => e.Id == r.EntityId)`. When Skip is first, the headline reads "Recommended: Skip". The upgraded "+" lookup must not dereference slot −1.
- Seeder: no change beyond Skip appearing in output JSON.

- [ ] **Step 1:** Write the failing tests in `RewardScoringTests`:
  - `SkipIsRankedAndCanWin`: research S03 deck; Skip ranks first.
  - `FloorOneNeverSkipsAFrontloadAttack`: S01 starter deck; top ≠ skip and top ∈ {Anger, Twin Strike}.
  - `DeckSizeNowChangesDecisionAgainstSkip`: the same weak offer is taken in a 12-card Act 1 deck and skipped in a 28-card Act 3 deck.
  - `DegenerateDecksAreFinite`: loop over decks of size 1, 2, 3, 40, 60, all-powers and all-zero-cost; all scores finite and the output deterministic across 3 runs and the fr-FR culture.
  - `UnannotatedDeckCardsAreFiller`: a deck with `sts2.break` + `sts2.ascenders_bane` doesn't throw, and the reasons mention filler.
  - `SkipNeverReachesEntityLookup`: `DeckRanking.DisplayName` handles skip.
- [ ] **Step 2:** Confirm they fail.
- [ ] **Step 3:** Implement `ScoringWeights`, `RewardScoring`, the `DeckRanking` delegation, the `Models` changes and the `OverlayWindow` changes.
- [ ] **Step 4:** Update the existing tests that assert old Ironclad reason text or ranking (`RewardTests.DeckFeaturesChangeRankingWithConcreteReasons`, `RankingsAreDeterministicAndUpgradesMatter`). Keep their **intent**: deck features change ranking with concrete reasons; determinism; upgrades matter; a Vicious with no Vulnerable sources isn't recommended. Do not weaken them to trivial assertions. `CoreTests.DeterministicGoldenRanking` uses fixture cards (non-Ironclad legacy path) and must remain byte-identical.
- [ ] **Step 5:** Raise `DevRatchet` to the measured hit count, which must be at least 12. Run all tests.
- [ ] **Step 6:** Try `dotnet build src/Scout.Windows/Scout.Windows.csproj -p:EnableWindowsTargeting=true`. Report whether WPF compiles on macOS. If it can't, say so; the evaluator inspects the diff by hand and CI on Windows is the final check.

**Amendments from the Task 4 review (2026-10-26):**
- **F1:** proportional feasibility is replaced by greedy funded-play allocation. It uses value density from the current deck's log-utility gradient and funds energy cards first.
- **F2:** reliability-weighted conditional energy counts toward supply.
- **F3:** invariant-culture date formatting.
- **F4:** a missing rare dependency halves a card's whole contribution when the card has no conditional.
- Skip scores +θ; the earlier "−θ" was an error in this plan.

### Task 5: Conditions, copies, timing, unmodeled cards, close calls

*(Amended)* Two additions:
- Vulnerable sources multiply attack frontload/AoE by `1 + 0.5·min(1, S/3)` (`VulnerableDamageBonus`, a hypothesis).
- `PerTurnScalingPersistenceFactor` is chosen from {1.0, 0.8, 0.6, 0.5}, using a consensus test: Dominate and Inflame outscore Demon Form in Act 1.

**Files:** Modify `src/Scout.Core/RewardScoring.cs`, `ScoringWeights.cs`; tests in `RewardScoringTests.cs`.

- Unmodeled offers (`Modeled == "none"`, no role at all, or any colorless offer in this version): score = median score of the modeled offers on the same screen (0 if none). The band is ±1.5, which forces `CloseCall` if it overlaps the top. The reason is `"Scout can't fully evaluate <name>; judge it yourself"`. `partial` cards get their modeled part, plus a reason line noting what is not modeled.
- Close calls: recompute every offer's score with reliability shifted −0.2 and +0.2 and with the adjacent stage. If the top option's minimum is below the second option's maximum, or the margin is under 0.75, set `CloseCall = true` on the top two. Add the reason `"Close call vs <other>: <dominant job difference>"`.
- Upgrade class: in Act3, `large`-class base copies multiply their upgraded-minus-base delta value by 0.8 when computing their own score. (This represents fewer remaining upgrades; it is a hypothesis.)

- [ ] **Step 1:** Write failing tests:
  - `UnsupportedVulnerablePayoffLoses` (S07: Pommel beats Vicious);
  - `SupportedPayoffWins` (S11: Dominate first);
  - `NonStackingDuplicateIsNotTaken` (S06: Impervious beats Barricade, and Barricade ranks below Skip);
  - `UsefulDuplicateIsTaken` (S05: Dismantle first);
  - `UnmodeledNeverWinsByDefault` (an offer with a role removed from a cloned `CardRoleSet` ranks at most second and sets `CloseCall`);
  - `CloseCallFlaggedForNearTie` (S02 or S15).
- [ ] **Step 2:** Implement the changes. Raise `DevRatchet` (target ≥ 15). All tests pass.

### Task 6: New inputs — HP, Floor, relics, colorless gating

**Files:** Modify `src/Scout.Core/Models.cs` (`RunContext` gains `int? Hp = null, int? MaxHp = null, int? Floor = null` at the end of the positional list), `RewardScoring.cs`, `ScoringWeights.cs`, `src/Scout.Core/RewardRecognition.cs` or wherever `CatalogValidation.Pool` is used for OCR, `src/Scout.Windows/DeckEditor.cs`, `Settings.cs`. Tests in `RewardScoringTests.cs` and `SetupTests.cs`.

- HP:
  - When `Hp` and `MaxHp` are known and `Hp/MaxHp < 0.4`: block weight ×1.5, setup factor ×0.7, hpWeight 1.5.
  - At ≥ 0.75: hpWeight 0.3.
  - Validation: `1 ≤ MaxHp ≤ 999` and `0 < Hp ≤ MaxHp` when set; otherwise `InvalidDataException("HP must be between 1 and max HP")`.
- Floor: used by `StageOf` (Task 4 rule). Valid range 1–60.
- Relics (`RunContext.Relics`, existing field, IDs from `CardRoleSet.Relics`), applied generically by `Effect` kind:
  - `vulnerableSource` adds to S;
  - `vulnerablePayoff` multiplies conditional vulnerable effects;
  - `hpLossRelief` multiplies hpWeight by (1 − value);
  - `strikeBonus` adds Frontload per Strike-named instance;
  - `exhaustPayoff` adds Aoe per exhaust;
  - `scaling` adds to the scaling job directly;
  - `blockPerTurn` adds to the block job;
  - `colorlessRewards` enables colorless OCR candidates.
  - Unknown relic IDs are ignored with a reason line.
- Colorless gating: the OCR recognition pool includes colorless cards only when the relics contain an effect `colorlessRewards`. The manual "Enter cards" dialog keeps colorless cards selectable. `BlockReason` must still accept a manually entered colorless offer.
- DeckEditor: add numeric HP / Max HP / Floor fields (blank = unknown) and a checkbox list of the relics from `CardRoles.Load().Relics`. Settings must still load old JSON without these fields (`RespectNullableAnnotations` and positional defaults; test this with an old settings JSON fixture).

- [ ] **Step 1:** Write failing tests:
  - `LowHpPrefersImmediateBlock` (S10 with HP 30/80: Flame Barrier first; with HP 75/80: not necessarily);
  - `RelicAddsVulnerableSource` (Bag of Marbles raises Vicious/Dominate reliability, shown in the reasons);
  - `HpLossReliefReducesSelfDamageCost`;
  - `ColorlessExcludedFromOcrPoolWithoutDingyRug`;
  - `OldSettingsJsonStillLoads`;
  - `InvalidHpRejected`.
- [ ] **Step 2:** Implement the changes. Raise `DevRatchet` (target ≥ 16). All tests pass.

### Task 7: Documentation and final verification

- Update `docs/card-reward-mvp.md` "Recommendation limits" and the editor steps (HP, Floor, relics, Skip, close calls). Update `docs/architecture.md` "Recommendations". Link the research doc.
- Run the full suite, `dotnet format --verify-no-changes`, the Seeder `evaluate` on dev and held-out sets, and the WPF build attempt.
- The final report includes the dev and held-out hit counts, before and after.

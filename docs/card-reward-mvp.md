# CardReward MVP

Scout's reward path is entirely offline: calibrated screen detector → three name crops → local Tesseract → character-pool catalog matching → three stable frames → explicit current deck → deterministic job-ledger ranking with Skip (Ironclad). It never selects a card. Merchant code retains its original template and price behavior; it has no new data or calibration in this milestone.

## First real recommendation on Windows

1. Download **Sts2Scout-win-x64** from a successful Windows Actions run, extract the entire folder, and launch `Sts2Scout.exe`. Windows 11 x64 is the target. .NET and the English OCR model are included. If native OCR initialization fails, install Microsoft's Visual C++ 2015–2022 x64 Redistributable. No SDK or source checkout is needed for normal setup.
2. This build always uses its bundled full catalog; old LocalAppData strategy files cannot override it. The first launch of the new setup version clears the old run/deck and starts interactive. Subsequent launches retain your entered run. Existing scan calibration is upgraded where possible. History can contain different pack contents sharing a display version without blocking advice.
3. Open **Edit deck**, choose your character, enter the actual game version **0.107.1** (a leading `v` is accepted), act, and ascension. Find a card, set its total copies and upgrade state, and click **Add card**. Base/upgraded copies are separate entries. **Remove card** removes a selected entry; **Clear deck** empties the list. Optionally enter **HP**, **Max HP** and **Floor** (blank means unknown; HP must be between 1 and max HP, floor 1–60) and tick the relics you own from the listed advice-relevant ones. **Save deck** applies changes. Do not enter a supported version if your game actually uses a different patch.
4. For immediate manual advice, click **Enter cards**, select all three offered names and upgrades, and click **Get advice**. This works before OCR is calibrated. Manual advice pauses scanning; enter a new reward manually or select **Resume scan** before automatic scanning.
5. For automatic scanning, focus an unhovered three-card reward, return to Scout, and use **Scan setup → Set up scan**. Draw boxes around the reward heading and each of the three card names. See [visual setup instructions](calibration.md). No diagnostic file exchange or command shell is required.
6. Focus the game. After three stable frames, Scout displays recognized names, a recommendation, alternatives, and deck-specific reasons. For Ironclad, **Skip (take no card)** is ranked with the offers and can be the recommendation. "(close call)" means the top two options swap under small changes to Scout's assumptions (condition reliability ±0.2 or the neighbouring run stage) or are within 0.3 points; treat either as reasonable. The flag means Scout's own estimate is uncertain, not that experts disagree. Expert-disputed cards often do not get it. The reason lines read: what the deck needs and what this card adds; how reliably its condition is met (for example Vulnerable sources); the tradeoff (deck size, energy demand vs supply); then upgrade, HP, relic and job-change details. **Why this card?** (or **Why skip?**) expands every line. **Scan details** shows raw OCR and rejection reasons. Unread cards are labeled as unread, not misleadingly displayed as zero-percent certainty.
7. Pick the card yourself and update **Edit deck**. Nothing is inferred from a recommendation. The window is movable and resizable; position it beside the game or on another monitor. Its position is saved. **Ctrl+Shift+S** toggles click-through, which is off by default. Advice remains visible when switching away from the game; it is the last observed reward, not a fresh background scan.

Scope remains English, single-player, three-card rewards for catalog patch 0.107.1. Default OCR regions are starting points, not guarantees for every layout. Real Windows gameplay validation is still required.

## Offline replay

```powershell
dotnet run --project tools/Scout.Seeder -- recognize data/strategy-pack.json "$env:LOCALAPPDATA\Sts2Scout\calibration.json" C:\captures\reward.pgm --context C:\scout\run.json --json
```

The context is a **RunContext**, not the enclosing settings file. Example:

```json
{
  "gameVersion": "0.107.1", "character": "ironclad",
  "deck": [], "relics": [], "needs": [], "archetypes": [],
  "act": 1, "ascension": 0, "gold": 0, "reserveGold": 0,
  "cards": [
    {"entityId": "sts2.bash", "copies": 3, "upgraded": false},
    {"entityId": "sts2.pommel_strike", "copies": 2, "upgraded": true},
    {"entityId": "sts2.defend_ironclad", "copies": 5, "upgraded": false}
  ]
}
```

This is a constructed evaluation deck, not the deck inferred from the supplied captures. Without context, replay defaults to the Ironclad pool and withholds a recommendation because the deck is empty. JSON includes dimensions, pixel hash, screen score, all OCR variants, raw text, normalized text, top candidates, rejections, upgrade state, final IDs, versions, and rankings. A fixed Unix-epoch observation time makes fixture replay deterministic; it does not simulate current data age. Live observations use capture time. PGM support remains the repository's P2/255 grayscale format. No game launch is needed. On an Apple Silicon development Mac install `brew install tesseract`; the adapter uses `/opt/homebrew/lib/libtesseract.dylib`. Linux uses `libtesseract.so.5`. Windows native OCR libraries are bundled through NuGet.

## Recognition decisions

Compared options: positional templates cannot generalize; Windows.Media.Ocr is compact but tied to Windows language availability and Windows-only evaluation; larger ONNX OCR stacks add model/runtime complexity; Tesseract supplies local cross-platform evaluation and distributable Windows native libraries. The `tessdata_best` English model gave better readings of the supplied outlined font than `tessdata_fast`, so its approximately 15 MB cost is accepted. No trained card-name templates or screenshot uploads are used.

Preprocessing retains original grayscale samples, tries natural/2×/4× text, then isolates light letter interiors by removing boundary-connected ribbon pixels and detached flat specks, with bilinear enlargement at several fixed thresholds. This is tailored to outlined English names. Raw engine confidence must be at least 0.60, normalized edit similarity at least 0.90, and the next candidate at least 0.08 behind. These are measurable engineering thresholds, not calibrated probabilities. Case, spacing, punctuation and common `0/o`, `1/l`, `|/l` substitutions normalize before comparison. Short or unrelated text stays unknown. Only the chosen character's Common/Uncommon/Rare single-player pool is eligible for OCR; colorless cards are added only when the run's relics include Dingy Rug. **Enter cards** still lists colorless cards, so special rewards can be entered manually. Special event/cross-character rewards require future explicit pool configuration.

A recognized trailing `+` supplies positive upgrade evidence separately from the normalized name. An OCR variant omitting the thin plus is not negative evidence. Accepted readings that identify different cards withhold the slot. Low-confidence or truncated upgrade punctuation can still be missed; review the displayed upgrades and use correction. The application does not claim perfect recognition of all catalog names/fonts/layouts. Three distinct slots, the screen match, and a valid nonempty deck are all required for a reward recommendation.

## Current deck source decision

Community projects show `current_run.save` can contain deck state, but research did not establish a publisher-supported stable read API, current schema guarantees, or a representative local single-player save fixture. [The community save investigation](https://github.com/MufanQiu/sts2-save-rebuild) documents lifecycle/cloud and schema-template dependencies. Its modification workflows are outside Scout's boundary and are not used. A guessed adapter could silently use stale or incompatible state, so the editor is the MVP source of truth. `DeckEntry[]` and `Deck.Migrate/Validate` form the future adapter boundary. Runtime opens no game files. Legacy repeated deck IDs migrate to base copies in memory and are saved with settings on the next editor save; no upgrade is invented and existing settings are retained until then.

## Recommendation limits

Ironclad rewards use a job ledger (design and evidence: [research](ironclad-reward-research.md), [plan](superpowers/plans/2026-10-26-ironclad-advice-v2.md)). Hand-reviewed role data for patch 0.107.1 (`src/Scout.Core/Data/card-roles.0.107.1.json`) states what each of the 80 reward cards and the starter cards contributes to six jobs (frontload, AoE, block, scaling, draw, energy), plus conditions, copy behaviour and upgrade class. The deck's capability per job is compared with the starter deck's, a concave utility is applied, and each offer scores the utility change it causes; Skip scores a stage threshold. Conditional effects are weighted by how reliably the deck meets the condition; HP, floor and listed relics adjust the weights. Role data that fails to load or does not match the patch falls back to the legacy heuristic. Every weight lives in `ScoringWeights` and is a tuning hypothesis, not a measured fact. Six community priors still add small, attributed amounts; age over 30 days discounts scores 10%; exact patch mismatch blocks ranking. Output is deterministic, with ordinal ties and invariant numbers.

Known limits:

- Energy feasibility is proportional (supply ÷ expected demand), so energy and free plays are overvalued in decks that can't use them. Havoc can beat Skip, and Pyre or Bloodletting can beat Uppercut in low-curve decks.
- Colorless offers are not modeled. They receive the median of the other offers, are marked "judge it yourself", and never win a tie.
- Act bosses, potions, the map, upcoming elites, multiplayer, enchantments and card-instance modifiers are not modeled. Neither are timing details beyond the role notes, or Strength boosting attack damage.
- Silent, Defect, Necrobinder and Regent still use the legacy tag heuristic.

This is not a combat simulator, an optimal policy or a win probability. The editor must be maintained by the player.

Evaluation: 20 dev scenarios (`tests/Scout.Tests/Fixtures/ironclad-scenarios.dev.json`) and a held-out set written independently by the evaluator. Acceptable hits went from 8 to 17 on dev (preferred 7 to 15) and from 12 to 20 of 26 on held-out. Both sets are reasoned judgments, not empirical benchmarks.

# CardReward MVP

Scout's reward path is entirely offline: calibrated screen detector → three name crops → local Tesseract → character-pool catalog matching → three stable frames → explicit current deck → deterministic heuristic ranking. It never selects a card. Merchant code retains its original template and price behavior; it has no new data or calibration in this milestone.

## First real recommendation on Windows

1. Download **Sts2Scout-win-x64** from a successful Windows Actions run, extract the entire folder, and launch `Sts2Scout.exe`. Windows 11 x64 is the target. .NET and the English OCR model are included. If native OCR initialization fails, install Microsoft's Visual C++ 2015–2022 x64 Redistributable. No SDK or source checkout is needed for normal setup.
2. This build always uses its bundled full catalog; old LocalAppData strategy files cannot override it. The first launch of the new setup version clears the old run/deck and starts interactive. Subsequent launches retain your entered run. Existing scan calibration is upgraded where possible. History can contain different pack contents sharing a display version without blocking advice.
3. Open **Edit deck**, choose your character, enter the actual game version **0.107.1** (a leading `v` is accepted), act, and ascension. Find a card, set its total copies and upgrade state, and click **Add card**. Base/upgraded copies are separate entries. **Remove card** removes a selected entry; **Clear deck** empties the list. **Save deck** applies changes. Do not enter a supported version if your game actually uses a different patch.
4. For immediate manual advice, click **Enter cards**, select all three offered names and upgrades, and click **Get advice**. This works before OCR is calibrated. Manual advice pauses scanning; enter a new reward manually or select **Resume scan** before automatic scanning.
5. For automatic scanning, focus an unhovered three-card reward, return to Scout, and use **Scan setup → Set up scan**. Draw boxes around the reward heading and each of the three card names. See [visual setup instructions](calibration.md). No diagnostic file exchange or command shell is required.
6. Focus the game. After three stable frames, Scout displays recognized names, a recommendation, alternatives, and deck-specific reasons. **Why this card?** expands scoring details. **Scan details** shows raw OCR and rejection reasons. Unread cards are labeled as unread, not misleadingly displayed as zero-percent certainty.
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

Preprocessing retains original grayscale samples, tries natural/2×/4× text, then isolates light letter interiors by removing boundary-connected ribbon pixels and detached flat specks, with bilinear enlargement at several fixed thresholds. This is tailored to outlined English names. Raw engine confidence must be at least 0.60, normalized edit similarity at least 0.90, and the next candidate at least 0.08 behind. These are measurable engineering thresholds, not calibrated probabilities. Case, spacing, punctuation and common `0/o`, `1/l`, `|/l` substitutions normalize before comparison. Short or unrelated text stays unknown. Only the chosen character plus colorless Common/Uncommon/Rare single-player pool is eligible. Special event/cross-character rewards require future explicit pool configuration.

A recognized trailing `+` supplies positive upgrade evidence separately from the normalized name. An OCR variant omitting the thin plus is not negative evidence. Accepted readings that identify different cards withhold the slot. Low-confidence or truncated upgrade punctuation can still be missed; review the displayed upgrades and use correction. The application does not claim perfect recognition of all catalog names/fonts/layouts. Three distinct slots, the screen match, and a valid nonempty deck are all required for a reward recommendation.

## Current deck source decision

Community projects show `current_run.save` can contain deck state, but research did not establish a publisher-supported stable read API, current schema guarantees, or a representative local single-player save fixture. [The community save investigation](https://github.com/MufanQiu/sts2-save-rebuild) documents lifecycle/cloud and schema-template dependencies. Its modification workflows are outside Scout's boundary and are not used. A guessed adapter could silently use stale or incompatible state, so the editor is the MVP source of truth. `DeckEntry[]` and `Deck.Migrate/Validate` form the future adapter boundary. Runtime opens no game files. Legacy repeated deck IDs migrate to base copies in memory and are saved with settings on the next editor save; no upgrade is invented and existing settings are retained until then.

## Recommendation limits

All cards have a neutral authored baseline, discounted by extraction confidence; six Ironclad community associations add deliberately tiny priors. Local scoring considers deck size, duplicates, upgraded copies, damage/block/draw/selection/energy/scaling/debuff coverage, energy demand, Vulnerable triggers, Exhaust payoffs/conflicts, burden handling, setup risk and configured act/ascension. Concrete owned-card counts appear in explanations. Sourced global associations include source/claim IDs; they are distinct from Scout's own inferences. Age over 30 days discounts live scores 10%; exact patch mismatch blocks ranking. The engine is deterministic for identical inputs and observation time, with ordinal ties and invariant numeric explanations.

This is a transparent feature heuristic, not a combat simulator, optimal policy or win probability. Structured catalog mechanics omit full conditional timing prose; complex mechanics, relic-dependent costs, HP, upcoming enemies, multiplayer, enchantments, infinite upgrades and card-instance modifiers are not modeled. Source uncertainty is explicit. No evidence supports card-specific act/ascension adjustments here; configured act/ascension affect authored need/setup heuristics only. The editor must be maintained by the player.

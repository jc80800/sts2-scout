# CardReward MVP

Scout's reward path is entirely offline: calibrated screen detector → three name crops → local Tesseract → character-pool catalog matching → three stable frames → explicit current deck → deterministic heuristic ranking. It never selects a card. Merchant code retains its original template and price behavior; it has no new data or calibration in this milestone.

## First real recommendation on Windows

1. Download the Windows workflow artifact for this branch/commit, extract the complete folder, and run `Sts2Scout.exe`. Windows 11 x64 is the target. .NET and English OCR data are included. The native Tesseract build needs Microsoft's **Visual C++ 2015–2022 x64 Redistributable** if it is not installed: [Microsoft's supported download](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). No Windows OCR language pack, Tesseract installer, API key, or internet connection is needed during play. A missing native prerequisite reports an OCR initialization error; it has not been tested on a clean Windows machine here.
2. Keep your working `%LOCALAPPDATA%\Sts2Scout\calibration.json`. Back up an older local `strategy-pack.json` and remove it if you want the new bundled catalog; a compatible old local pack takes priority. Do not use the synthetic test profile for the game.
3. From the source checkout with .NET 10, configure the name regions once:

   ```powershell
   dotnet run --project tools/Scout.Seeder -- reward-regions "$env:LOCALAPPDATA\Sts2Scout\calibration.json"
   ```

   This preserves the working screen detector and merchant probes. The default regions were measured from the supplied 1920×1080 frames: x=531,881,1231; y=460; width=171; height=28. They scale with a 16:9 client. They are a starting point, not a guarantee for different UI scales, long names, hover states, languages, or layouts. Custom normalized x/y/width/height tuples can follow the profile path (three tuples, twelve numbers). Include the entire outline and trailing plus, but avoid the cost badge and artwork. No card-specific template is created.
4. Launch Scout, press **Ctrl+Shift+S**, choose **Review / edit current deck**. Select your character, type game version **0.107.1** (without `v`), set act and ascension, search each card, choose base/upgraded, set copies, and press **Add card / set copies**. Review the list and **Save current run**. Base and upgraded copies are separate rows. Remove a row and add the corrected variant to change its upgrade state. **New run** clears the deck; saving is explicit. Nothing is inferred from a prior recommendation.
5. In a **single-player**, English v0.107.1 run, focus the game on an unhovered three-card reward. After three stable frames the overlay shows each name, upgrade state, name-match score, recommendation, alternatives, deck-specific reasons, versions, and cached date.
6. If any name is Unknown, use **Correct reward names / upgrades**. Select all three names from the current pool and explicitly set their upgrades. Confirming pauses capture and labels the result **USER CONFIRMED**; it does not interact with the game. Choose **Resume capture after correction** before the next reward. OCR raw readings/candidates and detailed score contributions can be expanded.
7. Pick your card yourself, then update the deck editor. Scout does not track which offer you selected, automatic deck growth, relic changes, or removals.

If the pack, configured game, and calibration versions differ, correct the mismatch; do not relabel a newer game's data as 0.107.1. The supplied capture's watermark matches this researched stable snapshot. Other patches, including beta 0.110.x, are intentionally unsupported.

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

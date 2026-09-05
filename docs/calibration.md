# Local diagnostic calibration

Scout ships no game imagery or recognition profile. Its template recognizer is an extensible baseline, not an OCR model. It normalizes rectangular grayscale samples to 32×16 pixels, uses mean absolute pixel difference, requires a threshold and runner-up margin, and refuses aspect-ratio mismatches. “Confidence” is a match score, not a statistically calibrated probability. Whole prices, entity labels and upgrades require their own templates; partial OCR guessing is not used.

1. Enable diagnostics and capture a reward frame and merchant frame as described in the Windows guide. Use a fixed resolution/UI scale, language, game patch and stable non-hover state. PGM P2 is a plain grayscale image format readable by common image tools. Do not commit your game captures without checking rights.
2. Prepare a local entity catalog and strategy pack using the seeding guide; the empty bundled pack does not supply game IDs. Store the pack as `%LOCALAPPDATA%\Sts2Scout\strategy-pack.json`.
3. From a source checkout, run the development calibration command. Coordinates are normalized client fractions (x / client width, y / client height, region width / client width, region height / client height). Choose small, distinctive, static screen-title regions and item-name regions; avoid animated backgrounds, pointer highlights or full-card artwork. The following values are illustrative geometry, **not measured STS2 coordinates**:

```powershell
$scout = "$env:LOCALAPPDATA\Sts2Scout"
$frame = "$scout\diagnostic-YOUR-TIMESTAMP.pgm"
$profile = "$scout\calibration.json"
dotnet run --project tools/Scout.Seeder -- calibrate $profile $frame screen CardReward 0 reward 0.3 0.05 0.4 0.08 YOUR-PATCH
dotnet run --project tools/Scout.Seeder -- calibrate $profile $frame entity CardReward 0 YOUR-CARD-ID 0.1 0.3 0.2 0.08 YOUR-PATCH
```

4. Add one screen probe per supported screen, and entity probes per visible slot (zero-based indices). Use `Merchant` for merchant frames. Repeating the same kind/screen/slot with a new label adds a competing template in the same region. A repeated label replaces its old template. Add all supported entity alternatives for each slot. Ambiguous matches become unknown.
5. Add merchant price templates using `price Merchant 0 75 ... YOUR-PATCH` (75 is an example known price label). Every distinct supported displayed price needs a template. Add `upgrade CardReward 0 upgraded ... YOUR-PATCH` only where a distinct upgrade indicator exists. Missing upgrade probes mean “no recognized upgrade,” not a verified base card.
You can also calibrate a `selection` probe with the selected entity ID as its label. A stable visual indicator produces an unconfirmed selection hypothesis only when its entity matches the recognized offered slot; hover highlighting is not proof of a decision.

6. Keep all templates in one profile at the same aspect ratio. Set `context.gameVersion` to `YOUR-PATCH` and restart Scout. Bad profiles fail at startup with an error rather than silently relaxing recognition. Test both supported screens, negatives, hovers, tooltips and transitions before trusting local results.
7. If a patch, language, UI scale or appearance changes, recalibrate. Back up the old profile and remove it to start clean. The CLI updates the profile version each time; archived observations retain that version.

The checked-in synthetic test profile is a JSON structure example only. Do not install it for STS2. A production OCR adapter can implement `IScreenRecognizer`, returning the same confidence-bearing `Observation` contract. Calibrated template matching currently handles card/relic/potion/service IDs identically; entity kinds come from the local catalog. No accuracy claim applies beyond the synthetic fixtures. There is no automatic calibration UI or game dataset download.

# Windows user guide

For CardReward OCR, deck entry, correction, and the first real recommendation, follow [the current MVP guide](card-reward-mvp.md). The capture/install details below still apply; manual deck settings and per-card template steps are legacy alternatives.

## Requirements

Windows 11 x64 is the supported target. Windows 10 2004+ has the capture exclusion API, but Windows 10 is not the tested/support target. Use a normal desktop session with DWM, single-player STS2, and windowed or borderless mode. Exclusive fullscreen, remote desktop, HDR, color filters, overlapping third-party windows, and elevated game processes may prevent usable capture. Scout runs as your normal user, without administrator rights.

## Download a prebuilt binary

A successful Windows workflow run is required before an artifact exists. This implementation does not publish a release or start a workflow remotely.

1. Open [the repository's Actions page](https://github.com/jc80800/sts2-scout/actions).
2. Select **Windows build**, then a successful run for the commit you intend to use. You may need to sign into GitHub to download workflow artifacts.
3. Under **Artifacts**, download **Sts2Scout-win-x64**. Artifacts expire after 30 days; use a newer successful run if expired.
4. Right-click the downloaded ZIP, select **Extract All**, and extract to a directory you own, for example `%USERPROFILE%\Apps\Sts2Scout`. Do not extract into Steam or the STS2 game directory.
5. Open the extracted directory and run **Sts2Scout.exe** beside `Sts2Scout.dll`, the other DLLs, and `data\strategy-pack.json`. Keep the whole directory together. The ZIP contains these files directly, not another ZIP or a single-file executable.

There is no Releases download until a maintainer explicitly publishes one. The self-contained x64 artifact includes .NET; end users do not need the SDK or a separate .NET runtime. Builds are unsigned: Windows SmartScreen may show “Windows protected your PC.” Check the origin/commit first; if you trust that build, **More info → Run anyway** permits it. Do not disable Windows security globally. Managed devices may require administrator/IT approval for unsigned applications.

## Build from a fresh clone

Install Git for Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) for x64. `global.json` selects any installed .NET 10 feature band at or above 10.0.100. Python 3.11+ is used only for repository checks. Run the exact PowerShell commands in the root README. Visual Studio is optional; if used, install a version supporting .NET 10 and the desktop development workload.

The publish output is `artifacts\Sts2Scout-win-x64\Sts2Scout.exe`. A plain `dotnet build` output is framework-dependent and is not the user download. First restore/publish needs internet for packages and Windows runtime packs. Running the published app afterward does not.

## Start and use

1. Launch Scout before or after starting STS2 from Steam. No Steam launch arguments or game modifications are needed.
2. Scout displays a waiting panel until it finds a visible process whose executable basename matches `processName` (default `SlayTheSpire2`). Verify the actual basename in Task Manager → Details if detection fails; omit `.exe` in settings. This name is configurable because it has not been verified on this development Mac.
3. Focus the game. Scout captures only while the game is foreground, at most once per configured polling interval. Minimized/unfocused games pause. The overlay follows the client origin, retaining its configured offsets.
4. The default **Ctrl+Shift+S** toggles mouse interaction with Scout. Click-through mode passes mouse interaction to the game; interactive mode lets you press Scout buttons. It does not automate game input. Press the hotkey again or click **Return to click-through** to resume.
5. The initial panel says **Calibration required**. Follow [calibration](calibration.md) and [seeding](seeding.md). The bundled patch-specific catalog supplies card facts; OCR name regions and an entered deck are required. A configured version mismatch also suppresses recommendations.
6. Once calibrated, visit a reward or merchant screen. Three consecutive recognized observations with matching choices are required before recording/ranking. Unknown entities/prices are never guessed. Merchant rankings require known prices and enough manually configured gold.
7. Heuristic scores are not win probabilities. Skipping a reward or saving gold remains an option. Merchant disappearance generates only an unconfirmed hypothesis; exiting a reward screen alone does not identify which card was picked.
8. Use the hotkey and **Quit Scout** to close. Alt+F4 also closes the focused Scout window. Only one instance runs per Windows session.

## Settings and run context

After the first start, close Scout and edit `%LOCALAPPDATA%\Sts2Scout\settings.json` with Notepad. Restart to apply changes. All settings use the exact JSON names below:

```json
{
  "processName": "SlayTheSpire2",
  "offsetX": 24,
  "offsetY": 80,
  "opacity": 0.9,
  "clickThrough": true,
  "hotkeyModifiers": 6,
  "hotkeyVirtualKey": 83,
  "diagnosticCapture": false,
  "pollMilliseconds": 1000,
  "context": {
    "gameVersion": "unconfigured",
    "deck": [], "relics": [], "needs": [], "archetypes": [],
    "act": 1, "ascension": 0, "gold": 0, "reserveGold": 0
  }
}
```

`offsetX`/`offsetY` are WPF device-independent pixels relative to the game client; `opacity` ranges from 0.2 to 1. `clickThrough: false` starts interactive. The modifier bitmask uses Alt=1, Ctrl=2, Shift=4, Win=8; add values. Key values are Win32 virtual-key codes expressed in decimal (83=S). Scout registers a global hotkey with no-repeat. If registration fails because another app owns it, Scout starts interactive and displays an error; change the keys and restart.

Deck and relic arrays contain reviewed catalog IDs; duplicate deck IDs represent copies. Needs/archetypes are explicit tags matching the pack. Set the exact patch string in pack, calibration and context. **No automatic deck, act, ascension or gold tracking is implemented**. Use the in-app deck editor for deck, character, act and ascension; manual JSON editing is not required for a reward recommendation. Stale context leads to stale recommendations. Price recognition supports calibrated whole-price templates; uncalibrated prices remain unknown.

## Diagnostics and local files

Set `diagnosticCapture` to true and restart to opt in. Focus the game for at least one poll, toggle Scout interaction, then click **Save diagnostic frame** within 30 seconds. Each explicit click saves one grayscale `.pgm` frame; no continuous screenshot collection occurs. Local captures may contain visible personal information; review before sharing. Turn the setting off and restart to disable saving.

All generated runtime files are in `%LOCALAPPDATA%\Sts2Scout`:

- `settings.json`: settings and user-entered run context.
- `scout.db`: SQLite observations, offers, unconfirmed selections, strategy provenance and context snapshots.
- `scout.log` and `scout.previous.log`: errors, rotated at approximately 2 MB. The log is created only when something is logged.
- `diagnostic-*.pgm`: opt-in snapshots for local calibration.
- `calibration.json` and `strategy-pack.json`: optional local inputs you install.

No screenshots are embedded in SQLite; only frame hashes are recorded. Game files are never opened. SQLite may create journal files alongside its database during transactions. Database history is local and currently has no automatic pruning; quit Scout before backing it up or deleting it.

## Update or uninstall

Quit Scout first. For an update, extract a new trusted artifact into a new application folder, then launch its EXE. Your local data remains under LocalAppData. Keep a backup of `scout.db` before upgrades; a newer database version is rejected by an older app. Strategy updates are manual: replace the local pack/profile while Scout is closed and set matching patch context. There are no automatic online updates.

To uninstall, quit Scout and delete its extracted application folder. To erase history/configuration too, delete `%LOCALAPPDATA%\Sts2Scout`. Do not delete or modify Steam/game files. Scout creates no service, startup task, registry installer entry, or game mod.

# Verification record and Windows smoke checklist

## Verified on this development machine

Platform: macOS arm64, .NET SDK 10.0.400 / runtime 10.0.11. Windows-specific source was cross-compiled, not executed. The machine initially had only .NET 7; the supported SDK and NuGet packages were installed under temporary directories. Sandbox restrictions required approved access for SDK downloads and local MSBuild/Roslyn/VSTest communication pipes/sockets. Those development tool sockets are not application networking.

Commands use `dotnet` below; the actual local executable was `/private/tmp/sts2-dotnet/dotnet`, with restored packages under `/private/tmp/sts2-nuget`:

| Check | Command | Result |
| --- | --- | --- |
| Full Release build / compiler analyzers | `dotnet build Sts2Scout.sln -c Release --no-restore -m:1 -nodeReuse:false` | Passed; all five projects, zero warnings/errors |
| Formatting | `dotnet format Sts2Scout.sln --verify-no-changes --no-restore` | Passed |
| Unit, fixture and SQLite integration tests | `dotnet test tests/Scout.Tests/Scout.Tests.csproj -c Release --no-build --no-restore --logger "console;verbosity=normal"` | 21 passed |
| Development CLI integration | `python scripts/smoke_seeder.py /private/tmp/sts2-dotnet/dotnet` | Passed prepare, allowlist denial, ingest, review gate, compile, overwrite denial, calibration |
| Runtime boundary guard | `python scripts/check_boundaries.py` | Passed: no runtime HTTP API/endpoint, development project dependency, game-memory/input API, or unscoped file-write API |
| NuGet advisory audit | `dotnet list src/Scout.Windows/Scout.Windows.csproj package --vulnerable --include-transitive --no-restore` | No known vulnerable packages in the configured NuGet source after SQLite bundle update |
| Self-contained cross-publish | `dotnet publish src/Scout.Windows/Scout.Windows.csproj -c Release -r win-x64 --self-contained true -o artifacts/Sts2Scout-win-x64` | Windows x64 executable and runtime generated locally |

The initially resolved SQLitePCLRaw.lib.e_sqlite3 2.1.11 had a [known native SQLite advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q). The final project explicitly selects SQLitePCLRaw.bundle_e_sqlite3 3.0.5, which resolves native SQLite 3.53.4, and enables transitive restore-time auditing. Database tests passed with the replacement. This reports the current advisory scan, not a guarantee of absence of vulnerabilities.

The source formatter is run with `dotnet format Sts2Scout.sln --verify-no-changes --no-restore`. Compiler analyzers are enabled with warnings treated as errors. The CI executes these same checks on Windows, plus `scripts/package.ps1` to stage docs, license notices and an executable artifact.

## Synthetic recognition metrics

Five checked-in 96×54 PGM images: reward, merchant, two small noisy variants, and one blank unknown. The first two also supply calibration patterns, so the set is deliberately not an independent validation dataset. Screen classification: true positives 4, false positives 0, false negatives 0; precision 100%, recall 100%, overall unknown rate 20% (the blank negative). Exact fixture entity/price assertions also pass, including card, relic and price labels. Additional generated tests check 192×108 scaling, aspect mismatch, ambiguous entity templates, low-confidence prices, and unconfirmed visual selection signals. Potion/service entities use the same mechanism but have no dedicated pixel fixtures.

**No representative STS2 screenshots were available.** Real-game precision, recall, latency, CPU use and unknown rate are unmeasured. The fixtures cover no real patches, localizations, accessibility modes, character sets, mouseover states or merchant layouts. Match scores are not calibrated probability estimates.

## Windows checks still required

These cannot be claimed from a Mac cross-build. Use a clean Windows 11 x64 machine and the packaged artifact; record OS/build, STS2 patch, GPU, resolution, scaling, localization and Steam mode. No GitHub workflow was run or artifact uploaded during this implementation; `Windows build` runs when the work is committed/pushed or manually dispatched by an authorized maintainer.

| Action | Expected result | Troubleshooting |
| --- | --- | --- |
| Extract the ZIP on a machine without .NET installed; run Sts2Scout.exe | Waiting panel appears; settings and SQLite database appear only in `%LOCALAPPDATA%\\Sts2Scout` | Keep all extracted files; inspect SmartScreen origin/commit; use x64 Windows |
| Start Scout before Steam/game, then reverse the order | Same process/window detection in both orders | Check Task Manager executable basename against `processName`; omit `.exe` |
| Focus STS2 in borderless/windowed mode | Scout follows client origin; calibrated pipeline polls | If blank/unavailable, disable HDR/filters/other overlays and check normal desktop composition; if the exclusion warning appears, inspect the log and captured frame |
| Move game between monitors at 100%, 150%, 200% scaling, including a monitor with negative coordinates | Overlay remains at configured client offsets | Adjust offsets; record DPI/monitor layout if drift occurs; mixed-DPI behavior needs validation |
| Toggle Ctrl+Shift+S; click Scout in each mode | Interactive buttons work only in interaction mode; mouse passes through otherwise; no game input is synthesized | Hotkey conflict displays a message and leaves Scout interactive; configure another key |
| Minimize/Alt+Tab away; close/restart game | Capture/rankings pause or clear; no prior-room decision is joined to the new process | Check focus and process-name settings |
| Leave diagnostics disabled through several screens | No `diagnostic-*.pgm` files appear | Only enable the flag and explicit save button when desired |
| Enable diagnostics; capture while Scout overlays the client | Saved PGM shows client pixels without Scout | If the fallback warning is visible, the frame may contain Scout pixels; do not use a contaminated frame for calibration and record the logged Win32 error and Windows/GPU conditions |
| Install local reviewed pack/profile; show stable reward and merchant screens | Three matching frames yield offers/rankings; unknown IDs and prices remain null | Verify patch strings, aspect, regions, supported price alternatives and manual run context |
| Hover/change screens rapidly or select a merchant offer | Stale recommendations clear; any selection hypothesis stays unconfirmed | Recalibrate false matches; never interpret hover/disappearance as proof |
| Disconnect networking / inspect with your preferred Windows network monitor | App works as before; no runtime requests | .NET developer tooling is not part of the shipped process |
| Observe Scout with Process Monitor | Writes are confined to its LocalAppData folder; no game-file opens/writes | Runtime implementation has no game-file reader; report any contrary behavior |
| Quit, back up database, replace app folder, relaunch; uninstall | History survives update; deleting app/data removes Scout | Quit before modifying the database or binary directory |

## Limits of automated checks

The boundary script is an architectural source/project guard, not a formal proof of native behavior or a firewall. Self-contained .NET includes framework assemblies with networking capabilities, but Scout has no HTTP call sites or endpoints and references no HTTP package. Game sentinel and path traversal tests prove the scoped write API rejects game paths; a symlink-write test runs on macOS and skips on Windows where link creation may require privileges. Native capture, click-through, exclusion, hotkeys, and DPI remain manual Windows checks.

# Architecture and decisions

- `Scout.Core` (.NET 10): immutable data contracts, strict JSON, validated packs, normalized grayscale template pipeline, stability filter, decision hypotheses, deterministic heuristic engine, and Scout-owned write paths. No Windows or HTTP dependency.
- `Scout.Storage`: Microsoft.Data.Sqlite, embedded transactional migration 001, parameterized writes and per-process session IDs. Foreign keys connect observations, choices and hypotheses. Provenance stores exact pack JSON; collisions under the same version are rejected. The schema deliberately forbids confirmed inferred selections.
- `Scout.Windows`: WPF borderless topmost window with an opaque outer background; Win32 process/window metadata and foreground client capture; global hotkey toggles WS_EX_TRANSPARENT/WS_EX_NOACTIVATE. Background capture avoids blocking the WPF dispatcher. A single in-flight poll bounds resource usage. Device coordinates are converted to WPF DIPs for position.
- `Scout.Seeder`: separate development executable using allowlisted saved sources and imported AI candidates, content-addressed raw evidence, quarantine and hash-bound human review. It never ships.
- `Scout.Tests`: cross-platform tests and original synthetic PGM/JSON fixtures. Windows CI cross-checks compilation, native SQLite integration and packaging.

## Capture choice and limits

The MVP uses a cropped desktop BitBlt and requests WDA_EXCLUDEFROMCAPTURE for Scout. Capture is gated to the foreground game and its verified process ID and requires a normal composed desktop. If display affinity succeeds, supported OS capture mechanisms exclude Scout. If it fails, capture continues and may include Scout or other topmost applications over the client rectangle; keep these closed. It is not a rendering hook. It does not open process memory or game files. Capture success is not proof the frame contains usable game pixels: blank/unknown frames remain unknown. Exclusive fullscreen and protected graphics can fail.

Microsoft documents that [WDA_EXCLUDEFROMCAPTURE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity) requires Windows 10 2004+ and DWM and excludes the window from supported OS capture mechanisms. Scout uses a non-layered, opaque WPF window because display affinity can fail for the layered window created by `AllowsTransparency`. Exclusion remains best effort: if Windows rejects it, Scout logs the Win32 error and continues capture with a persistent warning that overlapping Scout pixels may be present. This is not a universal content-protection guarantee; verify the actual capture on Windows. [PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow) asks the target to render synchronously and can block, so it is not the fallback. A future Windows Graphics Capture adapter can replace capture without changing core contracts. These documents were inspected during implementation; no game data was ingested from them.

## Recognition and temporal behavior

Probe regions are normalized; templates compare 32×16 grayscale samples with a configurable minimum threshold (never below .9) and ambiguity margin. One and only one screen probe may match. Entity/price/upgrade/selection probes are screen/slot-specific. Price recognition is exact whole-price template matching, not general digit OCR. The three-frame filter de-duplicates stable observations. Unknowns, process changes and focus loss reset decision history to avoid joining unrelated rooms. A vanished merchant slot is only a .5 confidence hypothesis; a reward transition never identifies a choice. No deck tracking is inferred from ambiguous selections.

Recognition values are match scores, not calibrated probabilities. Unknown entity/price fields are null. Persistence rechecks confidence and IDs before accepting any recognized fact. Logs omit frames; captures exist only in memory unless the diagnostic button is explicitly enabled and used. Context is manually supplied and stored with each observation so results can be reproduced.

## Recommendations

Scores combine local baseline, declared deck needs scaled by act, recognized upgrades, copy redundancy, dilution, setup risk scaled by ascension, applicable directed claims, price and reserve-gold opportunity cost. Claims iterate in ordinal ID order; final ties use entity ID then slot. Reasons include each contribution and source/claim attribution. Correlation is explicitly marked association-only. Unknown/unaffordable merchant choices and patch mismatches yield no ranking. These are hand-authored heuristics, not a learned optimal policy. There is no automatic card-removal, purchase ordering or full merchant budget optimization.

## Extension contracts

Implement `IScreenRecognizer` for OCR/new telemetry; emit null unknown IDs and retain confidence and profile version. Add screen enum values only with fixtures, persistence compatibility and a UI policy. Capture adapters supply `GrayFrame`; their writes must remain behind `ScoutPaths`. Additional read-only save/log telemetry should use a separate capability exposing only read streams, with write-denial tests before integration; current code opens no game files at all. New database migrations increment `user_version` transactionally and must test upgrade/reopen/forward-version rejection. New evidence types/patch rules require validator and schema changes together. AI providers belong only in `tools`, never under the runtime dependency graph.

No services, cloud components, updater, registry installation, code injection, game input or mod boundary exceptions are introduced.

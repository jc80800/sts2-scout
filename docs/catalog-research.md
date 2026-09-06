# Catalog research and refresh

Snapshot retrieved 2026-09-06. The normalized artifact contains 577 entries, complete relative to the stable Spire Codex API count at retrieval. Stable IDs are `sts2.` plus lowercase upstream IDs, preserving distinct character-specific starting cards. Character, type, rarity, cost, structured numeric mechanics, keyword tags, upgraded deltas/tags, uncertainty, URLs, retrieval date, patch, and raw-response hash are retained. Game art, screenshots, raw responses, copied card descriptions, and upstream source code are not committed.

Sources:

- [Spire Codex stable cards API](https://spire-codex.com/api/cards): structured facts; each entry links its detail endpoint.
- [Stable catalog counts](https://spire-codex.com/api/stats) and [changelog](https://spire-codex.com/api/changelogs): completeness and patch evidence. Site version **1.2.0 is not a game patch**; its title identifies game **v0.107.1**, build 23811903. The empty `/api/versions` response was not treated as evidence. Beta guide dates are not stable-data versions.
- [Mega Crit's Major Update #2 announcement](https://store.steampowered.com/news/app/2868840/view/710026912607505280): corroborates the v0.107.1 update.
- [Ironclad community guide](https://spire-codex.com/api/guides/ironclad-tier-list): six low-weight directional associations. The guide mixes population brackets and omits per-card sample sizes; no percentages are imported, confidence is 0.35, and no causal recommendation is inferred. Its explicit co-op example is excluded. Other cards retain neutral baselines, not invented expert ratings.
- [Vicious statistics](https://spire2stats.com/cards/vicious/): independently agrees on conditional Vulnerable-triggered draw; the visible 253-run sample mixes older patches, so those statistics are **not** applied to this patch's scores.
- [API terms](https://github.com/ptrlrd/spire-codex/blob/main/API_TERMS.md): community API access and attribution. The upstream software's noncommercial license is not imported into Scout. Only normalized facts and original short summaries are included; Mega Crit owns the game IP. See `data/source-manifest.json` for the retention/normalization decision and exact hashes.

Mechanical tags are conservative extraction heuristics, not exhaustively reviewed rule semantics. Vicious is a Vulnerable **payoff**, not a Vulnerable source; its draw requires the trigger. Numeric variable fields and upgrade deltas remain available even when timing cannot be normalized safely. All entries explicitly warn about dynamic/conditional effects. Complete API coverage is not a claim that every complex card has a complete executable model.

## Repeatable workflow

With Python 3.11+ and network access, run outside the desktop runtime:

```powershell
python scripts/refresh_catalog.py --download C:\scout-research\raw --retrieved-at 2026-09-06T06:55:00Z --output C:\scout-research\candidate
# Use the actual retrieval timestamp on a new fetch, and a new output directory.
dotnet run --project tools/Scout.Seeder -- validate C:\scout-research\candidate\strategy-pack.json
```

The script downloads four public endpoints once, checks the upstream card count, duplicates and patch evidence, and creates a **candidate**, refusing an existing output directory. It refuses a new patch until extraction/strategy rules are reviewed and its explicit patch guard is updated. `--offline RAW_DIRECTORY` reproduces the candidate from saved `cards.json`, `stats.json`, `changelogs.json`, and `strategy.json`, with the original retrieval timestamp. Keep raw sources private/outside the repository. Review the diff, source permissions, uncertainty, counts and strategy claims; normalization is not automatic research approval. Existing `prepare/ingest/compile` commands remain available for richer reviewed strategy claims.

Before installation, validate both published JSON schemas and semantic constraints:

```powershell
python -m pip install jsonschema
python scripts/validate_data.py C:\scout-research\candidate\strategy-pack.json
dotnet run --project tools/Scout.Seeder -- install C:\scout-research\candidate\strategy-pack.json "$env:LOCALAPPDATA" 0.107.1
```

Quit Scout before installing. The runtime validates strict JSON plus semantic constraints too: versions, count, IDs, metadata, provenance and claims. Installation atomically replaces only Scout's own file after validation, retaining the prior valid pack. At startup a malformed/incompatible local file falls back to `strategy-pack.last-valid.json`, then the bundled valid artifact; status reports rejection and cached age. A bundled patch mismatch can load the app/deck editor but never yields a recommendation. No automatic web research happens in the runtime.

## Optional remote manifest boundary

No production service or credentials are required. A maintainer may host the schema in `schemas/update-manifest.schema.json` with schemaVersion, gameVersion, packVersion, HTTPS url and SHA-256 of the pack. Then explicitly run:

```powershell
dotnet run --project tools/Scout.Seeder -- fetch https://YOUR-TRUSTED-HOST/manifest.json "$env:LOCALAPPDATA" 0.107.1
```

Only this separate update tool makes HTTP requests. Downloads have time/size limits, redirects are disabled by the CLI, version and hash must agree, and malformed/incomplete packs are rejected before installation. Failure exits nonzero and leaves the cache untouched. A hash is integrity evidence, **not** an authenticity signature: trust the manifest host. The `HttpClient` boundary is tested with a mock HTTP transport; plain HTTP is restricted to loopback tests. The desktop continues using its existing cache without internet.

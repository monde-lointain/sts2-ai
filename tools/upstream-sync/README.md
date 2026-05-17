# tools/upstream-sync

Manual-trigger tool to detect new Slay the Spire 2 Steam patches, re-extract the upstream source via GDRE, version the tree by Git tag, and emit a structured port-decision document for the Q1 lead to review.

## Install

```
.venv/bin/pip install -e tools/upstream-sync
```

## Workflow

```
$ make sync-check
Steam buildid: 22823976 (last synced: 22000000 / v0.103.2)
Status: NEW PATCH DETECTED
Patch notes since last sync: 2 items
  - 2026-05-08: "Beta Hotfix Notes - v0.105.1"
  - 2026-04-30: "Beta Patch Notes - v0.105.0"
Next: `make sync SYNC_ARGS="--version v0.105.1"`

$ make sync SYNC_ARGS="--version v0.105.1"
[1/5] Steam: buildid 22823976, installdir 'Slay the Spire 2'
[2/5] Extracting via GDRE -> /tmp/sts2-extract-XXXX (~ 1 min)
[3/5] rsync mirror -> ~/development/projects/godot/sts2/
[4/5] Committing + tagging v0.105.1 (upstream tree)
[5/5] Generating port-decision doc -> engine/headless/docs/specs/03-v0.103.2-to-v0.105.1-port-decisions.md
Done. Review the port-decision doc; manual commits to Q1 / Q4 follow.
```

## Human review workflow

1. Run `make sync-check` to see if there's a new patch
2. Run `make sync --version vX.Y.Z` (look up version in Steam News or the game UI)
3. Review the auto-generated `engine/headless/docs/specs/0N-vA.B.C-to-vX.Y.Z-port-decisions.md`:
   - Edit per-row decisions if heuristics misclassified (the doc is markdown; edit freely)
   - Commit the doc to monorepo (`git commit docs/specs/0N-...md`)
4. Open per-row tasks:
   - **PORT** items -> Q1 sub-stages
   - **DELETE** items -> Q1-ADR-004 T3 ledger entries
   - **DEFER** items -> tracked against the unblock trigger named in the row
   - **IGNORE / SURFACE-NO-ACTION** -> no action

## Synthetic version fallback

If you don't know the human-readable version yet, use `--version-from-buildid`:

```
make sync SYNC_ARGS="--version-from-buildid"   # produces e.g. build-22823976-2026-05-14 tag
```

You can re-tag later when the human version is known (just `git tag v0.105.1 <commit>` in the upstream tree).

## Configuration

Defaults work for the canonical machine setup. Override via env or flag:

| Var / Flag | Default | Purpose |
|---|---|---|
| `STEAM_HOME` / `--steam-home` | `~/snap/steam/common/.local/share/Steam` | Steam install root |
| `GDRE_BIN` / `--gdre-bin` | `~/applications/GDRE_tools-v2.5.0-beta.5-linux/gdre_tools.x86_64` | GDRE binary |
| `UPSTREAM_TREE` / `--upstream-tree` | `~/development/projects/godot/sts2` | Decompiled tree |

Alternate Steam install locations:
- Flatpak: `~/.var/app/com.valvesoftware.Steam/data/Steam`
- Native Linux: `~/.local/share/Steam`
- macOS: `~/Library/Application Support/Steam`

## Cross-references

- ADR-003 (Q4 patch-adaptation lever)
- Q1-ADR-004 (T3 ledger discipline)
- `engine/headless/docs/specs/02-encounter-port-decisions.md` (the de-facto template our output emulates)

## State schema versions

`.upstream-sync-state.json` is versioned via the `schema_version` field.

| Version | Fields | Introduced |
|---|---|---|
| v0 | last_synced_at, last_synced_buildid, last_synced_version, tool_version, upstream_tree_path | 0.1.0 (initial) |
| v1 | + last_synced_dll_sha256, gdre_version, schema_version | Wave 4 / Stream A.0 |

Legacy v0 state files are auto-promoted to v1 on next write (new fields populated as null until next `make sync`).

## Port-decision sidecar schema versions

The port-decision JSON sidecar (`engine/headless/docs/specs/0N-*-port-decisions.json`) is versioned via the `schema_version` field.

| Version | Changes | Introduced |
|---|---|---|
| v1 | Initial schema: rows with path, bucket, git_status, decision, status, patch_notes_hint (plain string), rationale | Wave 5 / Stream A.2 |
| v2 | `patch_notes_hint` promoted to structured dict (`change_type`, `magnitude`, `version`, `excerpt`, `gid`, `claim_only_candidate`); added `wave` and `stream_id` per-row fields | Wave 10.5 / Stream 10.5.α |

**Backward-compat read**: if `wave`, `stream_id`, or `claim_only_candidate` fields are absent in a v1 sidecar, treat as `null`. If `patch_notes_hint` is a string (v1 format), coerce to `{"excerpt": value, "gid": null, ...}` or ignore on `str` type.

## Game-content allowlist (ModelDb introspection)

The correlator optionally filters `[b]Entity[/b]` BBCode extractions to known game-content class names. The allowlist is derived from the upstream `sts2.dll` assembly via `content_allowlist.py`:

- **Strategy (Option C — heuristic)**: scans the DLL's metadata string heap for CamelCase names ending in known content suffixes (`Power`, `Model`, `Card`, `Relic`, `Encounter`, `Potion`, `Enchantment`, `Affliction`, `Badge`). No .NET CLR runtime required.
- **Cache**: `tools/upstream-sync/cache/content-allowlist-<sha7>.json` keyed by `pinned_dll_sha256` from `engine/headless/upstream-pin.json`. On cache hit, the DLL scan is skipped.
- **CI safety**: if the DLL is absent (CI runners), returns an empty frozenset — the correlator treats this as "disabled" (no filtering).

```python
from upstream_sync.content_allowlist import build_allowlist
from pathlib import Path

allowlist = build_allowlist(
    pin_json_path=Path("engine/headless/upstream-pin.json"),
    cache_dir=Path("tools/upstream-sync/cache"),
)
# Pass to correlate:
from upstream_sync.correlate import correlate
corr = correlate(diff_report, patch_notes, content_allowlist=allowlist)
```

## Change-type classifier

`change_type.py` provides heuristic classification of patch-note excerpt lines:

```python
from upstream_sync.change_type import classify_change_type

ct, mag = classify_change_type("Buffed Untouchable card: upgraded Block gain increased From +2 -> +3")
# ct = "buffed", mag = "+2→+3"

ct, mag = classify_change_type("Fixed Aeonglass icon/intent display")
# ct = "fixed", mag = None
```

`change_type` values: `"buffed" | "nerfed" | "added" | "removed" | "fixed" | "changed" | "reworked" | "unclassified"`.

**Accuracy caveat**: this is heuristic keyword-matching, not NLP. Estimated accuracy on the v0.105.x corpus is ≥75% for change-type classification, ≥85% for magnitude extraction when a numeric range is present. If accuracy drops below 70% on your corpus, flag in the engineer report.

## Patch-notes hint block in engineer-dispatch prompts

Every prompt template (`monster.j2`, `generic-port.j2`) now renders a `## ⚠ Patch-notes hint` block **at the top of the per-row context section**, before the upstream diff. When a hint is available (structured dict from the correlator), it surfaces:

- **Change type** (buffed/nerfed/added/...) with magnitude
- **Version** (patch note title)
- **Excerpt** (BBCode-stripped context around the entity mention)
- **Source** (Steam gid)

When no hint is available, the block renders a "No patch-notes hint surfaced" placeholder so engineers know the block was checked. The `REQUIRED` cross-check instruction prevents engineers from skipping a row just because the diff looks like a Godot threading change.

## PATCH-NOTES-CLAIM-ONLY tracking

`patch_notes_hint.claim_only_candidate: bool` flags rows where the correlator surfaced a hint but no upstream `.cs` code change was detected (e.g. behavior changes visible only in integration tests or game logic not yet decompiled). The 10.5.β audit filters these rows for the PATCH-NOTES-CLAIM-ONLY category.

## update-port-decision-status.sh extension

The `--wave` and `--stream` flags have been added to both single-row and batch modes:

```bash
# Single-row: flip to MERGED and stamp wave/stream
.claude/scripts/update-port-decision-status.sh v0.105.1 src/Core/Models/Cards/Untouchable.cs MERGED --wave 10.5 --stream "10.5.α"

# Batch: flip all matched rows from wave branch to MERGED with wave/stream stamp
.claude/scripts/update-port-decision-status.sh --batch worktree-agent-abc123 --wave 10.5 --stream "10.5.α"
```

Wave and stream_id fields are written as JSON values into the matching row(s) in the sidecar. Existing rows without these fields (v1 sidecars) will have them added on first write.

## Pipeline auto-trigger

Three trigger surfaces are supported (ADR-026):

### 1. Local crontab (Steam-aware)

Add to user crontab on the machine with Steam installed:

```cron
# Check for new STS2 upstream patch every 6 hours
0 */6 * * * cd /path/to/sts2-ai && make sync-check >> ~/.cache/sts2-sync-check.log 2>&1
```

`make sync-check` polls Steam buildid via `.upstream-sync-state.json` and emits a structured delta report. If a new buildid is detected, it logs `NEW PATCH DETECTED` — user reviews and runs `make sync --version vX.Y.Z` manually. **Recommendation:** disable Steam auto-update for STS2 during bridge phases to prevent mid-bridge drift.

### 2. GHA state-only cron (Steam-unaware)

A GitHub Actions workflow (`.github/workflows/upstream-check.yml`) runs on a schedule and checks whether `.upstream-sync-state.json:last_synced_buildid` in the repo diverges from the known-pinned buildid. Because GHA runners have no Steam access, this cannot fetch new Steam artifacts — it signals that a local sync is needed and opens an issue or posts a workflow annotation. The cron schedule is defined in the workflow file.

### 3. Manual trigger

```bash
make sync-check   # detect only; no writes
make sync SYNC_ARGS="--version vX.Y.Z"   # full sync: extract + rsync + commit + port-decision doc
```

See Workflow section above for the full manual flow.

## Prompt generation

After `make sync`, the pipeline generates engineer-dispatch prompts via `prompt_generator.py`:

```bash
# Emit a quantum-lead briefing prompt to stdout (paste into Claude session)
.venv/bin/python -m upstream_sync.cli dispatch-quantum-lead --version vX.Y.Z

# Generate a per-row engineer prompt for a specific row
.venv/bin/python -m upstream_sync.cli generate-prompt --version vX.Y.Z --row-path src/Monsters/Slime.cs
```

`dispatch-quantum-lead` does NOT spawn a subagent. It emits a structured briefing to stdout — port-decision summary, JSON sidecar excerpts, B.0 policy decision placeholder, recommended bridge structure scaffold. User pastes the output into a Claude session and invokes the quantum-lead subagent from there.

### JSON sidecar

`make sync` emits a JSON sidecar alongside the port-decision markdown:

```
engine/headless/docs/specs/0N-vA.B.C-to-vX.Y.Z-port-decisions.json
```

Each row in the sidecar has a `path` (upstream source path) and `status` field:

| Status | Meaning |
|---|---|
| `PENDING` | Awaiting dispatch to engineer subagent |
| `DISPATCHED` | Wave in flight |
| `MERGED` | Wave merged, gate green |
| `DEFERRED` | Explicitly deferred to Phase-2+ |
| `NO_ACTION_NEEDED` | SURFACE-NO-ACTION or IGNORE rows — terminal at generation time |

`NO_ACTION_NEEDED` rows are never dispatched and never auto-flipped. `/wave-close` bumps `PORT`/`DELETE` rows to `MERGED` via `.claude/scripts/update-port-decision-status.sh` (soft-fail).

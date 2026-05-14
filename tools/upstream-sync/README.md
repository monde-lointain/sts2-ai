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

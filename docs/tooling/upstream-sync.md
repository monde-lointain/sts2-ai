# Upstream sync tooling

`tools/upstream-sync/` automates the patch-detect -> extract -> diff -> port-decision pipeline for keeping our codebase synced with Megacrit's STS2 source.

Tool README: [`tools/upstream-sync/README.md`](../../tools/upstream-sync/README.md)

## Cross-references

- **ADR-003** (Token Registry as Patch-Adaptation Lever) -- Q4 stability rules apply to the advisory section the tool emits
- **Q1-ADR-004** (Mod-Layer Discipline) -- T3 ledger entries originate from this tool's DELETE decisions
- **engine/headless/docs/specs/02-encounter-port-decisions.md** -- the manual precedent the tool's output emulates

## Operational contract

The tool is **manual-trigger** (`make sync-check`, `make sync`). It never auto-commits to Q1 or Q4. Output is a port-decision markdown document; humans review and act.

The decompiled upstream tree at `~/development/projects/godot/sts2/` is version-tracked via git tags (`v0.103.2`, `v0.105.1`, etc.). The tool itself sits in `tools/upstream-sync/`; state file lives at `<monorepo>/.upstream-sync-state.json` (gitignored).

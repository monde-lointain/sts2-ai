# Q1 Determinism Probe

External CI tool that validates Q1's per-step deterministic output against
golden traces. See `docs/specs/00-system-overview.md` § Determinism Probe and
`docs/plans/q1-implementation-plan.md` § S13.

## Approach

**Current mode: Approach B (Q1 self-consistency goldens).** Captured-once-then-compared
goldens detect ANY Q1 regression (per-step hash drift = bug), validate the
determinism contract (same seed + script → same hashes byte-exact), and
assert structural integrity for all 22 encounters.

**Approach A (live upstream Godot comparison) — scaffolding committed:**
- Godot 4.5.1-mono was installed during S13. `/home/clydew372/applications/godot/Godot_v4.5.1-stable_mono_linux_x86_64/godot` is on PATH.
- Upstream-side driver script: `~/development/projects/godot/sts2/q1_probe_driver.gd` (additive, does not modify any existing upstream scene/script). Boots via
  `godot --headless --path ~/development/projects/godot/sts2/ --script res://q1_probe_driver.gd`.
- Upstream's `sts2.csproj` builds clean (0 errors) with .NET 9.0.116 + `rollForward: latestMajor` (workaround: build from /tmp with a local global.json since SDK 9.0.303 is not installed). The `sts2.dll` from the Steam install at
  `/home/clydew372/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/` is also available for linking.
- Blocker before per-step live capture is achievable: upstream's `CombatManager.StartCombatInternal` references ~12 Godot.SceneTree-coupled singletons (`NRunMusicController`, `NCombatRoom`, `NModalContainer`, `NCombatStartBanner`, `NCombatRulesFtue`, `Cmd.CustomScaledWait`, `SaveManager`, `RunManager.Instance.ActionExecutor`, `NetCombatCardDb`, etc.); driving them headlessly requires either (a) the full game scene tree (`scenes/game.tscn`) to mount — which currently fails on `.uid` resolution because the decompiled tree was not built by the editor's "Build C# project" step — or (b) writing stubs for all twelve coupled singletons inside a console host that links `sts2.dll` directly. Both paths are 1-2 week efforts and outscope a single probe-stage. The cleaner deterministic surface is `CombatManager.SetUpCombat` + `Player.PopulateCombatState`, which run RNG-driven shuffles with no scene-tree deps; a future Phase-1.5 follow-up can wire those alone to capture initial-state goldens from upstream while leaving per-step under the self-consistency regime.

The probe's comparison logic does not change when upstream-derived goldens
become available; only the goldens directory's contents change.

## Layout

- `corpus/phase1-corpus.json` — corpus of (seed, encounter, script) tuples.
- `goldens/<seed>-<encounter>-<scriptHash>.jsonl` — per-step probe-out files
  captured once and committed.
- `src/Program.cs` — probe driver / comparator.
- `src/CorpusGenerator.cs` — generates `phase1-corpus.json` deterministically.

## Modes

| Mode | Invocation | What it does | Budget |
|---|---|---|---|
| Quick | `make probe-quick` | 5-seed smoke per-step + structural-all-22 | <60s |
| Full  | `make probe-full`  | Full corpus (50-seed smoke per-step + 10 seeds × 22 initial-state + structural) | ~5min |
| Structural | `--mode structural` | All 22 encounters StartCombat only | <30s |
| Per-step | `--mode per-step` | Smoke encounter scripted-action per-step | varies |
| Capture | `--mode capture` | Regenerate goldens (use only when Q1 changes are intentional) | ~5min |

## Usage

```bash
# Full probe run (scheduled / pre-merge)
dotnet run --project test/determinism-probe -- --mode full

# Quick smoke check (pre-commit safe)
dotnet run --project test/determinism-probe -- --mode quick

# Regenerate goldens after an intentional Q1 change
dotnet run --project test/determinism-probe -- --mode capture
```

## Exit codes

- `0` — PASS (every step's hash matches its golden).
- `1` — FAIL (at least one divergence; first divergence reported on stderr).
- `2` — ERROR (corpus missing, golden missing, infrastructure failure).

# Sts2Q1ConsoleHost

Foundation for the **Phase-1.5 per-step Godot probe** (`R9` discharge path).

## What this is

`Sts2Q1ConsoleHost` is a plain .NET 9 console process that loads the upstream
`sts2.dll` (Steam install) via reflection + `AssemblyLoadContext`, with
runtime dependencies resolved from the same directory. It exposes a stub
interface (`ISceneTreeSingletons`) for the 12 SceneTree-coupled singletons
that `CombatManager.StartCombatInternal` references.

**Console-only.** The host MUST NOT mount a Godot scene tree
(`scenes/game.tscn`). That path is documented blocked in
`engine/headless/docs/phase1-gate-report.md` (Approach A, `.uid` resolution
failure). This host bypasses the scene tree entirely and stubs the
singletons in process.

## Scope by sub-stream

This project is constructed in four sub-streams of `P-1.5-1`:

| Sub-stream | Scope                                                                    | Status     |
|------------|--------------------------------------------------------------------------|------------|
| **α**      | Project skeleton, CLI scaffold, composition root, `ISceneTreeSingletons` interface, `upstream_bound` sentinel, smoke test. | this branch |
| **β**      | Fill in the 12 singleton stub bodies (NRunMusicController, NCombatRoom, …). | future      |
| **γ**      | `Pinned<TStub>` decorator harness — records call counts + arg-shape hashes per stub for stub-pin regression. | future      |
| **δ**      | Per-step probe driver — drives `StartCombatInternal` per step, emits the JSONL stream to `--out`. | future      |

α is the load-bearing piece: it owns the project geometry and the
`upstream_bound` contract everything else hangs off.

## CLI

```text
Sts2Q1ConsoleHost --sts2-dll <path> --seed <uint> --encounter <id> --out <path>
```

| Flag           | Meaning                                                              |
|----------------|----------------------------------------------------------------------|
| `--sts2-dll`   | Explicit path to upstream `sts2.dll`. **No auto-discovery in α.**     |
| `--seed`       | RNG seed (`uint`).                                                   |
| `--encounter`  | Phase-1 corpus encounter id (e.g. `CultistsNormal`).                  |
| `--out`        | Output JSONL stream path. α creates / truncates; δ writes per step. |

## Exit codes

| Code | Meaning                                                                      |
|------|------------------------------------------------------------------------------|
| 0    | Success — `upstream_bound` sentinel emitted on stdout.                       |
| 1    | CLI usage error.                                                             |
| 2    | Upstream-load or type-resolution error (assembly load + reflect).            |

## `upstream_bound` sentinel

On a successful α run, stdout contains exactly one JSON line:

```json
{"event":"upstream_bound","sts2_dll":"…","assembly_name":"sts2","assembly_version":"…","combat_manager_type":"MegaCrit.Sts2.Core.Combat.CombatManager","player_type":"MegaCrit.Sts2.Core.Entities.Players.Player"}
```

The smoke test (`ConsoleHostSmokeTests.Sts2Q1ConsoleHost_loads_upstream_sts2_dll`)
asserts this line exists.

## Local invocation

```bash
make consolehost-smoke \
  STS2_DLL="$HOME/snap/steam/common/.local/share/Steam/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/sts2.dll"
```

The default `STS2_DLL` in the `Makefile` already points at the canonical
Steam install location on the Q1 build host.

## What this is NOT

- **Not a Godot host.** No scene tree, no autoload, no `.tscn`.
- **Not the probe driver.** δ wires the per-step JSONL output.
- **Not a stub-body project.** β fills `ISceneTreeSingletons`.
- **Not on the `make ci` test path.** The console host is exercised by the
  `ConsoleHostSmokeTests` smoke test, which auto-skips when the Steam
  install is absent (worktree builds without Steam stay green).

# sts2-ai

AlphaZero-style training pipeline for Slay the Spire 2. Twelve services
(quanta Q1–Q12) covering simulation, oracle verification, experience
storage, model training, inference, evaluation, and observability. Phase 1
ships a deterministic combat-only engine + bit-identical state codec +
content registry; Phases 2–5 add full-run policy, ascension ladder, and
curriculum-driven training.

Strategy: **`docs/scaling-strategy.md`** (the load-bearing design doc).

## Repo layout

| Path | Owner / role |
|---|---|
| `engine/headless/` | Q1 — Game Simulator (C#, .NET 9). Deterministic hook-protocol entry point; M1 state codec; M2 IPC. |
| `engine/cpp/` | Q2 — Oracle Verifier (C++20). Expectimax solver; `CompactState`; agreement-row writer; playable single-fight demo. |
| `contracts/` | Cross-quantum schemas (`schemas/*.proto`) + content registry (`registry/phase1-silent.json`). Versioned per ADR-001. |
| `pipeline/` | Q3 (experience-store), Q5/Q7/Q8/Q9/Q10/Q11/Q12 services. Python 3.12, `pipeline/common/` shared. |
| `docs/specs/` | Module specs (one per quantum) + ADR log (`01-decisions-log.md`). |
| `docs/` | Strategy doc, design notes, audits. |
| `.claude/` | Project workflow (subagents, skills, slash commands, hooks). See `.claude/CLAUDE.md`. |
| `tools/` | Content seeder, AST analyzer, upstream-sync tooling. |

## Quantum map

| Q | Name | Substrate |
|---|---|---|
| Q1 | Game Simulator | `engine/headless/` |
| Q2 | Oracle Verifier | `engine/cpp/` |
| Q3 | Experience Store | `pipeline/experience-store/` |
| Q4 | Content Registry | `contracts/registry/` |
| Q5 | Model Registry | `pipeline/model-registry/` |
| Q6 | Evaluation Reports | output of Q12 |
| Q7 | Observability | `pipeline/observability/` |
| Q8 | Rollout Workers | `pipeline/rollout-workers/` |
| Q9 | Inference Server | `pipeline/inference-server/` |
| Q10 | Trainer | `pipeline/trainer/` |
| Q11 | Curriculum Generator | TBD (Phase 2+) |
| Q12 | Evaluation Harness | `pipeline/evaluation-harness/` |

Module specs at `docs/specs/modules/<slug>.md`. Spec sections carry status
badges (`[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`) per ADR-023 — scan
top-of-section to know what's real vs aspirational.

## Requirements

- CMake **3.28**+ and a C++20 compiler (Clang 18, GCC 14, or MSVC 2022) — Q2
- .NET 9 SDK — Q1
- Python **3.12** in a project venv — Q3/Q10/everything else
- Ninja recommended for C++ builds; `make` wraps everything cross-cutting

## Build + test

The top-level `Makefile` orchestrates every substrate. Most workflows:

```sh
make build              # C++ engine (BUILD_TYPE=Release default)
make test               # C++ unit tests
make q1-ci              # C# headless test pass
make q2-ci              # full C++ regression (~18 min, prefer background)
make q3-ci              # Q3 Python tests
make q10-ci             # Q10 trainer Python tests
make phase0-gate        # everything above (~2 min cached) — the canonical green gate
```

Run `make help` for the full target list. C++-only contributors can use
the CMake presets directly:

```sh
cmake --preset ninja-debug
cmake --build --preset ninja-debug
ctest --preset ninja-debug
```

The playable demo binary (Q2 substrate) is still buildable:

```sh
build/ninja-debug/Debug/sts2_fight --seed 42
```

— deterministic Silent vs CULTISTS_NORMAL, ANSI-rendered, exit 0.

## Test counts (live)

Roughly 100+ C# tests (Q1 headless), 250+ C++ tests (Q2 engine including
RNG / damage / power / cultist state-machine / IPC), 350+ Python tests
across `pipeline/`. Phase 1 gate report at
`engine/headless/docs/phase1-gate-report.md` is the source of truth for
what currently ships.

## Where to read next

| Goal | Doc |
|---|---|
| Understand the 5-phase plan + design rationale | `docs/scaling-strategy.md` |
| Why each architectural choice was made | `docs/specs/01-decisions-log.md` (24 ADRs) |
| What a single quantum does + its interface contracts | `docs/specs/modules/<slug>.md` |
| Combat mechanics specifics | `docs/cultists-normal-overview.md` |
| Hierarchical policy / oracle interplay | `docs/micro-macro-policy-architecture-note.md` |
| How the project uses Claude Code (subagents, skills, hooks) | `.claude/CLAUDE.md` |
| Phase 1 ship summary | `engine/headless/docs/phase1-gate-report.md` |

## Development setup

```sh
python3 -m venv .venv
.venv/bin/pip install -r requirements-dev.txt
.venv/bin/pre-commit install
.venv/bin/pre-commit install --hook-type pre-push
```

Pre-commit enforces `clang-format`, `ruff`, structural checks, and a few
project-specific gates (no system-python invocation, no relative `.venv`
paths). Pre-push gates main-bound pushes on proto-edit / ADR pairing and
(warn-only) spec-edit / substrate pairing per ADR-024.

`make tidy` runs `clang-tidy` (full warnings-as-errors). Nightly CI also
runs the slow analyzers (`scan-build`, `cppcheck`, `complexity`).

## Tooling

Maintainer tooling under `tools/`:

- `tools/content/` — registry seeders + validators (`phase1-silent.json`).
- `tools/ast-analyzer/` — libclang AST walker emitting analysis JSON + Markdown.
- `tools/upstream-sync/` — pulls upstream Godot/STS2 changes.
- `engine/cpp/tools/seed-pinner/` — regenerates `engine/cpp/tests/seeds/expected_values.h` after toolchain changes.

## Status

**Phase 1** — combat-only deterministic engine **shipped** (per
`engine/headless/docs/phase1-gate-report.md`): bit-identical state
round-trip, M2 IPC p99 = 14µs (35× under budget), 292/292 structural
probes, content manifest registered.

**Phase 1.5** — open. Per-step behaviour fill for 21 remaining
encounters; live-upstream probe comparison; Q2 Oracle adapter unpark
decision.

**Phase 2+** — full-run policy, run-level heads, `macro_context` derivation
(ADR-019), and the curriculum generator come online here.

License: TBD.

# sts2-ai

A C++20 command-line simulator of one Slay the Spire 2 fight: **The Silent (starter deck + Ring of the Snake) vs CULTISTS_NORMAL (Calcified Cultist + Damp Cultist), Ascension 0.**

Deterministic per `--seed`. Renders ANSI/UTF-8 to a standard terminal. Exits silently with code 0 when the fight ends.

## Requirements

- CMake **3.28** or newer
- A C++20 compiler — tested with Clang 18 and MSVC 2022
- Ninja (recommended) or Visual Studio 17 2022
- On Windows, a shell with the MSVC toolchain on `PATH` (e.g. *x64 Native Tools Command Prompt for VS 2022*)

## Build

The project ships with `CMakePresets.json`. From the repo root:

### Ninja (preferred)

```
cmake --preset ninja-debug
cmake --build --preset ninja-debug
```

### Visual Studio 2022 (multi-config)

```
cmake --preset vs2022
cmake --build --preset vs2022-debug
```

Both presets place binaries under `build\<preset>\<config>\` — e.g. `build\ninja-debug\Debug\sts2_fight.exe`, `build\vs2022\Debug\sts2_fight.exe`.

Other presets: `ninja-release`, `vs2022-release`. List them with `cmake --list-presets`.

### Configure-time options

| Option                    | Default | Effect |
|---------------------------|---------|--------|
| `STS2_BUILD_TESTS`        | `ON`    | Build the test executable |
| `STS2_WARNINGS_AS_ERRORS` | `ON`    | `/WX` (MSVC) or `-Werror` (Clang/GCC) |

Pass via `-D`, e.g.:

```
cmake --preset ninja-debug -DSTS2_BUILD_TESTS=OFF
```

## Run

```
build\ninja-debug\Debug\sts2_fight.exe --seed 42
```

Without `--seed`, a 64-bit seed is drawn from `std::random_device`.

## Playing

Each turn the program redraws the full combat state:

```
============================================================
  Round 1   Energy 3/3   Draw 5   Discard 0   Exhaust 0

  Silent   HP ################ 70/70   0 blk

  [0] Calcified Cultist   HP ################ 40/40   0 blk   BUFF
  [1] Damp Cultist        HP ################ 53/53   0 blk   BUFF

  o [0] Strike       (1) 6dmg
  o [1] Strike       (1) 6dmg
  o [2] Defend       (1) 5blk
  o [3] Neutralize   (0) 3dmg + Weak 1
  o [4] Survivor     (1) 8blk, discard 1
  o [5] Strike       (1) 6dmg
  o [6] Defend       (1) 5blk

> Action - digit to play, e=end turn, q=quit:
```

Input is line-buffered (type, then Enter):

| Key   | Action |
|-------|--------|
| `0..9`| Play the card at that hand index. If it targets an enemy, you'll be prompted for a target index next. |
| `e`   | End your turn. Discard the hand, run the enemy phase, draw a new hand. |
| `q`   | Quit immediately (exit code 0). |
| EOF   | Same as `q`. |

When the fight ends (player HP reaches 0 or both enemies are slain), the program renders one last frame and exits silently with code 0 — no banner, no summary.

## Test

```
ctest --preset ninja-debug
```

Or run the binary directly:

```
build\ninja-debug\Debug\sts2_simulator_tests.exe
```

252 active tests (3 skipped) cover RNG, damage formula, power semantics, card behaviour, cultist state machine, combat loop, renderer, and input parser.

## Architecture

`sts2_simulator` is a single static library covering the whole codebase. It exports ALIAS target `sts2::simulator`; the exe and tests link against it.

Layout:

- Public headers: `engine/cpp/include/sts2/<module>/*.h`
- Implementations: `engine/cpp/src/<module>/*.cc`
- Internal (test-visible only) headers: `engine/cpp/src/<module>/*_internal.h`

All code lives under the `sts2::` namespace.

- `sts2::game` — pure game logic, no I/O. Bare types (`Rng`, enums in `types.h`, `Power`, `Card`, `Player`, `Enemy`, `Vitals`) plus module-specific function namespaces (`sts2::cards`, `sts2::powers`, `sts2::damage`, `sts2::enemies`) and the `Combat` class.
- `sts2::render` — ANSI/UTF-8 rendering. Reads `Combat`, writes to `std::ostream`.
- `sts2::input` — line-buffered action/index parser. Stream-driven, no globals.
- `sts2::app` — argv parsing and prompt strings.
- `engine/cpp/src/main.cc` — wires layers: arg parsing, console init, combat setup, prompt loop.

## Tooling

`tools/ast-analyzer/` walks the source tree via libclang and emits an analysis JSON plus a Markdown report. Maintainer tooling — needs a venv:

    python3 -m venv .venv
    .venv/bin/pip install libclang
    .venv/bin/python tools/ast-analyzer/sts2_ast_analyzer.py --out tools/ast-analyzer/analysis.json
    .venv/bin/python tools/ast-analyzer/generate_part1_doc.py

`engine/cpp/tools/seed-pinner/` regenerates `engine/cpp/tests/seeds/expected_values.h`. Re-run after toolchain change:

    cmake --build build --target sts2_seed_pinner
    build/$<CONFIG>/sts2_seed_pinner > engine/cpp/tests/seeds/expected_values.h

The full design — and the encounter mechanics lifted from the STS2 source — is in `docs/cultists-normal-overview.md`.

## Make wrapper (Linux/macOS)

A top-level `Makefile` wraps the common workflows:

```
make build                    # configure + build (default Release)
make test                     # build and run ctest
make BUILD_TYPE=Debug build   # override config
make format                   # clang-format in-place
make tidy                     # clang-tidy
make cppcheck                 # cppcheck (requires submodules: see below)
make complexity               # lizard cyclomatic complexity
make cloc                     # SLOC counts
make coverage                 # lcov coverage report
```

Run `make help` for the full target list.

The `cppcheck` target needs the rules submodule:

```
git submodule update --init external/cppcheck-rules
```

## Development setup

After cloning, install pre-commit hooks:
```
.venv/bin/pip install pre-commit
.venv/bin/pre-commit install
.venv/bin/pre-commit install --hook-type pre-push
```
This enforces clang-format and structural checks on every commit; clang-tidy at push time (Wave 1).

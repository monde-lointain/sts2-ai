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

122 unit and integration tests cover the RNG, damage formula, power semantics, card behaviour, cultist state machine, combat loop, renderer, and input parser.

## Architecture

`sts2_simulator` is a single static library that builds the entire `src/` tree. It exports the ALIAS target `sts2::simulator`; the exe and the tests both link against `sts2::simulator`.

- `src/game/` — pure game logic. No I/O. Independently unit-tested.
  - `Rng`, `Types` (enums), `Power`, `Card`, `Player`, `Enemy` — data types
  - `Powers`, `Damage`, `Cards`, `Enemies`, `Combat` — behaviour
- `src/render/` — ANSI/UTF-8 rendering. Reads `Combat`, writes to `std::ostream`.
- `src/input/` — line-buffered action/index parser. Stream-driven, no globals.
- `src/main.cpp` — wires the layers together: arg parsing, console init, combat setup, prompt loop.

The full design — and the encounter mechanics lifted from the STS2 source — is in `cultists_normal_overview.md`.

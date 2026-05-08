# sts2-fight

A C++17 CLI simulator of a single Slay the Spire 2 fight: **The Silent (starter deck + Ring of the Snake) vs CULTISTS_NORMAL (Calcified Cultist + Damp Cultist), Ascension 0.**

Deterministic per `--seed`. Renders in ANSI/UTF-8 to a standard terminal. Exits silently with code 0 when the fight ends.

## Build (Windows)

Requires CMake 3.16+ and a C++17 compiler. Tested with Clang 18 and MSVC 2022.

### Ninja (preferred; needs a VS dev environment)

From a "x64 Native Tools Command Prompt for VS 2022" (or any shell where `cl` is on PATH):

```
cmake -S . -B build -G Ninja
cmake --build build
```

### Visual Studio generator (fallback)

```
cmake -S . -B build -G "Visual Studio 17 2022"
cmake --build build --config Debug
```

With the VS generator, executables land in `build\Debug\` rather than `build\`.

## Run

```
build\sts2_fight.exe --seed 42
```

(VS generator: `build\Debug\sts2_fight.exe --seed 42`)

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

| Key   | Action                                                              |
|-------|---------------------------------------------------------------------|
| `0..9`| Play the card at that hand index. If it targets an enemy, you'll be prompted for a target index next. |
| `e`   | End your turn. Discards your hand, runs the enemy phase, draws a new hand. |
| `q`   | Quit immediately (exit code 0).                                     |
| EOF   | Same as `q`.                                                         |

When the fight ends (player HP reaches 0 or both enemies are slain), the program renders one last frame and exits silently with code 0 — no banner, no summary.

## Test

```
build\sts2_fight_tests.exe
ctest --test-dir build --output-on-failure
```

115 unit and integration tests covering the RNG, damage formula, power semantics, card behaviour, cultist state machine, combat loop, renderer, and input parser.

## Architecture

- `src/game/` — pure game logic. No I/O. Independently unit-tested.
  - `Rng`, `Types` (enums), `Power`, `Card`, `Player`, `Enemy` — data types
  - `Powers`, `Damage`, `Cards`, `Enemies`, `Combat` — behaviour
- `src/render/` — ANSI/UTF-8 rendering. Reads `Combat`, writes to `std::ostream`.
- `src/input/` — line-buffered action/index parser. Stream-driven, no globals.
- `src/main.cpp` — wires the layers together: arg parsing, console init, combat setup, prompt loop.

The full design is in `cultists_normal_overview.md` (mechanical reference for the encounter, lifted from the STS2 source).

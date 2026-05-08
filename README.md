# sts2-fight

A C++17 CLI simulator of a Slay the Spire 2 fight: Silent vs CULTISTS_NORMAL. Deterministic given a seed; intended as a foundation for AI experimentation against a single encounter.

## Build (Windows)

Requires CMake 3.16+ and a C++17 compiler. Two paths tested:

### Ninja (preferred; needs a VS dev environment)

From a "x64 Native Tools Command Prompt for VS 2022" (or any shell where `cl` is on PATH):

```
cmake -S . -B build -G Ninja
cmake --build build
```

### Visual Studio generator (fallback)

If Ninja cannot find `cl`:

```
cmake -S . -B build -G "Visual Studio 17 2022"
cmake --build build --config Debug
```

With the VS generator, executables land in `build\Debug\` rather than `build\`.

## Run

```
build\sts2_fight.exe --seed 42
```

Prints `seed=42` and exits 0. Without `--seed`, a 64-bit seed is drawn from `std::random_device`.

## Test

```
build\sts2_fight_tests.exe
```

Or via CTest:

```
ctest --test-dir build --output-on-failure
```

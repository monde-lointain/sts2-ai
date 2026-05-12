# sts2_seed_pinner

Regenerator for `tests/seeds/expected_values.h`. Build with `cmake --build --preset ninja-debug`, then run `build\ninja-debug\Debug\sts2_seed_pinner.exe > tests\seeds\expected_values.h` to overwrite the header. Re-pin after any toolchain or STL change (values are not portable across libstdc++ / libc++ / MSVC STL).

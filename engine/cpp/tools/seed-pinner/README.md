# sts2_seed_pinner

Regenerator for `tests/seeds/expected_values.h`. Build with `cmake --build --preset ninja-debug`, then run `build\ninja-debug\Debug\sts2_seed_pinner.exe > tests\seeds\expected_values.h` to overwrite the header. Re-pin after any toolchain or STL change (values are not portable across libstdc++ / libc++ / MSVC STL).

On a POSIX `make`-driven build, the equivalent regen is:

```sh
make build
build/Release/sts2_seed_pinner > engine/cpp/tests/seeds/expected_values.h
make format        # clang-format the regenerated header in-place
```

## Per-encounter pin registry

`pin_seeds.cc` carries an in-source `kEncounterSeeds` table of
`(encounter_id, seed)` entries (S2-T5). Today the table covers
`CULTISTS_NORMAL` at seeds `0x42` and `0xC0FFEE`; per-encounter coverage
grows in Phase-1.5+ as the C++ engine acquires non-cultist encounter
mechanics. The HP / shuffle / deck constants emitted into
`expected_values.h` are unchanged by the data-driven refactor: the table
drives the `--manifest` sidecar (below) only.

## `--manifest` (Q2-ADR-005 stamping sidecar)

```sh
build/Release/sts2_seed_pinner --manifest > expected_values.h 2> manifest.json
```

Without the flag: behavior is unchanged (back-compat — `expected_values.h`
on stdout, search-range diagnostics on stderr).

With `--manifest`: same header on stdout; additional JSON sidecar appended
to stderr after the search-range diagnostics. Shape:

```json
{
  "algorithm_sha": "phase1a-stub-algorithm-sha",
  "build_sha": "phase1a-stub-build-sha",
  "version_tag": "Q2-Phase-1A-2026-05-12-001",
  "registry_sha": "<64-hex SHA-256 of contracts/registry/phase1-silent.json>",
  "encounters": [
    {"encounter_id": "CULTISTS_NORMAL", "seeds": [66, 12648430]}
  ]
}
```

The `algorithm_sha` / `build_sha` / `version_tag` fields come from
`sts2::oracle::adapter::current_manifest()`; the `registry_sha` field is
`sts2::oracle::registry::current_phase1_registry_sha256()`. Both surfaces
are owned by `sts2::oracle_adapter` / `sts2::oracle_registry` (S2-T1).

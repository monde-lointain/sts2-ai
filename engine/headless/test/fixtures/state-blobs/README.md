# Q1 → Q2 handoff state-blob fixture corpus

Six fixture state-blobs produced by Q1's M1 codec at simulator boot for
pinned `(seed, encounter_id)` pairs. The corpus is the regression set the Q2
oracle adapter (per pipeline ADR-011) is built against; reading each
`state.blob` and projecting onto Q2's `CompactState` is the canonical
exercise the Q2 adapter must pass for the wave.

## Catalog

| # | dir                                   | seed | encounter id            | role |
|---|---------------------------------------|------|-------------------------|------|
| 1 | `01-cultists-normal-seed42`           |   42 | `CultistsNormal`        | Smoke per-step gold standard (full Godot per-step parity). Indexed 2-monster slot. |
| 2 | `02-fossil-stalker-elite-seed42`      |   42 | `FossilStalkerElite`    | Elite, initial-state-only. Single-monster. |
| 3 | `03-fossil-stalker-elite-seed1337`    | 1337 | `FossilStalkerElite`    | Seed variation — tests Q2 seed-dependence for same encounter. |
| 4 | `04-kaiser-crab-boss-seed42`          |   42 | `KaiserCrabBoss`        | Phase-1 boss; spawns Crusher + Rocket. Exercises named-slot encoding (`"crusher"` / `"rocket"`). |
| 5 | `05-louse-progenitor-normal-seed42`   |   42 | `LouseProgenitorNormal` | Single-monster non-smoke normal. Drop-in for the deleted TwoLouseNormal slot. |
| 6 | `06-small-slimes-seed42`              |   42 | `SmallSlimes`           | B.1-ε DEFER. Initial-state only; stresses Q2 MissingUpstream path. |

## Required header notes (project-lead approved, verbatim)

### #4 — KaiserCrabBoss

> Phase-1 KaiserCrabBoss spawn-time powers (BackAttackLeft/Right, CrabRage, Surrounded) reference power IDs absent from the Phase-1 power catalog; Q2 adapter must define unknown-power-reference behavior — surface in Q2 S0 ADRs.

### #6 — SmallSlimes

> Encounter cannot run end-to-end in Q1 Phase-1A — encounter-RNG plumbing deferred to B.1-ε. Initial-state only; stresses Q2 MissingUpstream path.

## Per-fixture layout

Each subdirectory contains exactly two files:

- `state.blob` — bytes from `Sts2Headless.Adapters.StateCodec.StateCodec.Serialize`
  produced at simulator boot for the listed `(seed, encounter)` pair. The blob
  is post-`StartCombat`, pre-first-script-action — i.e., what M1 produces when
  the host's composition root finishes bootstrapping combat.
- `metadata.json` — keys: `seed`, `encounter_id`, `role`,
  `expected_canonical_hash_hex` (lowercase-hex SHA-256 over the blob bytes),
  `blob_bytes` (file size).

## Canonical hashes

| # | dir                                  | bytes | `expected_canonical_hash_hex`                                        |
|---|--------------------------------------|-------|----------------------------------------------------------------------|
| 1 | `01-cultists-normal-seed42`          | 5575  | `fbaa37129d5d963fb8c98558233d5d5021003aed4c0cefeb361a96929482fbe5`   |
| 2 | `02-fossil-stalker-elite-seed42`     | 5493  | `ef1b2a5630ef9ebd067ae13b0831d5f8d5c4dcff6df61939bf20c572f96a7d0f`   |
| 3 | `03-fossil-stalker-elite-seed1337`   | 5495  | `92ebc2e91a62a521f055d791e624e293aed2ed51cbac17a963babf11dd295a45`   |
| 4 | `04-kaiser-crab-boss-seed42`         | 5562  | `9edb550ef2e4a99f9544b58516f64d8d803919acfff9db29be91938a0a9cef8e`   |
| 5 | `05-louse-progenitor-normal-seed42`  | 5524  | `37e7517005a0a50c05240874a6e2969c490617711bdd4d2d04c3361eaaaab392`   |
| 6 | `06-small-slimes-seed42`             | 5549  | `d33371738949b606df7713b1b19c5645fb2e4d8c822c72c6224a6ce7c8cf1fbd`   |

The hashes were computed by `Sts2Headless.Domain.Determinism.CanonicalHash.Sha256Hex`
over the M1-serialized blob at the recipe carrier inputs (see
`test/Sts2Headless.Tests.Tools/Fixtures/StateBlobFixtureRecipe.cs`). Every
`make ci` run reproduces and asserts these match — see
`StateBlobFixtureRegressionTests.cs`.

## Producing / regenerating

The recipe lives in
`test/Sts2Headless.Tests.Tools/Fixtures/StateBlobFixtureRecipe.cs`. To
regenerate after an intentional Q1 change shifts the blob bytes (e.g., schema
bump, codec field-order change, monster HP edit):

```sh
STS2_REGEN_STATE_BLOB_FIXTURES=1 \
  dotnet test test/Sts2Headless.Tests.Tools \
    --filter "FullyQualifiedName~Regenerate_all_fixtures_in_place"
git diff test/fixtures/state-blobs/    # review carefully
git add test/fixtures/state-blobs/
git commit
```

CI never regenerates: the test is a one-shot helper gated by the env var.
The default regression test (`Fixture_bytes_reproduce_from_clean_Q1_boot`)
asserts byte-for-byte equality with what HEAD's M1 produces and fails CI
otherwise.

## Inspecting

Use `tools/StateBlobDumper` to inspect any of these blobs:

```sh
dotnet run --project tools/StateBlobDumper -- \
  test/fixtures/state-blobs/01-cultists-normal-seed42/state.blob
```

Output is JSONL (one object per line): envelope → sections → canonical hash.
See `tools/StateBlobDumper/README.md` for the field schema. When debugging a
Q2 adapter mismatch, diff the dumper output against Q2's decode of the same
blob.

## Stability contract

These fixtures are part of the cross-quantum Q1→Q2 wire contract. Their
bytes must not change except under one of:

1. An intentional Q1 M1 codec schema bump (state-codec.md SchemaVersion history).
2. An intentional content-catalog change affecting registered ids or
   encounter monster sets.
3. An intentional `ManifestStamp` recipe change (the fixture build-id and
   git-sha placeholders live in `StateBlobFixtureRecipe`).

In any of those cases, regenerate, document the cause in the commit message,
and notify Q2.

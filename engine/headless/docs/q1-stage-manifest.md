# Q1 Stage Manifest

Backfilled 2026-05-12 per project-lead direction (option (b) — backfill mapping without history surgery).

Maps Q1's stage-by-stage development to commits in the **upstream genealogy repo** `~/development/projects/cs/sts2-headless/`, preserved as tag `phase1-genealogy/v1` and bundled at [`engine/headless/genealogy/phase1-genealogy-v1.bundle`](../genealogy/phase1-genealogy-v1.bundle).

The monorepo `cpp/sts2-ai/` imported the upstream wholesale in commit `4120453 chore(q1): migrate headless simulator into services`, so per-stage S{N}-T history is not visible in `cpp/sts2-ai`'s git log. This manifest preserves audit-ability without rewriting history.

## Migration receipt

| | |
|---|---|
| Monorepo import | `4120453 chore(q1): migrate headless simulator into services` |
| Upstream HEAD at import | Post-B.1-final + post-P-refactors |
| Preservation tag | `phase1-genealogy/v1` (annotated) at upstream HEAD |
| Bundle archive | `engine/headless/genealogy/phase1-genealogy-v1.bundle` (1.4 MB, all refs) |
| Verified byte-identical | `Phase1Monsters.cs`, `Phase1Encounters.cs` (full tree-diff on request) |

## How to dereference

```
# Existing upstream checkout
git -C ~/development/projects/cs/sts2-headless show <sha>

# From bundle (anywhere, no network)
git clone engine/headless/genealogy/phase1-genealogy-v1.bundle /tmp/sts2-genealogy
git -C /tmp/sts2-genealogy log --oneline --grep='S[0-9]\+-T'
```

## Phase 1A stage table (S0–S13)

| Stage | Module | Sub-tasks (SHA) | Merge |
|---|---|---|---|
| S0 | Repo skel + banned-API analyzer + CI tripwire | T1 `dbbb72e`, T2 `0c629b2`, T3 `4630f94` | (linear) |
| pre-S1 | M5 prior-session rescue | `4b091d1` | — |
| S1 | M5 Determinism Kernel — RNG byte-equal upstream, IClock, RngSerializer, CanonicalHash | T1 `f0af1c7` (RNG differential), T2 `d2904ce`, T3 `eaebf74`, T4 `716273c`, T5 `1d2931e` | `2e06e2f` |
| S2 | M8 Engine Strip — categorical Godot stubs | T1 `6b100f8` (StubRegistry), T2 `968f6d7`, T3 `a9566e7`, T4 `876070b`, T5 `38436a4` | `6c4bdab` |
| S3 | M7 Content Catalog framework | T1 `9609b2a`, T2 `6c60609`, T3 `528ed85`, T4 `486a55e`, T5 `45baed7` | `a562c46` |
| S4 | M6d Action Queue + HookRegistry | T1 `d41ddc5`, T2 `24c65f8`, T3 `ee19658`, T4 `46e882d`, T5 `654af4e` (6 pinned ordering scenarios) | `955c62e` |
| S5 | M6c smoke content + abstract bases | T1 `36fbca2`, T2 `23a6d7c`, T3 `f2cbde5`, T4 `3b7d1c9`, T5 `70842cb`, T6 `32efb7f` + fix `a40d946` | `db1deba` |
| S6 | M6a Combat Domain (cheap-clone state types) | T1 `e5441cd`, T2 `d67aaea`, T4 `b805563` (turn lifecycle), T6 `83b3458` (legal-action enum), T7 `5315996` (HARD GATE w/ golden hashes) + fix `dd7ecc8` | `38f9d4d` |
| S7 | M1 State Codec — bit-identical roundtrip CI gate | T1 `dfc0966`, T2 `665ac65`, T3 `c46678a`, T4 `417f597`, T5 `4a87fb0` (21 fixtures, HARD GATE) | `7d77c3b` |
| S8 | M9 Process Host | T1 `cbf002a`, T2 `4832a96`, T3 `67186f2`, T4 `b9aaed2`, T5 `5a98004` (Prometheus), T6 `4dbd944`, T7 `d39f945`, T8 `9e7fb1d` (reactive from S13) | `6180b6f` |
| S9 | M2 Hook IPC — p99 < 500µs HARD GATE | T1 `0eddc40` (SPSC ring + posix sem + shmem), T2 `c791be5`, T3 `ef68720`, T4 `b75aea7`, T5 `4c174b4` (mock worker), T6 `283c51d` (p99 measured **14.24 µs**, p999 34.43 µs, max 80.48 µs, 48 alloc-bytes/RT) | `978e805` |
| S10 | M3 Replay Recorder | T1 `8d7e378`, T2 `1fac16a`, T3 `9dd8dee`, T4 `b1b0606`, T5 `f6dc325`, T6 `a2a6f7a` (JSON debug dumper) | `d5bb118` |
| S11 | M4 Control Plane (JSON-RPC over Unix socket) | T1 `980d78b`, T2 `93dd563`, T3 `9cbae28`, T4 `ead4701`, T5 `5a1b24c`, T6 `9befc1a`, T7 `12bc810` | `9f8bf65` |
| S12 | M6c full Silent content | T1 `711ca4d` (Silent/colorless/status/curse cards), T2 `6e86970` (53 relics), T3 `2a05f34` (40 powers), T4 `28c20ca` (30 monsters), T5 `a6b0eff` (potions + encounters) | `083c1d2` |
| S13 | Determinism Probe + Phase-1 Gate | T1 `200b51f`, T2/T3 `9d56ba1`, T4 `e13bb48`, T5 `ced8eb6` (gate report — PARTIAL verdict), baseline merge `f56b086` | `18a874e` |

## Post-S13 fill-in waves

### Stream-B + Stream-C

| Wave | Description | Notable SHAs | Merge |
|---|---|---|---|
| Stream-B | S12 behavior fill-in (narrower than lead's bar at the time) | T3 `1fbe895` (Chomper 2-state rotation) | `1754115` |
| Stream-C | Narrow initial-state-upstream spike — canary FIRED (10 Q1 encounters with no upstream STS2 equivalent) | T3 `a8344fe` (capture 220 upstream goldens), T4 `1b6d942` (probe `--mode initial-state-upstream`) | `a87275f` |

### B.1 content reconciliation wave

| Sub-stream | Description | Notable SHAs | Merge |
|---|---|---|---|
| B.1-α (RNG layer) | T1 `0ed7285` (master seed string-hash), T2 `e2759ed` (RunRngSet threading), T3 `48ffc9d` (codec schema 1→2), T4 `143a176` | | `c695a8c` |
| B.1-β (content audit) | T1 `ebe9a1f`/`f8c0c6d` (Bowlbug/Fossil/Frog HP), T2 `bb2331d`, T3 `56be8c7`, T4 `aeae888` (parity diff) | | `5e20bc8` |
| B.1-γ (behavior fill-in) | T2 `282f0fd` (MoveBranch resolver), T3 `612e77b` (intent rotations), T4 `a5293f0` (relic SubscribeHooks), T5 `6bab2b6` (X-cost / Shiv-exhaust + codec 2→3) | | `6f31860` |
| B.1-δ (encounter port-or-delete) | T1+T2+T5 `db0c55e` (decisions doc) | | `8576d42` |
| **B.1-final** | T1 `a73c8ba` (Lagavulin → LagavulinMatriarch rename + HP), T2 `4cbe3b2` (delete 7 invented + port KaiserCrabBoss→Crusher+Rocket + add LouseProgenitorNormal) | M-Headless gate FLIPPED PARTIAL → PASS | **`9d89f37`** |
| Post-B.1 goldens regen | | `baae17e` | — |
| Q1-ADR-011 (parallel partition by file, R8) | `7895254`, `05134ae` | | — |

### P-refactors (post-B.1, pre-monorepo import)

| Refactor | Description | SHA |
|---|---|---|
| P1 | Unify `ApplyDamageModifiers` into `DamageModifier.Modify` | `356d1c3` |
| P2a | Strip `MonsterModel` mutable instance state | `a59c265` |
| P2b | Strip `CardModel` mutable upgrade state | `e3970d2` |
| P2c | Strip `PowerModel` mutable `Amount` + dead virtual hooks | `e238886` |
| P3 | Decompose `CombatEngine.StartCombat` into named helpers | `0f6a099` |

(P-refactors aimed at state-cleanliness / code clarity. No Phase-1A behavioral surface change.)

## Preservation invariant

The 93 S{N}-T SHAs MUST remain dereferenceable indefinitely. If `~/development/projects/cs/sts2-headless/` is cleaned up, rehydrate from the bundle:

```
git clone engine/headless/genealogy/phase1-genealogy-v1.bundle ~/development/projects/cs/sts2-headless-restored
```

Tag `phase1-genealogy/v1` names this preservation point. Future archive bumps (e.g., post-Phase-1.5) create `phase1-genealogy/v2` without overwriting v1.

## Status as of this manifest

- **Phase-1A** — ratified 2026-05-12.
- **Phase-1 (per `docs/plans/q1-implementation-plan.md` §6.1)** — NOT closed pending Phase-1.5 (14-encounter per-step + live-Godot per-step full corpus + B.1-ε encounter-RNG).
- **R4 (headless port ≤ 2 mo)** — SUBSTANTIALLY MITIGATED.
- **R9 (Phase-1.5 scope/timing)** — NEW, IN PROGRESS; target date + live-Godot approach pending.

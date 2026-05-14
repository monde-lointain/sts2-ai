# Module: State Codec (M1)

> Versioned binary serializer for `(CombatState, RunState, RngBundle, TokenMap, ManifestStamp)`. Bit-identical roundtrip is the contract. Owner of the Game Version Manifest stamped onto every blob and replay. **This document is the Q2 oracle-adapter handoff package for the Q1→Q2 contract (per pipeline ADR-011).**

## Responsibilities

- Serialize and deserialize the full Q1 state: `CombatState` (M6a-owned), `RunState` (M6b-owned), `RngBundle` (M5-owned, opaque per Q1-ADR-003), per-content state on M6c instances (polymorphic via class-id discrimination), and `M6d.ActionQueue` + `HookRegistry`.
- Stamp every blob with the Game Version Manifest (the 5-tuple: schema major/minor + game_version + simulator_build_sha + registry_sha) per pipeline ADR-005 and Q1-ADR-005.
- Validate version on deserialization: reject blobs with incompatible state schema version with an explicit `SchemaVersionError`. **Never silently coerce.**
- Guarantee bit-identical roundtrip: `Serialize(Deserialize(Serialize(state))) == Serialize(state)` byte-for-byte, enforced by CI per pipeline `scaling-strategy.md` §4.1 #4.
- Provide a canonical state hash (64-char lowercase-hex SHA-256, via `Sts2Headless.Domain.Determinism.CanonicalHash.Sha256Hex`) used by the determinism probe (Q1-ADR-007) and by Q2's oracle-agreement signal.
- Provide schema migration tooling for forward-compat reads where possible (additive fields tolerated; structural changes require explicit migration).

`[Phase 1 scope]` — full M1 functional. Combat-only blobs serialized; M6b state stubbed in the blob (placeholder fields). Section ids in use today: `Rng=0`, `Tokens=1`, `CombatState=2`.

`[Phase 2]` — full `RunState` serialized. Schema version bumped (see SchemaVersion history below).

`[Phase 3+]` — schema migrations between versions become part of the patch-adaptation workflow.

Out of scope: the actual state types (M6a / M6b / M6c / M6d / M5 own those); IPC framing (M2); replay file format (M3 — but M3 embeds M1 blobs as checkpoints).

## Data Ownership

M1 owns the **versioned binary state schema** (Q1's load-bearing data contract) and the **Game Version Manifest schema**. The codec is the single source of truth for the on-wire layout described below; protobuf envelopes (see "Cross-quantum envelope" below) wrap these bytes but never replace them.

The schema is **not** JSON or any human-readable format. Binary, fixed-endianness (little-endian), aligned per type. Field ordering is part of the schema and changes require version bumps.

### Section-table layout (Phase 1, schema v3)

```
[HEADER]
  u32  magic           = 0x53435443 ("STCT" little-endian, Sts2 sTate Codec)
  u16  schema          = StateCodecConstants.SchemaVersion (v3 at time of writing)
  u16  header_size     = byte-count of the stamp block below
  stamp (header_size bytes):
    u8   git_sha_len
    utf8 git_sha
    u16  build_id_len
    utf8 build_id
    32   content_hash  = SHA-256 of the registered-content id set
                        (sort ASCII-ordinal, NUL-join, UTF-8, SHA-256)
[SECTIONS]                 (canonical order: Rng → Tokens → CombatState)
  for each section:
    u16  section_id    (see SectionId enum)
    u32  section_size
    body (section_size bytes)
  terminator:
    u16  0xFFFF
[TRAILER]                  (TrailerSizeBytes = 36)
  u32  trailer_magic   = 0x53544354
  32   sha256(everything before trailer)
```

All multi-byte integers are little-endian. String fields are length-prefixed UTF-8; the prefix width is field-specific (`u8` for `git_sha`, `u16` for `build_id`, `u32` for in-section strings) and is part of the wire schema.

### Section ids (Phase 1)

| id | name           | owner | wire shape                                                                            |
|----|----------------|-------|---------------------------------------------------------------------------------------|
| 0  | `Rng`          | M5    | self-describing envelope; opaque M5 bytes inside (see "Per-section codecs" below).    |
| 1  | `Tokens`       | M7    | `u32 count; count × (length-prefixed UTF-8 string, i32 id)` in insertion order.       |
| 2  | `CombatState`  | M6a   | flattened CombatState; field-declaration order; see "Per-section codecs" below.       |

Future section ids (additive, append-only — existing variants must never change value):

| id | name (planned) | owner | phase  |
|----|----------------|-------|--------|
| 3  | `CombatAux`    | M6a   | 2      |
| 4  | `RunState`     | M6b   | 2      |
| 5  | `Hooks`        | M6d   | 2 / 3  |

The deserializer treats unknown ids as "unsupported" today; forward-compat additive reads ship alongside the next id that needs them. Until then, reading a blob authored by a newer codec fails with `StateCodecException` and an explicit diagnostic — never silent corruption.

### Per-section codecs

The host-side reference implementation is
`src/Sts2Headless.Adapters/StateCodec/StateCodec.cs`. Q2 adapter authors should
read the corresponding `Write*` / `Read*` helpers there directly when in doubt;
the spec narrative below pins the field order. **Wire layout, not the C# class
shape, is the contract.**

**Rng (section id 0).** Envelope wraps M5's `RngStateSerializerV1` output:

```
u8   run_blob_present     = 1            (Phase-1 always 1)
u32  run_blob_len
N    M5 RunRngSet bytes   (opaque)
u8   player_blob_present  = 1
u32  player_blob_len
N    M5 PlayerRngSet bytes (opaque)
```

The `_present` flags reserve a future stage to omit either set during a partial
restore without bumping the schema. The M5 inner blobs are opaque from M1's
viewpoint and version themselves independently per Q1-ADR-003.

**Tokens (section id 1).** Insertion-ordered `(string, i32 id)` pairs. The
TokenMap is rebuilt by replaying inserts in order; a roundtrip-mismatch on
reassigned id is a hard error. Token ordering must come from
`TokenMap.Enumerate()`; *never* enumerate a `Dictionary<,>`.

**CombatState (section id 2).** Flattened CombatState fields in
declaration order. The wire schema is exhaustively pinned by the doc-comments
on `StateCodec.WriteCombatState`. Field order at schema v3:

```
i32  TurnCounter
i32  Phase                  (CombatPhase cast)
Creature Player
i32  EnemyCount
EnemyCount × Creature
i32  Energy
i32  BaseEnergyPerTurn
i32  HandDrawSize
CardPile DrawPile
CardPile HandPile
CardPile DiscardPile
CardPile ExhaustPile
i32  PlayerRngCounter
i32  MonsterRngCounter
i32  AttacksPlayedThisTurn  (v2; Stream-B-T4)
i32  CardsDrawnThisCombat   (v2; Stream-B-T4)
i32  LastSpentEnergy        (v3; B.1-gamma-T5 X-cost snapshot)
i32  ExhaustedShivCount     (v3; B.1-gamma-T5 Shiv counter)
```

Nested types (declaration order on each):
- `Creature`: `u32 Id; lp-utf8 Name; i32 CurrentHp; i32 MaxHp; i32 Block; i32 PowerCount; PowerCount × PowerInstance; u8 IntentPresent (0/1, if 1: MonsterIntent); bool IsPlayer`.
- `PowerInstance`: `lp-utf8 ModelId; i32 Stacks; u32 SourceCreatureId; bool JustApplied`.
- `MonsterIntent`: `i32 Kind; i32 DamagePerHit; i32 HitCount; i32 AppliesCount; AppliesCount × (lp-utf8 PowerId, i32 Stacks); lp-utf8 MoveId` (MoveId appended at v2, Stream-B-T3).
- `CardPile`: `i32 Count; Count × CardInstance`.
- `CardInstance`: `u32 InstanceId; lp-utf8 ModelId; i32 UpgradeLevel; u8 CostOverridePresent (0/1, if 1: i32 CostOverride)`.

(`lp-utf8` = `u32 length; UTF-8 bytes`.)

### Manifest stamp — 5-tuple invariants

Every blob carries a single 5-tuple, stamped at the head of the header block.
The header byte-layout splits the conceptual 5-tuple across the codec's own
header (`schema_major`, `schema_minor`) and the embedded `ManifestStamp` record
(`game_version`, `simulator_build_sha`, `registry_sha`):

| Conceptual field        | Wire location                                          | Notes                                                                                          |
|-------------------------|--------------------------------------------------------|------------------------------------------------------------------------------------------------|
| `schema_major`          | header `schema` u16, high byte (`schema >> 8`)         | Today 0; bumped only on breaking layout changes (e.g., section-id remap).                      |
| `schema_minor`          | header `schema` u16, low byte (`schema & 0xFF`)        | Today 3; bumped on additive field additions (see SchemaVersion history below).                 |
| `game_version`          | `ManifestStamp.BuildId` (u16-length-prefixed UTF-8)    | Caller-supplied (M9 / control-plane). E.g., `"Q1-Phase1-2026-05-12-001"`.                      |
| `simulator_build_sha`   | `ManifestStamp.GitSha` (u8-length-prefixed UTF-8)      | Caller-supplied. SHA of the Q1 source tree producing the blob.                                 |
| `registry_sha`          | `ManifestStamp.ContentHash` (32 raw bytes)             | Pre-sorted ASCII, NUL-joined, UTF-8, SHA-256 of the registered-content id set. See D6.         |

Invariants:

1. **Stamping is total.** Every `Serialize` call produces a blob carrying all five fields. Callers that lack one (e.g., a test that doesn't care about `game_version`) supply `""` rather than skipping the field. The codec never auto-fills.
2. **`registry_sha` is order-independent.** The pre-sort in `ManifestStamp.ContentHashFromIds` is part of the contract: the same set of ids hashes to the same 32 bytes regardless of registration order.
3. **Schema rejection is hard.** Deserialize rejects any blob whose `schema` u16 doesn't match the codec's current `SchemaVersion`. There is no silent acceptance of older or newer blobs.
4. **Stamp mismatch is informational at the codec layer.** Deserialize does **not** refuse a stamp-mismatch — the codec returns the parsed `StateBlob` with the stamp intact, and downstream layers (M2 session handshake, Q2 oracle adapter) enforce cross-process equality. The codec's job is to surface the stamp, not to reject on it.
5. **Bit-identical roundtrip is the gate.** Beyond every individual invariant, `Serialize(Deserialize(Serialize(t))) == Serialize(t)` must hold byte-for-byte for every fixture in the corpus. CI failure here blocks merge.

### Cross-quantum envelope (`state_blob.proto v0.1`)

The pipeline's `contracts/schemas/game-simulator/state_blob.proto` declares a
`StateBlobEnvelope` message that wraps M1's binary output for IPC + storage
crossing the Q1→Q2 quantum boundary. The proto's fields map 1:1 onto this
spec's 5-tuple plus the codec's binary payload:

```
message StateBlobEnvelope {
  uint32 schema_major        = 1;     // → header schema high byte
  uint32 schema_minor        = 2;     // → header schema low byte
  string game_version        = 3;     // → ManifestStamp.BuildId
  string simulator_build_sha = 4;     // → ManifestStamp.GitSha
  string registry_sha        = 5;     // → hex of ManifestStamp.ContentHash
  bytes  payload             = 6;     // → full M1 binary blob bytes
  bytes  payload_sha256      = 7;     // → SHA-256 of payload (mirrors the M1 trailer hash)
}
```

**Status (D1 pending lead approval):** the v0.1 proto landed today carries
exactly the seven fields above. Comment-block additions describing the v3
schema_minor bump and the `payload_sha256 == M1 trailer hash` equivalence are
queued for the D1 orchestrator-drafted update — **per project-lead direction,
M1 does not modify the proto itself; D1 owns that change.** This spec
cross-references the pending update so Q2 adapter authors can read both
documents and see the same conceptual contract.

When the v0.1 → v1.0 promotion lands, the proto picks up:
- A repeated section-ids field (so Q2 can fast-skip on a known section-id set).
- A wire-format version stamp on the proto itself (so the proto's evolution is independent of the M1 schema).

Both are append-only and will not change the existing field numbers.

### Q2 oracle-adapter consumption (per ADR-011)

Q2 owns the `engine→CompactState` adapter. The Q1→Q2 wire is
`StateBlobEnvelope`; Q2's adapter reads `payload` via Q2's own decoder. The
codec's contract to Q2:

1. The `payload` bytes are exactly what `StateCodec.Serialize` returned. No
   trailing zero-pad, no length prefix beyond the envelope's own
   `bytes payload` wire-type encoding.
2. The first 8 bytes of `payload` are the `magic`+`schema`+`header_size`
   triple. Q2's adapter must check `magic == 0x53435443` and reject on
   mismatch before touching anything else.
3. The trailer is 36 bytes (`u32 trailer_magic + 32 SHA-256`). Q2 must
   independently re-hash and reject on mismatch — the envelope's
   `payload_sha256` is informational at the proto layer; the codec's trailer
   hash is the authoritative tamper-detection.
4. Section ordering is canonical: Q2 may rely on `Rng → Tokens → CombatState`
   in Phase 1. Future stages append new sections at the end; Q2 must accept
   trailing-unknown sections (preserved for round-trip).
5. Unknown power / monster / card ids encountered while projecting onto
   `CompactState` are **Q2's adapter concern** — M1 emits whatever the
   producing engine had registered. The KaiserCrabBoss fixture (#4 in the
   handoff package) exercises this path directly: its spawn-time powers
   (`BackAttackLeftPower`, `BackAttackRightPower`, `CrabRagePower`,
   `SurroundedPower`) reference ids absent from the Phase-1 power catalog,
   so the blob's `PowerInstance.ModelId` strings are stable but not
   resolvable in Q1's catalog. Q2's S0 ADRs must pin
   `unknown-power-reference` behaviour.

### SchemaVersion history

| Version | Wire change                                                                                                                            |
|---------|----------------------------------------------------------------------------------------------------------------------------------------|
| v1      | Initial M1 codec; no Stream-B fields; no per-creature `MoveId` on `MonsterIntent`.                                                     |
| v2      | Stream-B-T3: `MonsterIntent.MoveId` appended. Stream-B-T4: `CombatState.AttacksPlayedThisTurn` + `CardsDrawnThisCombat` appended.        |
| v3      | B.1-gamma-T5: `CombatState.LastSpentEnergy` + `ExhaustedShivCount` appended (X-cost snapshot + Shiv-exhaust counter).                  |

Each bump is additive — older blobs cannot be read by a newer codec without an
explicit migrator. Migrator authoring is Phase 3+ scope; today we reject and
move on.

### Field observability tagging (per pipeline ADR-016)

Per pipeline ADR-016 (architecture-note cascade, 2026-05-14), deployed-policy
inference inputs are restricted to player-observable or explicitly
belief-sampled fields. **M1 emits all fields — including hidden source state
useful for simulator correctness, labeling, debugging, and counterfactual
analysis.** Filtering is the **consumer-side** responsibility (Q8 rollout
workers, Q9 inference server, Q10 trainer's deployed-input audits). Q1's
contract is to tag each emitted field with one of three observability classes
so consumers know what to filter:

| Tag                    | Meaning                                                                                              | Deployed-input eligibility |
|------------------------|------------------------------------------------------------------------------------------------------|----------------------------|
| `SOURCE_PERFECT`       | Hidden from the player. Examples: RNG counters, future encounter/event queues, hook-private fields. | **Never.** Q9 must filter. |
| `POLICY_VISIBLE`       | Player-observable. Examples: current HP, hand contents, visible enemy intents.                       | **Yes.**                   |
| `BELIEF_SAMPLED`       | Hidden, but Q9 reads a sampled belief over its value rather than the true value.                     | **Yes (as belief sample).** |

**Tag manifest location.** A `field-observability-tags.md` manifest table lives
next to this spec (Phase-2 deliverable; not present in Phase-1A). The manifest
enumerates each field reachable through the section-table layout above and
records its tag. For Phase-2 substrate boot (Q8/Q9/Q10), the manifest is the
contract — Q9 reads it at startup, builds a per-field include/exclude bitmap,
and the bitmap is applied at the deployed-input boundary.

**Phase-1A scope.** Phase-1A has no deployed inference; no consumer reads the
tag manifest yet. The tagging requirement is **anchored here** so the
manifest's authoring is a Phase-2 deliverable when Q9 boots, not a
retroactive retrofit. Q1 Phase-1A and Phase-1.5 emit fields unchanged; the
observability classification is a documentation artifact only until Q9
substrate exists.

**Audit.** Q12 evaluation harness adds a `no-hidden-state-leak` audit (per
pipeline `evaluation-harness.md` §Tradeoff-test reports) that scans deployed
inference inputs and asserts no `SOURCE_PERFECT` field appears. Any non-zero
count is a P0 alert per pipeline `observability.md`.

## Communication

### Synchronous (in-process calls)

- **Inbound:** `Serialize(CombatState, RunRngSet, PlayerRngSet, TokenMap, ManifestStamp) → byte[]` from M2 (responding to save_state hook), M4 (control-plane save_state RPC), M3 (replay checkpoint creation), M9 (graceful-shutdown snapshot).
- **Inbound:** `Deserialize(ReadOnlySpan<byte>) → StateBlob` from M2 (load_state hook), M4 (control-plane load_state RPC), M3 (replay checkpoint replay), M9 (resume from snapshot at startup).
- **Inbound:** `CanonicalHash.Sha256Hex(blob) → string` (Domain layer) for the determinism probe and Q2 oracle-agreement signalling.
- **Outbound:** calls into per-section codecs owned by M6a / M6b / M5 / M6c / M6d via codec interfaces. M1 does not know each section's internal layout — it knows the section ordering and the version-to-decoder mapping.

### Asynchronous

- None.

### Events emitted

- None.

## Coupling

- **Afferent (in):** M2 (hook protocol save/load), M3 (replay checkpoints), M4 (control RPC save/load), M5 (canonical hash), M9 (process boot snapshot), Q2 oracle adapter (cross-quantum, via `StateBlobEnvelope`).
- **Efferent (out):** M5 (`IRngStateSerializer`), M6a / M6b / M6c / M6d (per-section codec interfaces).
- **Indirect:** M7 Content Catalog (Q4 token-registry SHA stamped into manifest).

Aim: M1 knows nothing about IPC, files, or the network. It operates on byte arrays. M2 / M3 / M4 do the actual transport.

## Testing Strategy

### Unit Tests

Live under `test/Sts2Headless.Tests.Adapters/StateCodec/`. Mock M5, M6a, M6b, M6c, M6d codec interfaces with simple deterministic stubs. Focus on schema versioning and roundtrip:

- **Bit-identical roundtrip:** for a fixture state, `Serialize` produces byte sequence `B1`. `Deserialize(B1) → state2`. `Serialize(state2) → B2`. Assert `B1 == B2` byte-for-byte. Cover state combinations: empty combat, combat with full hand, combat with stacked powers, run state with full deck.
- **Schema version rejection:** craft a blob with state-schema-version = MAX+1; assert deserialize throws `StateCodecException` with explicit message; no silent coercion.
- **Schema version migration (forward-compat additive):** craft a blob with version N missing field added in version N+1; assert deserialize succeeds with field at documented default. *(Phase 3+ scope; not in Phase 1.)*
- **Schema version migration (structural change):** craft a blob with version N where version N+1 changed field layout; assert deserialize requires explicit migrator (not auto-promote).
- **Manifest stamping:** every `Serialize` call stamps a manifest with current versions; `Deserialize` reports the stamped manifest before decoding.
- **Manifest mismatch:** deserialize a blob whose manifest's GitSha differs from the running process's; assert explicit `ManifestMismatch` warning logged but deserialization proceeds (manifest is informational at the codec level; M2's session handshake enforces equality).
- **SHA-256 trailer integrity:** corrupt one byte in the middle of a blob; assert deserialize fails on trailer-mismatch; do not return a partially-decoded state.
- **Canonical hash determinism:** `CanonicalHash.Sha256Hex(state) == CanonicalHash.Sha256Hex(Serialize(Deserialize(Serialize(state))))`. Hash is independent of process state (no pointer addresses).
- **Polymorphic per-content section:** a `CardModel` subclass with per-instance state roundtrips through the polymorphic codec; class-id discrimination resolves correctly.

### Integration Tests

Verify M1's quantum boundaries:

- **End-to-end roundtrip with all modules:** drive Q1 through 100 random actions, snapshot state, restart process, deserialize, drive same actions, assert state hashes match at every step.
- **M2 save/load roundtrip:** invoke M2's save_state hook; receive blob; pass to a fresh Q1 instance via M2's load_state; verify state continues identically.
- **M4 save/load roundtrip:** analogous for control-plane RPC.
- **M3 replay checkpoint:** M3 emits a replay with embedded checkpoint blobs; replay tool deserializes checkpoints; verifies state at each checkpoint matches a recorded golden hash.
- **CI bit-identical-roundtrip gate:** the bit-identical roundtrip test runs on every commit; failure blocks merge per pipeline `scaling-strategy.md` §4.1 #4.
- **Cross-version compatibility matrix:** for the last N supported state schema versions, assert that a blob written by version N can be read by version N (sanity), and that version N+1 either accepts or rejects with explicit migrator-required error (not silent corruption).

### Q2 handoff fixture corpus

The Q1→Q2 adapter regression set lives at `engine/headless/test/fixtures/state-blobs/`. Each fixture is a subdirectory containing:

- `metadata.json` — keys: `seed`, `encounter_id`, `role`, `expected_canonical_hash_hex`, `blob_bytes`.
- `state.blob` — bytes from `StateCodec.Serialize` against Q1's M1 at simulator boot for the (seed, encounter) pair.

The corpus is reproducible: re-running the fixture generator against a clean Q1 boot must produce byte-identical `state.blob` files and matching canonical hashes. A regression test asserts the `expected_canonical_hash_hex` recorded in each `metadata.json` matches what `CanonicalHash.Sha256Hex` produces on the blob bytes. See the corpus `README.md` for per-fixture role + Q2 implications.

Debugging the corpus: `tools/StateBlobDumper` accepts a `.blob` path and prints
the envelope + per-section pretty-print to stdout. Use it whenever a Q2
adapter mismatch surfaces — diff the dumper output against the matching
Q2-side decode.

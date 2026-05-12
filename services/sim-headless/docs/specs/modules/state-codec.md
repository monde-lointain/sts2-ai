# Module: State Codec (M1)

> Versioned binary serializer for `(CombatState, RunState, RngBundle)`. Bit-identical roundtrip is the contract. Owner of the Game Version Manifest stamped onto every blob and replay.

## Responsibilities

- Serialize and deserialize the full Q1 state: `CombatState` (M6a-owned), `RunState` (M6b-owned), `RngBundle` (M5-owned, opaque per Q1-ADR-003), per-content state on M6c instances (polymorphic via class-id discrimination), and `M6d.ActionQueue` + `HookRegistry`.
- Stamp every blob with the Game Version Manifest: `(STS2 source SHA, mod SHA, state schema version, Q4 token-registry SHA, build hash, M5 RNG schema version, M2 hook protocol version)` per pipeline ADR-005 and Q1-ADR-005.
- Validate version on deserialization: reject blobs with incompatible state schema version with an explicit `SchemaVersionError`. **Never silently coerce.**
- Guarantee bit-identical roundtrip: `Serialize(Deserialize(Serialize(state))) == Serialize(state)` byte-for-byte, enforced by CI per pipeline `scaling-strategy.md` §4.1 #4.
- Provide a canonical state hash (`uint64`) used by the determinism probe (Q1-ADR-007) and by Q2's oracle-agreement signal.
- Provide schema migration tooling for forward-compat reads where possible (additive fields tolerated; structural changes require explicit migration).

`[Phase 1 scope]` — full M1 functional. Combat-only blobs serialized; M6b state stubbed in the blob (placeholder fields).

`[Phase 2]` — full `RunState` serialized. Schema version 2.

`[Phase 3+]` — schema migrations between versions become part of the patch-adaptation workflow.

Out of scope: the actual state types (M6a / M6b / M6c / M6d / M5 own those); IPC framing (M2); replay file format (M3 — but M3 embeds M1 blobs as checkpoints).

## Data Ownership

M1 owns the **versioned binary state schema** (Q1's load-bearing data contract) and the **Game Version Manifest schema**.

- **Binary state schema** — sectioned format:
  ```
  [Manifest section: GameVersionManifest]
  [State schema version: u32]
  [CombatState section: variable-length, M6a-defined]
  [RunState section: variable-length, M6b-defined]
  [RngBundle section: opaque bytes from M5, with M5's version stamped]
  [M6c per-content state: polymorphic, class-id discriminated]
  [ActionQueue + HookRegistry section: M6d-defined]
  [Trailer: SHA-256 of all above for integrity check]
  ```
- **`GameVersionManifest`** — stamped at top of every blob; immutable post-creation.
- **Schema version registry** — list of supported schema versions; mapping from version to deserializer impl.

The schema is **not** JSON or any human-readable format. Binary, fixed-endianness (little-endian), aligned per type. Field ordering is part of the schema and changes require version bumps.

## Communication

### Synchronous (in-process calls)

- **Inbound:** `Serialize(CombatState, RunState, …) → byte[]` from M2 (responding to save_state hook), M4 (control-plane save_state RPC), M3 (replay checkpoint creation), M9 (graceful-shutdown snapshot).
- **Inbound:** `Deserialize(byte[]) → (CombatState, RunState, …)` from M2 (load_state hook), M4 (control-plane load_state RPC), M3 (replay checkpoint replay), M9 (resume from snapshot at startup).
- **Inbound:** `CanonicalHash(state) → uint64` from M5 (for the determinism probe), M9 (for telemetry).
- **Outbound:** calls into per-section codecs owned by M6a / M6b / M5 / M6c / M6d via codec interfaces. M1 does not know each section's internal layout — it knows the section ordering and the version-to-decoder mapping.

### Asynchronous

- None.

### Events emitted

- None.

## Coupling

- **Afferent (in):** M2 (hook protocol save/load), M3 (replay checkpoints), M4 (control RPC save/load), M5 (canonical hash), M9 (process boot snapshot).
- **Efferent (out):** M5 (`IRngStateSerializer`), M6a / M6b / M6c / M6d (per-section codec interfaces).
- **Indirect:** M7 Content Catalog (Q4 token-registry SHA stamped into manifest).

Aim: M1 knows nothing about IPC, files, or the network. It operates on byte arrays. M2 / M3 / M4 do the actual transport.

## Testing Strategy

### Unit Tests

Mock M5, M6a, M6b, M6c, M6d codec interfaces with simple deterministic stubs. Focus on schema versioning and roundtrip:

- **Bit-identical roundtrip:** for a fixture state, `Serialize` produces byte sequence `B1`. `Deserialize(B1) → state2`. `Serialize(state2) → B2`. Assert `B1 == B2` byte-for-byte. Cover state combinations: empty combat, combat with full hand, combat with stacked powers, run state with full deck.
- **Schema version rejection:** craft a blob with state-schema-version = MAX+1; assert deserialize throws `SchemaVersionError` with explicit message; no silent coercion.
- **Schema version migration (forward-compat additive):** craft a blob with version N missing field added in version N+1; assert deserialize succeeds with field at documented default.
- **Schema version migration (structural change):** craft a blob with version N where version N+1 changed field layout; assert deserialize requires explicit migrator (not auto-promote).
- **Manifest stamping:** every `Serialize` call stamps a manifest with current versions; `Deserialize` reports the stamped manifest before decoding.
- **Manifest mismatch:** deserialize a blob whose manifest's STS2 SHA differs from the running process's; assert explicit `ManifestMismatch` warning logged but deserialization proceeds (manifest is informational at the codec level; M2's session handshake enforces equality).
- **SHA-256 trailer integrity:** corrupt one byte in the middle of a blob; assert deserialize fails on trailer-mismatch; do not return a partially-decoded state.
- **Canonical hash determinism:** `CanonicalHash(state) == CanonicalHash(Deserialize(Serialize(state)))`. Hash is independent of process state (no pointer addresses).
- **Polymorphic per-content section:** a `CardModel` subclass with per-instance state roundtrips through the polymorphic codec; class-id discrimination resolves correctly.

### Integration Tests

Verify M1's quantum boundaries:

- **End-to-end roundtrip with all modules:** drive Q1 through 100 random actions, snapshot state, restart process, deserialize, drive same actions, assert state hashes match at every step.
- **M2 save/load roundtrip:** invoke M2's save_state hook; receive blob; pass to a fresh Q1 instance via M2's load_state; verify state continues identically.
- **M4 save/load roundtrip:** analogous for control-plane RPC.
- **M3 replay checkpoint:** M3 emits a replay with embedded checkpoint blobs; replay tool deserializes checkpoints; verifies state at each checkpoint matches a recorded golden hash.
- **CI bit-identical-roundtrip gate:** the bit-identical roundtrip test runs on every commit; failure blocks merge per pipeline `scaling-strategy.md` §4.1 #4.
- **Cross-version compatibility matrix:** for the last N supported state schema versions, assert that a blob written by version N can be read by version N (sanity), and that version N+1 either accepts or rejects with explicit migrator-required error (not silent corruption).

# Module: Replay Recorder (M3)

> Replay file emission: `(seed, action_sequence, optional checkpoints, schema_version, manifest)`. Async filesystem sink. Consumed by Q3 (Experience Store ingest), Q12 (Eval Harness pinned-seed runs), and debugging tools.

## Responsibilities

- Record the action sequence applied to Q1 from process start (or from explicit `begin_replay` RPC) to process exit (or `end_replay`). Each action recorded with its enqueue context (which module enqueued it, which decision-id triggered it).
- Optionally embed M1 state-blob checkpoints at configured intervals (e.g., every N actions, every combat boundary, on demand from M4).
- Stamp every replay file with the Game Version Manifest (provided by M1).
- Write replay files to a configured filesystem directory (path supplied by M9 from config). One file per replay session; naming includes seed and timestamp.
- Provide a replay-reader companion API (used by Q12, debugging, and the determinism probe in Q1-ADR-007) that decodes a replay file back into an action sequence + checkpoint list.
- Be tolerant of disk pressure: backpressure-resistant via a bounded buffered writer; if the disk falls behind, drop the *oldest* replay's tail and log; never block the decision path.

`[Phase 1 scope]` — minimal replay: seed + action sequence + manifest. No checkpoints (full state is small enough that re-deriving from seed + actions is the test). Combat-only.

`[Phase 2]` — full-run replays; checkpoints at room boundaries.

`[Phase 3+]` — counterfactual rollout replays: replays from a saved state with an alternative action sequence. Replay format extended to encode `(start_state_checkpoint, alternative_action_sequence)`.

Out of scope: state encoding (M1); IPC (M2); RPC (M4); the action queue itself (M6d).

## Data Ownership

M3 owns the **replay file format** — Q1's third versioned data contract.

- **Replay file structure:**
  ```
  [Header section: replay schema version (u32), manifest (M1-defined), session_id (uuid)]
  [Seed section: per-subsystem RNG seeds (M5-defined)]
  [Action sequence: stream of (decision_id: u64, action_index: u32, action_payload: bytes)]
  [Checkpoint table (optional): list of (decision_id, file_offset, state_blob)]
  [Trailer: SHA-256 of all above]
  ```
- **Replay file naming:** `{session_id}_{seed}_{timestamp_unix}.replay` (timestamp from a deterministic clock relative to action count, not wall clock).
- **Replay schema version** — independent of M1 state schema version; bumped when format changes (e.g., adding checkpoint table support is a bump).

Files are append-only during a session; finalized (trailer SHA written) on session close.

## Communication

### Synchronous (in-process calls)

- **Inbound:** action-recording calls from M6d after each action resolves: `Record(decision_id, action_index, action_payload)`.
- **Inbound:** checkpoint requests from M4 (control plane) or from an internal scheduler at configured intervals: `Checkpoint(state_blob)`.
- **Inbound:** `BeginReplay(session_id, seed, manifest)` and `EndReplay()` lifecycle calls from M9.
- **Outbound:** writes to filesystem (the only Q1 module with substantive disk IO).
- **Outbound:** state-blob serialization via M1 for checkpoints.

### Asynchronous

- File flushing is buffered and asynchronous **off the decision path**, on M9's utility thread (per Q1-ADR-008). The decision path enqueues records to a lock-free producer-consumer ring; the utility thread drains and writes.

### Events emitted

- Filesystem writes (filesystem is the consumer transport).
- Disk-pressure metrics to M9 telemetry.

## Coupling

- **Afferent (in):** M6d Action Queue (action-record calls); M4 Control Plane (checkpoint requests, replay lifecycle); M9 Process Host (init, config, lifecycle).
- **Efferent (out):** M1 State Codec (serialize checkpoints); filesystem.
- **Indirect:** Q3 Experience Store (ingest replays); Q12 Eval Harness (pinned-seed verification); debugging tools.

Aim: M3 does not import M6a / M6b / M6c. It is a pure-output adapter.

## Testing Strategy

### Unit Tests

Mock M1, M9 config, filesystem (in-memory fake). Focus on file format and recorder lifecycle:

- **File format roundtrip:** record N actions; finalize; reader decodes; assert action sequence identical.
- **Checkpoint encoding:** record actions interspersed with checkpoints; reader produces both action sequence and checkpoint list; checkpoints reference correct decision_ids.
- **Manifest stamping:** every replay file's header contains the manifest passed at `BeginReplay`.
- **Session_id uniqueness:** two `BeginReplay` calls with no shared session_id produce different files.
- **Trailer SHA integrity:** finalized file's trailer SHA matches recomputed SHA over all preceding bytes; corrupt one byte → reader rejects.
- **Schema version negotiation:** a reader with schema version V1 on a file written at V2 produces an explicit `UnsupportedReplayVersion` error.
- **Backpressure behavior:** simulate slow disk; verify producer never blocks the decision path; oldest-first drop policy logged.
- **Append-only invariant:** writing to a finalized file throws.

### Integration Tests

Verify M3's quantum boundaries:

- **Replay-as-truth:** record a session through M3; replay the actions through a fresh Q1 instance keyed off the recorded seed; verify final state matches via `M1.CanonicalHash`.
- **Q3 ingest contract:** Q3's mock ingest reads M3 output; assert the parsed action sequence matches the original recording byte-for-byte.
- **Q12 pinned-seed contract:** for the Phase-1 reference encounter, replay produces identical state to the live run on the same seed.
- **Differential vs Godot:** the determinism probe (Q1-ADR-007) reads a replay; replays it against unmodified Godot; per-step state hashes match Q1's.
- **Disk pressure under load:** running Q1 at full hot-path throughput while recording, p99 disk-flush latency does not affect decision-path latency. M9 telemetry separates the two.
- **Long-session integrity:** replay a 1-hour session; verify file remains under disk-space budget; verify replay-reader can stream-decode without loading entire file.

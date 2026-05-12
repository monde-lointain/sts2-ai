# Module: Control Plane (M4)

> Cold-path RPC for orchestration: `{load_state, save_state, set_seed, step_until_decision, terminate, begin_replay, end_replay, request_checkpoint}`. JSON-over-Unix-socket. Used by Q11 (Curriculum Generator), Q12 (Eval Harness), debugging tools. Not latency-critical.

## Responsibilities

- Listen on a Unix-domain socket configured by M9; accept one client connection at a time (single-orchestrator pattern).
- Frame request/response messages as line-delimited JSON. Every request includes `request_id`, `method`, `params`; every response includes `request_id`, `result` or `error`.
- Implement RPC methods:
  - `load_state(blob)` — deserialize via M1, install into M6a / M6b, reset M6d action queue.
  - `save_state()` → `blob` — serialize via M1.
  - `set_seed(subsystem, seed)` — reseed via M5.
  - `step_until_decision()` → `(state_blob, legal_action_mask, decision_kind)` — drain M6d to next decision boundary, return state.
  - `apply_action(action_index, action_payload)` — validate via M6d, apply.
  - `terminate()` — request graceful shutdown via M9.
  - `begin_replay(session_id, seed, manifest)` / `end_replay()` — toggle M3 replay recording.
  - `request_checkpoint()` — instruct M3 to embed a checkpoint at the next action boundary.
- Validate inputs at the protocol boundary: reject malformed JSON, unknown methods, parameter type mismatches.
- Emit per-RPC structured logs to M9 for observability; not latency-tracked (cold path).
- Be robust to client disconnect: socket close mid-session leaves Q1 in a valid state; next client can reconnect and resume.

`[Phase 1 scope]` — `save_state`, `load_state`, `set_seed`, `step_until_decision`, `apply_action`, `terminate`. Replay control deferred.

`[Phase 2]` — replay control RPCs added.

`[Phase 3+]` — `clone_state` RPC for branchable rollout (returns a state handle the orchestrator can resume into in a separate Q1 process).

Out of scope: hot-path IPC (M2 owns that); replay file format (M3 owns); state schema (M1 owns); domain logic (M6a / M6b / M6c / M6d).

## Data Ownership

M4 owns the **control RPC schema** — Q1's fourth versioned data contract.

- **Wire format:** line-delimited JSON. Each line is one message.
- **Request schema:** `{request_id: int, method: string, params: object}`.
- **Response schema:** `{request_id: int, result: object} | {request_id: int, error: {code: int, message: string}}`.
- **Method registry:** stable method names; new methods additive; deprecation requires version negotiation at session start.
- **RPC schema version:** included in initial handshake (`hello` method); version negotiation on connect.

JSON is acceptable here (cold path; human-readable beats binary efficiency). State blobs in `load_state` / `save_state` are base64-encoded byte arrays — M1 schema applies inside.

## Communication

### Synchronous (in-process calls)

- **Inbound:** Unix-socket connections from Q11 / Q12 / debug tools.
- **Outbound:** state serialization via M1; reseed via M5; queue/decision operations via M6d; replay lifecycle via M3; lifecycle / shutdown via M9.

### Synchronous (cross-process)

- **Q11 / Q12 / debug ↔ Q1 over Unix socket.** Single concurrent client; serial RPC.
- Latency unconstrained — orchestration is human-scale or batch-scale.

### Asynchronous

- None.

### Events emitted

- Structured RPC logs to M9.

## Coupling

- **Afferent (in):** M9 Process Host (binds the listening socket, runs the accept loop).
- **Efferent (out):** M1 State Codec (serialize/deserialize); M5 Determinism Kernel (reseed); M6d Action Queue (step / apply); M3 Replay Recorder (replay control); M9 Process Host (terminate signal).
- **External:** Q11 / Q12 / debug tools (cross-process clients).

Aim: M4 does not import M6a / M6b / M6c directly. State access is via M1; queue control is via M6d.

## Testing Strategy

### Unit Tests

Mock M1, M5, M6d, M3, M9, the socket transport (in-process fake). Focus on RPC dispatch and validation:

- **Method dispatch:** each method routes to the correct handler; unknown method returns `error: {code: 405}`.
- **Parameter validation:** `set_seed` with non-int seed → `error: {code: 400}`; `apply_action` with out-of-range action_index → 400; missing-required-param → 400.
- **Request-id round-trip:** request_id=42 yields response with request_id=42; serial ordering preserved (RPCs handled FIFO).
- **save_state / load_state:** save returns a base64 blob; load with that blob restores state; cross-call equivalence holds.
- **set_seed propagation:** `set_seed` invokes M5's reseed; subsequent randomness reflects new seed.
- **step_until_decision contract:** returns immediately if already at decision boundary; otherwise drains M6d and returns when next boundary reached.
- **apply_action gating:** `apply_action` after `step_until_decision` advances state; without preceding `step_until_decision`, returns `error: {code: 409}` (no pending decision).
- **terminate flow:** `terminate` returns `result: ok` then closes the socket and triggers M9 graceful shutdown.
- **Schema version negotiation:** client `hello` with version V1 (Q1 supports V1+V2) connects at V1; client `hello` with unsupported version → connection refused with explicit error before any RPC accepted.
- **Disconnect mid-session:** client closes after partial-write of a request; Q1 logs `client_disconnected_mid_request`; state remains valid; next connect succeeds.

### Integration Tests

Verify M4's quantum boundaries:

- **Q11 / Q12 contract test:** a mock Q11 client drives a full save → reseed → step → apply → save flow; final state matches expected.
- **load → resume contract:** save state mid-combat via M4; spawn fresh Q1 process; load state via M4; step + apply matches reference.
- **State-codec contract:** state blobs returned by `save_state` round-trip cleanly through M1 (cross-process and within-process).
- **Replay control roundtrip:** `begin_replay` → ~50 actions → `end_replay`; assert M3 emits a complete replay file with manifest and trailer.
- **Cross-version client compatibility:** an old Q12 client speaking V1 against a V2 Q1 negotiates V1 and operates correctly. A V2 client against a V1 Q1 fails at handshake.
- **Concurrent-client rejection:** second client connecting while first is still attached is rejected with explicit error; first client's state is unaffected.

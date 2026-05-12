# Module: Hook Protocol Adapter (M2)

> The hot-path IPC contract with Q8 Rollout Workers. Shared-memory ring buffer + two semaphores. Per-decision target <500µs (pipeline ADR-005). Versioned message schema co-versioned with M1's state schema (Q1-ADR-005).

## Responsibilities

- Establish IPC session with paired Q8 worker process: shared-memory segment allocation, ring buffer init, semaphore handshake, manifest exchange.
- Frame request/response messages: each request carries a request-id and message type; each response includes corresponding request-id, state blob, legal-action mask, optional metadata.
- At every player-decision boundary (signaled by M6d yielding), serialize current state via M1, compute legal-action mask via M6d, emit the decision request to Q8, await action response, validate the action via M6d, hand action to M6d for application.
- Implement protocol message types: `decision_request`, `decision_response`, `save_state_request`, `save_state_response`, `load_state_request`, `load_state_response`, `terminate`, `error`.
- Validate Q8-supplied actions: reject any action not in the legal mask; reject malformed messages with explicit error code.
- Emit per-decision latency metrics (request-received → action-applied) to M9's telemetry endpoint.
- Reject session-establish if Q8's manifest does not match Q1's exactly (Q1-ADR-005). **Never silently coerce.**
- Be robust to Q8 crash: if the worker process dies (semaphore timeout), Q1 logs and exits; the supervisor restarts both per pipeline ADR-005.

`[Phase 1 scope]` — combat-decision messages only. Card-pick / map / shop / event / rest / potion message types defined in schema but unused.

`[Phase 2]` — all run-level decision message types active.

`[Phase 3+]` — branchable rollout messages: `clone_state_request` (returns a state-handle Q8 can resume into).

Out of scope: state encoding (M1 owns the state schema, M2 just transports the blob); replay emission (M3); cold-path RPC (M4); the actual game logic (M6a / M6b / M6c / M6d).

## Data Ownership

M2 owns the **hook protocol message schema** — the wire format between Q1 and Q8. Versioned. Co-versioned with M1's state schema per Q1-ADR-005.

- **Message envelope:** `(request_id: u64, message_type: u16, body_length: u32)` + body.
- **Message types:** enum stable across versions; new types appended; deprecation requires major bump.
- **`decision_request` body:** `(state_blob: bytes, legal_action_mask: bitset, decision_kind: u8, manifest: GameVersionManifest)`.
- **`decision_response` body:** `(action_index: u32, action_payload: bytes)`.
- **`save_state_response` body:** `(state_blob: bytes, manifest: GameVersionManifest)`.
- **`error` body:** `(error_code: u16, message: utf8 string)`.
- **Schema version negotiation:** session-establish message includes schema version; mismatch terminates session.

The wire format is **not** JSON. Binary, little-endian, fixed offsets where possible to permit zero-copy reads from the ring buffer. State blobs are opaque bytes from M2's perspective — M1's schema applies inside.

## Communication

### Synchronous (in-process calls)

- **Inbound:** "drain to next decision" call from M9's main loop after process boot.
- **Outbound:** legal-action enumeration via M6d; serialize state via M1; deserialize action; validate action via M6d; apply action via M6d.

### Synchronous (cross-process IPC)

- **Q8 ↔ Q1 over shared-memory ring buffer + two semaphores.** Wire format is M2's owned schema.
- Q1 produces decision requests; Q8 produces action responses. Both use same ring buffer; semaphores discriminate direction.
- Per-decision latency target <500µs (pipeline ADR-005).

### Asynchronous

- None. Even though the worker is "async" from Q1's main-loop perspective, the IPC handshake is synchronous — Q1 blocks on the response semaphore.

### Events emitted

- Latency metrics to M9 (Prometheus histograms).
- Session-establish / session-terminate logs to M9.

## Coupling

- **Afferent (in):** M9 Process Host (constructs M2 at boot, runs the main loop).
- **Efferent (out):** M1 State Codec (serialize state); M6d Action Queue (legal-action enum, action validation, action application); M9 (telemetry).
- **External:** Q8 Rollout Worker process (shared-memory peer).

Aim: M2 does not import M6a / M6b / M6c. It interacts with the domain only through M6d and M1.

## Testing Strategy

### Unit Tests

Mock M1, M6d, the ring buffer (in-process fake). Focus on message framing and protocol logic:

- **Message envelope framing:** serialize a request; deserialize back; verify all fields recovered.
- **Request-id round-trip:** request with id=42 receives response with id=42; mismatched-id response is rejected with `IllegalResponse`.
- **Ring buffer wrap:** fill buffer past wrap-point; verify producer waits on consumer; consumer reads correctly across wrap.
- **Semaphore handshake:** producer signals consumer; consumer wakes, reads, signals back; producer wakes. Verify no deadlock.
- **Manifest mismatch:** session establish with mismatched manifest; verify session refuses with explicit error code; no decision request sent.
- **Schema version negotiation:** Q8 advertises version V1; Q1 supports V1+V2; session establishes at V1. Q8 advertises V0 (Q1 dropped support); session refused.
- **Legal-action mask:** generate mask from a mocked combat state; verify expected legal cards × targets are set.
- **Illegal-action rejection:** Q8 sends action not in mask; M2 sends `error` response; M6d not invoked.
- **Malformed message rejection:** Q8 sends message with `body_length` exceeding ring-buffer capacity; M2 logs and terminates session.
- **Crash detection:** Q8 fails to signal within timeout; M2 raises `WorkerStalled`; M9 logs and process exits.

### Integration Tests

Verify M2's quantum boundaries:

- **End-to-end IPC roundtrip:** spawn Q1 + a mock Q8 process pair; send 100 random actions through M2; verify Q1 state matches a no-IPC reference run.
- **Latency budget:** p50 / p99 per-decision latency under realistic load. p99 must be <500µs (pipeline ADR-005). Tracked via Prometheus histogram + CI assertion.
- **State-codec contract:** save_state response from M2 contains a blob that M1 deserializes correctly; round-trip identity holds.
- **Manifest co-version with M1:** changing M1's state schema version requires bumping M2's schema version (Q1-ADR-005); CI cross-check asserts consistency.
- **Crash recovery:** kill Q8 mid-session; verify supervisor restart-both-processes flow brings the pair back up cleanly; no leaked shared-memory segment.
- **Worker contract test (vs Q8 reference impl):** a mock Q8 implemented to the spec drives a full Phase-1 combat to completion; final state matches reference.
- **Schema deprecation flow:** a deprecated message type is sent by an old Q8 build; Q1 returns `error: deprecated` and refuses; old build cannot silently use the new server.

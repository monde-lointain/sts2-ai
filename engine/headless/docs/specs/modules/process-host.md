# Module: Process Host (M9)

> Top-level entry point and composition root for the hexagonal architecture. Configuration loading, signal handling, supervisor integration, structured logging, Prometheus pull endpoint for Q7. Wires every other module together at startup.

## Responsibilities

- Parse and validate process configuration: seed, character/encounter selection (Phase 1) or run-start parameters (Phase 2+), socket paths for M2 / M4, replay directory, Q4 manifest path, log level. Configuration sources: command-line flags, env vars, optional config file.
- Construct every other module in dependency order at startup: M5 (Determinism Kernel), M7 (Content Catalog) → M6c, M6d, M6a, M6b → M1 (State Codec) → M3, M2, M4 → ready.
- Wire ports to adapters: bind `IActionProvider` to M2 (production) or M4 (orchestrated); `IPersistenceCodec` to M1; `IReplaySink` to M3; `IClock` and `IRngSource` to M5; `IContentProvider` to M7. M8 stubs are wired here too (T2 substitutions per Q1-ADR-004).
- Run the main loop: drive M6d to drain to the next decision; hand off to M2 (or M4 in orchestrated mode); apply returned action; loop. Single-threaded (per Q1-ADR-008).
- Run a separate utility thread for IO-y background work: M3 disk flush, Prometheus scrape responses, log flush. Does not touch domain state.
- Handle signals: `SIGTERM` triggers graceful shutdown (drain in-flight action, finalize replay, close M2 session, exit). `SIGINT` same. `SIGUSR1` triggers a Prometheus-style snapshot dump.
- Expose Prometheus pull endpoint: HTTP server on a configured port, scraped by Q7. Metrics exported by every module accumulate here.
- Emit structured logs (JSON-lines to stderr by default): module-level events, error conditions, lifecycle transitions.
- Integrate with the supervisor: register PID, respond to supervisor health-checks via a tiny TCP heartbeat or via Prometheus health metric. On any unhandled exception, emit a final log line with full diagnostics, then exit non-zero — supervisor restarts both Q1 and Q8 per pipeline ADR-005.

`[Phase 1 scope]` — combat-only entry point: `(seed, character, deck, relics, encounter_id, ascension) → final state`. Direct combat-launch flow.

`[Phase 2]` — full-run entry point: `(seed, character, ascension) → full run`. Run-level launch flow. Optional resume-from-snapshot.

`[Phase 3+]` — counterfactual rollout coordination: spawned-from-saved-state mode where M9 loads a state at boot and resumes from it, supporting Q11 / Q12 batch-orchestration patterns.

Out of scope: business logic of any kind (delegated to M6); IPC / RPC framing (M2 / M4); storage formats (M1 / M3); RNG primitives (M5); content lookups (M7); engine stubbing (M8).

## Data Ownership

M9 does not own a versioned external schema. It uses the standard .NET options pattern for configuration; configuration schema is internal.

- **Configuration record** — strongly-typed C# record bound from CLI flags / env vars / config file. Documented in `--help` output but not a versioned protocol.
- **Process metadata** — PID, start time (deterministic from M5's clock or wall-clock at boot, whichever applies), build hash. Stamped into M1's `GameVersionManifest` at construction time; M9 *provides* this data but M1 *owns* the manifest schema.

No state is persistent in M9 itself.

## Communication

### Synchronous (in-process calls)

- **Inbound:** signal handlers (SIGTERM, SIGINT, SIGUSR1).
- **Outbound:** every other module — M9 is the composition root.

### Synchronous (cross-process)

- **Prometheus scrape (HTTP):** Q7 polls M9's `/metrics` endpoint.
- **Supervisor heartbeat (optional):** small TCP socket the supervisor probes.

### Asynchronous

- Utility thread for replay flush, Prometheus serving, log flush. Decoupled from decision path per Q1-ADR-008.

### Events emitted

- Structured logs (stderr default).
- Prometheus metrics (HTTP scrape).
- Lifecycle telemetry (boot, ready, decision-loop start, decision-loop iter, shutdown).

## Coupling

- **Afferent (in):** OS supervisor (signals, lifecycle); Q7 Observability (Prometheus scrape); operator (CLI invocation).
- **Efferent (out):** every other Q1 module — M9 is the composition root and main loop. M9 imports M1, M2, M3, M4, M5, M6a, M6b, M6c, M6d, M7, M8.
- **External:** filesystem (config file load, replay directory creation); HTTP (Prometheus serving); OS signal interface.

Aim: M9 is the *only* module that imports everything. All other modules have minimal afferent edges as designed.

## Testing Strategy

### Unit Tests

Mock all other modules with port doubles; mock filesystem and signal handlers. Focus on configuration, wiring, and lifecycle:

- **Config parse:** valid CLI invocation produces expected config record; invalid invocation produces helpful error message; missing required flag → exit code 2.
- **Wiring order:** modules are constructed in topological dependency order; assertion that M5 and M7 exist before M6c is constructed; M1 exists before M2 / M3 / M4.
- **Port-to-adapter binding:** in production mode, `IActionProvider` resolves to M2; in orchestrated mode, to M4. Test both modes.
- **Signal handling:** SIGTERM during decision loop sets a shutdown flag; loop exits at next quiescent boundary; in-flight action completes; replay finalized.
- **Unhandled exception path:** simulate exception in M6d; M9 catches at top level, logs full diagnostic, exits non-zero with documented exit code.
- **Prometheus endpoint:** `/metrics` returns a valid Prometheus-text-format response with all module-registered metrics.
- **Log structure:** emitted log lines are valid JSON; required fields (timestamp, level, module, event) present.
- **Composition-root determinism:** for the same configuration record, the wired module graph is identical across boots.

### Integration Tests

Verify M9's quantum boundaries:

- **End-to-end Phase-1 boot:** invoke Q1 with `(seed, character=Silent, deck=starter, relics=[Ring of the Snake], encounter=CULTISTS_NORMAL, ascension=0)`; verify process boots, M2 establishes session with mock Q8, decision loop turns over, terminates cleanly.
- **First-decision turnaround:** time from process exec → first decision request emitted on M2's ring buffer is below documented budget (target: <1s including JIT warmup; cold-path).
- **Prometheus scrape contract:** Q7's mock scraper hits `/metrics`, parses, asserts expected metric families: `q1_decision_latency_microseconds`, `q1_state_serialize_bytes`, `q1_replay_disk_bytes_per_sec`, `q1_stub_hit_total{category=...}`, `q1_action_queue_depth`.
- **Graceful shutdown:** SIGTERM during a Phase-1 combat results in a finalized replay file, a closed M2 session (Q8 sees clean termination), and exit code 0.
- **Supervisor restart loop:** kill Q1 mid-decision; supervisor's restart brings Q1 back; M2 re-establishes a fresh session; replay file from the killed run is intact (or marked `incomplete` in the trailer, depending on whether finalization completed).
- **Unhandled exception telemetry:** trigger an exception via a test hook; assert the final log line contains a full stack trace and is the *last* line written before exit.
- **Config-file vs CLI precedence:** CLI flag overrides config-file value; env var overrides config-file value; CLI flag overrides env var. Documented precedence enforced.

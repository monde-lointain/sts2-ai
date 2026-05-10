# Module: Rollout Workers (Q8)

> Stateless self-play workers. Run MCTS using cached weights; emit trajectories to Q3. The hot path of training-data generation.

## Responsibilities

- Run AlphaZero-style PUCT MCTS over Q1's combat MDP (per ADR-009). At each node, query Q9 for prior + value; use Q1 to expand children.
- Emit `(state, search_policy, search_value)` triples to Q3 as completed trajectories.
- Maintain a local **weight cache** of the current production model artifact (fetched via Q5 → Q9 in-process or from a pinned ONNX file). Refresh on a configured cadence.
- Pull the **Content Registry** (Q4) bundled with the loaded artifact (per ADR-010); use it to translate Q1's internal IDs to token IDs in trajectory output.
- Stateless: any worker can be killed or restarted without affecting any other worker. No per-worker durable data. Cattle, not pets.

Out of scope: model training, inference network internals (delegated to Q9), state schema (delegated to Q1).

## Data Ownership

None persistent. In-memory only:

- Network weight cache (mirror of a Q5 artifact).
- Action enumeration cache for revisited states (transient; cleared per combat).
- MCTS tree (transient; per-decision).
- Loaded Q4 registry (in-memory mirror).

## Communication

- **Sync — IPC to Q1 (hot path):** shared-memory ring buffer per ADR-005. <500µs per decision target.
- **Sync — IPC to Q9 (hot path):** shared-memory request for batched policy/value. Target <50µs round-trip when batches are warm.
- **Async — write to Q3:** trajectory append API. Workers do not block on Q3 acknowledgment beyond local buffer flush.
- **Sync — fetch artifact (cold path):** Q5 fetch on startup and on weight-refresh trigger.
- **Pull — metrics:** Q7 scrapes per-worker throughput, MCTS depth distributions, queue waits.

## Coupling

- **Afferent (in):** none. Nothing depends on workers' internal state.
- **Efferent (out):** Q1, Q3, Q4, Q5 (artifact load), Q9, Q7.
- **Indirect:** worker-level supervisor process (restarts crashed pairs of `worker + Q1` processes).

## Phase Expectations

- **Phase 1.** 64–128 cores total, single-host or small fleet. One Q1 process per worker. MCTS budget tunable up to 1024 sims per decision; default 64–256.
- **Phase 2.** 256–512 cores. Workers handle both combat and run-level decision queries; pull combat policy and meta-policy heads from the same Q5 artifact.
- **Phase 3+.** 1024+ cores. Sharded by character or scenario class. Curriculum scheduler (in Q11) directs worker assignments.

## Open Risks

- **MCTS overhead** dominates throughput if action enumeration or state hashing is slow. Mitigation: cache-friendly data structures; profile early; reuse `CompactState` hashing where applicable.
- **Worker / Q1 process pair crash** can leave shared-memory regions in a corrupted state. Mitigation: supervisor cleans shared memory on pair restart; ring buffer protocol survives partial writes.
- **Stale weights** (refresh interval too long) drag training off-policy. Mitigation: refresh cadence tunable; trainer publishes weights at intervals matched to expected on-policy windows.
- **Hot-spot encounters** in the curriculum oversample a few states. Mitigation: workers periodically randomize encounter selection; Q11 monitors coverage.

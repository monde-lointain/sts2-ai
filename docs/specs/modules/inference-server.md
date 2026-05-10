# Module: Inference Server (Q9)

> Batched policy + value forward passes. Stateless. Lives co-located with worker pools so the Worker↔Q9 hot path stays in shared memory.

## Responsibilities

- Serve the model's `forward(rich_state) → (prior, value)` for the combat policy and (later) the run-level heads.
- **Batch** requests across many workers to keep the GPU saturated. Latency budget per batch is set by the slowest worker waiting for its result; we balance batch size vs. tail latency.
- **Load** the configured model artifact at startup and on configured promotion (per ADR-007). Promotion is a config update, not a Q5-driven push.
- **Default deployment:** in-process inside an inference daemon co-located with worker pools, talking to workers via shared memory. ONNX Runtime engine. (`scaling-strategy.md` §5.1 batched-inference-server pattern.)

Out of scope: training, model storage, deciding which model is current.

## Data Ownership

None persistent.

- In-memory weight cache for the loaded artifact.
- Batch buffers (request and response).
- Optionally: small KV cache for repeated states within an MCTS subtree (not a true cache layer; transient).

## Communication

- **Sync — shared memory from Q8:** request `(batch of RichState)` → response `(batch of (prior, value))`. Target <50µs warm-batch round-trip.
- **Sync — fetch from Q5:** at startup, and when a config update signals a new artifact ID.
- **Pull — metrics:** Q7 scrapes batch sizes, batch latency P50/P95/P99, GPU utilization, queue wait.

## Coupling

- **Afferent (in):** Q8 (workers).
- **Efferent (out):** Q5 (artifact load), Q7 (metrics), Q4 (loaded as part of artifact bundle).
- **Indirect:** GPU device drivers; ONNX Runtime; co-located on the same node as worker pools per `scaling-strategy.md` §5.1.

## Phase Expectations

- **Phase 1.** Single inference server per host serving a 64–128-worker pool. ONNX Runtime + a single GPU. Combat policy only.
- **Phase 2.** Add run-level heads (card-pick, value head). Same artifact, multiple output heads.
- **Phase 3+.** May split into separate Q9 instances per head if heads diverge in latency / throughput characteristics. Multi-GPU per host if a single GPU saturates.

## Open Risks

- **Batch-tail latency.** A single slow request inflates the whole batch's latency. Mitigation: batch timeout; oversize-batch split; limit max batch size.
- **Promotion rollback semantics.** Two Q9 instances can serve different artifacts during a rolling promotion; not a correctness issue but training stats become noisy. Mitigation: A/B harness explicitly tags requests with serving artifact ID.
- **GPU OOM** if input shapes vary. Mitigation: pad-and-mask to fixed shapes; reject requests beyond shape budget.
- **Q9 ↔ Q8 shared-memory protocol drift.** Schema is part of the artifact bundle (RichState contract). Versioning piggybacks on the artifact; mismatched workers fail at startup, not at request time.

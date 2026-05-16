---
quantum: Q9
substrate: pipeline/inference-server/
---

> Status legend: see ADR-023. Section badges = `[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`.

# Module: Inference Server (Q9)

> Batched policy + value forward passes. Stateless. Lives co-located with worker pools so the Worker↔Q9 hot path stays in shared memory.

## Responsibilities

- Serve the model's `forward(rich_state, macro_context) → (prior, combat_outcome_samples, combat_outcome_summary, hp_fraction_aux)` for the combat policy and (later) the run-level heads. Output shape per ADR-014 (samples + summary, HP-fraction aux), input shape per ADR-015 (observable run state + macro_context). Phase-1 inference produces degenerate single-sample outputs while training bootstraps on the scalar HP-fraction; Phase-2+ produces real multi-sample outputs.
- **Filter `SOURCE_PERFECT` fields out of input** per ADR-016: Q1 emits a full state; Q9 reads the observability-regime manifest and includes only `POLICY_VISIBLE` and `BELIEF_SAMPLED` fields in network inputs. The audit trail is logged for Q12 to verify no hidden-state leak.
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

- **Phase 1.** Single inference server per host serving a 64–128-worker pool. ONNX Runtime + a single GPU. Combat policy only (degenerate-sample output bootstrapped from scalar HP-fraction per ADR-014 Phase-1 convention). `macro_context` input may be zero-stub until Phase-2.
- **Phase 2.** Add run-level heads (card-pick, value head). Same artifact, multiple output heads. Combat output transitions to real multi-sample shape; `macro_context` carries v1.1 fields (HP / MaxHP / gold / per-potion-slot shadow prices) derived per ADR-019 (Accepted 2026-05-15) — sp head positioned downstream of the shared run encoder with stop-gradient on the consumption path.
- **Phase 3+.** May split into separate Q9 instances per head if heads diverge in latency / throughput characteristics. Multi-GPU per host if a single GPU saturates.

## Open Risks

- **Batch-tail latency.** A single slow request inflates the whole batch's latency. Mitigation: batch timeout; oversize-batch split; limit max batch size.
- **Promotion rollback semantics.** Two Q9 instances can serve different artifacts during a rolling promotion; not a correctness issue but training stats become noisy. Mitigation: A/B harness explicitly tags requests with serving artifact ID.
- **GPU OOM** if input shapes vary. Mitigation: pad-and-mask to fixed shapes; reject requests beyond shape budget.
- **Q9 ↔ Q8 shared-memory protocol drift.** Schema is part of the artifact bundle (RichState contract). Versioning piggybacks on the artifact; mismatched workers fail at startup, not at request time.

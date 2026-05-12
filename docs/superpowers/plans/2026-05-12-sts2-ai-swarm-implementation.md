# STS2 AI Swarm Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the 12-quantum STS2 RL pipeline, starting with the Phase 1 Silent combat pipeline and reusing the existing C# headless simulator work.

**Architecture:** Keep quantum boundaries from `docs/specs`: schema owners first, stateless services second, integration last. Migrate the existing Q1 simulator from `/home/clydew372/development/projects/cs/sts2-headless` into this repo under `services/sim-headless/`; treat upstream STS2 at `/home/clydew372/development/projects/godot/sts2` as read-only source truth.

**Tech Stack:** C++20/CMake/GoogleTest for Q2 prototype/oracle, C#/.NET for Q1, Python/PyTorch for rollout/training/eval, protobuf schemas, RocksDB hot replay, Parquet cold replay, ONNX Runtime inference, MLflow tracking, Docker Compose local orchestration, Kubernetes Phase 2+ orchestration, Prometheus/Grafana observability.

---

## Orchestrator Rules

- [ ] Use one worktree per parallel agent.
- [ ] Assign disjoint write scopes before dispatch.
- [ ] Dispatch agents by wave; merge only after every wave agent returns `DONE`.
- [ ] After every merge, run affected tests first, then repo-level smoke.
- [ ] Require every agent final report to include changed paths, public API changes, tests run, and risks.
- [ ] Q1 agents must read both `services/sim-headless/` and `/home/clydew372/development/projects/godot/sts2`.
- [ ] Treat upstream STS2 source as read-only.
- [ ] Do not start Phase 2 work until Phase 1 gates pass or an ADR explicitly waives them.

## Phase 0: Repo and Contract Spine

### Task 1: Q1 Migration

**Files:**
- Create/modify: `services/sim-headless/**`
- Read-only source: `/home/clydew372/development/projects/cs/sts2-headless/**`
- Read-only upstream: `/home/clydew372/development/projects/godot/sts2/**`

- [ ] Move the existing headless project into `services/sim-headless/`, preserving `Domain`, `EngineStrip`, `Adapters`, `Host`, tests, probes, analyzer tripwire, docs, and Make targets.
- [ ] Update relative paths in migrated docs/tests/build scripts so the project builds from the new repo location.
- [ ] Keep all references to upstream STS2 pointed at `/home/clydew372/development/projects/godot/sts2`.
- [ ] Add a top-level wrapper target for Q1 CI from this repo.
- [ ] Run: `make -C services/sim-headless ci`
- [ ] Expected: build, tests, and tripwire pass.
- [ ] Commit: `chore(q1): migrate headless simulator into services`

### Task 2: Shared Schemas

**Files:**
- Create: `schemas/q1/hook.proto`
- Create: `schemas/q1/state_blob.proto`
- Create: `schemas/q3/trajectory.proto`
- Create: `schemas/q4/token_registry.proto`
- Create: `schemas/q5/artifact.proto`
- Create: `schemas/q6/eval_report.proto`
- Create/modify: schema codegen config for C#, C++, Python.

- [ ] Add protobuf schemas for Q1 hook protocol, Q1 state blob envelope, Q3 trajectory v0, Q4 token registry, Q5 artifact manifest, and Q6 eval report rows.
- [ ] Generate C#, C++, and Python bindings.
- [ ] Add version compatibility tests: minor additions accepted, major mismatches rejected.
- [ ] Run schema codegen and compatibility tests.
- [ ] Expected: generated bindings compile in all three languages.
- [ ] Commit: `feat(schemas): add pipeline v0 contracts`

### Task 3: Service Skeletons

**Files:**
- Create: `services/experience-store/**`
- Create: `services/model-registry/**`
- Create: `services/inference-server/**`
- Create: `services/rollout-workers/**`
- Create: `services/trainer/**`
- Create: `services/eval-harness/**`
- Create: `services/observability/**`

- [ ] Create minimal service packages with health checks and Prometheus `/metrics` endpoints.
- [ ] Add local config files with explicit ports and data directories.
- [ ] Add smoke tests that start each service and hit health/metrics.
- [ ] Run all skeleton smoke tests.
- [ ] Expected: every service starts locally without requiring external infrastructure.
- [ ] Commit: `chore(services): add pipeline service skeletons`

### Task 4: Phase 1 Content Registry

**Files:**
- Create: `content/registry/phase1-silent.json`
- Create: `content/registry/schema.json`
- Create: `content/registry/tests/**`
- Read: `services/sim-headless/test/fixtures/q4-manifest-phase1.json`
- Read: `/home/clydew372/development/projects/godot/sts2/src/**`

- [ ] Seed registry from the existing Q1 fixture and upstream Silent/cards/relics/enemies.
- [ ] Include stable token IDs, deprecation log, registry manifest, and card DSL stubs.
- [ ] Add tests for duplicate IDs, ID reuse, missing references, deprecation monotonicity, and DSL parse.
- [ ] Run registry tests.
- [ ] Expected: token coherence green.
- [ ] Commit: `feat(content): add phase1 silent token registry`

**Phase 0 Gate**

- [ ] Existing C++ prototype tests pass.
- [ ] Q1 migrated CI passes.
- [ ] Every service skeleton starts.
- [ ] Schema codegen works for C#, C++, Python.
- [ ] Q4 token coherence tests pass.

## Phase 1: Silent Combat Pipeline

### Task 5: Q1 Combat Simulator

**Files:**
- Modify: `services/sim-headless/src/**`
- Modify: `services/sim-headless/test/**`
- Read: `/home/clydew372/development/projects/godot/sts2/src/**`

- [ ] Finish combat-only entrypoint: `(seed, character=Silent, deck, relics, encounter_id, ascension)`.
- [ ] Step until player decision and emit `DecisionRequest` with state blob plus legal actions.
- [ ] Accept `DecisionResponse`, validate action, apply it, and continue.
- [ ] Implement save/restore through the v0 state blob envelope.
- [ ] Emit replay files with seed, actions, checkpoints, and manifest.
- [ ] Run Q1 determinism probe, save/restore tests, and replay reproduction tests.
- [ ] Expected: bit-identical save/restore/action roundtrip and deterministic replay.
- [ ] Commit: `feat(q1): expose combat hook protocol`

### Task 6: Q2 Oracle Adapter

**Files:**
- Modify: `include/sts2/ai/**`
- Modify: `src/ai/**`
- Modify: `tests/ai/**`
- Create: `services/oracle-adapter/**` if a wrapper service/CLI is needed.

- [ ] Preserve current expectimax implementation.
- [ ] Add `verify(state_blob, budget) -> {value, action, expansion_complete}` wrapper.
- [ ] Implement Q1 state blob to `CompactState` adapter behind schema-version checks.
- [ ] Extend pinned seed metadata with encounter/deck identifiers.
- [ ] Run current C++ tests and oracle adapter tests.
- [ ] Expected: current tests green; unknown Q1 schema rejected.
- [ ] Commit: `feat(oracle): verify q1 state blobs`

### Task 7: Q3 Experience Store

**Files:**
- Modify: `services/experience-store/**`
- Use: `schemas/q3/trajectory.proto`

- [ ] Implement RocksDB-backed append for trajectory v0.
- [ ] Implement uniform minibatch sampling.
- [ ] Store provenance: `model_version`, `sampling_mode`, `generator`.
- [ ] Add priority-index placeholder without enabling prioritized sampling yet.
- [ ] Add schema mismatch rejection.
- [ ] Run append/sample/migration tests.
- [ ] Expected: append and sample return schema-valid records; incompatible schema writes fail.
- [ ] Commit: `feat(q3): add hot replay store v0`

### Task 8: Q5 Model Registry

**Files:**
- Modify: `services/model-registry/**`
- Use: `schemas/q5/artifact.proto`

- [ ] Implement local filesystem artifact store.
- [ ] Implement SQLite metadata table.
- [ ] Artifact bundle includes weights/checkpoint placeholder, ONNX placeholder, Q4 registry, provenance manifest.
- [ ] Enforce complete provenance: code SHA, dataset SHA, seed, hyperparameters, registry SHA.
- [ ] Use temp-write plus atomic rename on publish.
- [ ] Add content-hash validation on fetch.
- [ ] Add deletion guard for referenced artifacts.
- [ ] Run publish/fetch/delete-guard tests.
- [ ] Expected: incomplete provenance rejected; fetch validates hash.
- [ ] Commit: `feat(q5): add local model registry`

### Task 9: Q6/Q12 Combat Eval

**Files:**
- Modify: `services/eval-harness/**`
- Create/modify: Q6 report storage under `services/eval-harness/reports/**` or `services/evaluation-reports/**`
- Use: `schemas/q6/eval_report.proto`

- [ ] Implement combat-only eval runner.
- [ ] Fetch target artifact from Q5.
- [ ] Run pinned regression battery.
- [ ] Run held-out seed pool.
- [ ] Call Q2 for oracle agreement on tractable states.
- [ ] Measure inference latency.
- [ ] Write versioned Q6 report rows.
- [ ] Implement `gate_check(artifact_id, phase_1)`.
- [ ] Run eval report and gate-check tests.
- [ ] Expected: gate failure names the failing subcriterion.
- [ ] Commit: `feat(eval): add phase1 combat gate`

### Task 10: Q9 Inference Server

**Files:**
- Modify: `services/inference-server/**`
- Use: `schemas/q5/artifact.proto`

- [ ] Implement ONNX Runtime server shell with deterministic mock model first.
- [ ] Implement shared-memory request/response batch API.
- [ ] Load configured artifact from Q5.
- [ ] Enforce max shape budget and reject oversized requests.
- [ ] Implement batch timeout and max batch size.
- [ ] Add metrics for batch size, batch latency, queue wait, and loaded artifact.
- [ ] Run batching, timeout, shape rejection, and artifact-load tests.
- [ ] Expected: deterministic mock output stable across runs.
- [ ] Commit: `feat(q9): serve batched policy value inference`

### Task 11: RichState Encoding

**Files:**
- Create/modify: shared encoding library under `services/rollout-workers/`, `services/trainer/`, or `services/common/encoding/**`
- Use: `content/registry/phase1-silent.json`

- [ ] Encode hand as ordered card tokens.
- [ ] Encode draw/discard/exhaust as token counts.
- [ ] Encode player scalars and powers.
- [ ] Encode enemy sequence with HP, block, intent, and powers.
- [ ] Encode legal action mask aligned with Q1 legal action list.
- [ ] Make registry mismatch startup-fatal.
- [ ] Add parity tests proving worker and trainer encode the same state identically.
- [ ] Run encoding tests.
- [ ] Expected: unknown token hard-fails.
- [ ] Commit: `feat(encoding): add combat richstate v0`

### Task 12: Q8 Rollout Workers

**Files:**
- Modify: `services/rollout-workers/**`
- Use: Q1 hook API, Q3 append API, Q9 inference API, Q4 registry.

- [ ] Implement worker supervisor that starts one Q1 process per rollout slot.
- [ ] Implement PUCT MCTS with configurable simulation budget.
- [ ] Query Q9 for prior/value at expanded nodes.
- [ ] Append completed trajectories to Q3.
- [ ] Load Q4 from the artifact bundle.
- [ ] Clean shared memory on worker/Q1 restart.
- [ ] Add deterministic mock-model rollout test.
- [ ] Run rollout worker tests.
- [ ] Expected: one seeded rollout creates a valid trajectory v0.
- [ ] Commit: `feat(q8): generate combat rollouts`

### Task 13: Q10 Trainer

**Files:**
- Modify: `services/trainer/**`
- Use: Q3 sampling API, Q5 publish API, RichState encoder.

- [ ] Implement PyTorch combat model skeleton.
- [ ] Implement policy cross-entropy, value MSE, L2 weight decay, and KL penalty.
- [ ] Implement Q3 sampler client.
- [ ] Implement Q5 checkpoint publisher.
- [ ] Stamp artifact provenance with code SHA, dataset SHA, seed, hyperparameters, registry SHA.
- [ ] Add one-minibatch deterministic fixture test.
- [ ] Run trainer tests.
- [ ] Expected: one train step runs and publishes a provenance-complete checkpoint.
- [ ] Commit: `feat(q10): train combat policy from replay`

### Task 14: Observability

**Files:**
- Modify: `services/observability/**`
- Modify: Docker Compose/local orchestration files.

- [ ] Add Prometheus scrape config for Q1/Q3/Q5/Q8/Q9/Q10/Q12.
- [ ] Add Grafana dashboards for steps/sec, decisions/sec, Q9 latency, Q3 ingest/sample, loss, win rate, gate status.
- [ ] Add alerts for determinism failure and replay starvation.
- [ ] Add local Docker Compose stack for Phase 1 services.
- [ ] Run local observability smoke.
- [ ] Expected: dashboards load and scrape targets are up.
- [ ] Commit: `feat(q7): add phase1 observability stack`

### Task 15: End-to-End Vertical Slice

**Files:**
- Modify: integration tests under `tests/`, `services/*/tests`, or `integration/**`.

- [ ] Start local stack.
- [ ] Run one seeded Q1 combat through Q8 with Q9 mock inference.
- [ ] Append trajectory to Q3.
- [ ] Train one Q10 minibatch.
- [ ] Publish artifact to Q5.
- [ ] Run Q12 eval.
- [ ] Write Q6 report.
- [ ] Run vertical slice test.
- [ ] Expected: rollout -> replay -> train -> artifact -> eval report completes.
- [ ] Commit: `test(integration): add phase1 vertical slice`

### Task 16: Performance Baselines

**Files:**
- Create/modify: `benchmarks/**`
- Modify: `services/*/benchmarks/**` as needed.

- [ ] Add benchmark for Q8 to Q1 latency.
- [ ] Add benchmark for Q8 to Q9 latency.
- [ ] Add benchmark for Q9 batch P50/P95/P99.
- [ ] Add benchmark for Q3 ingest/sample throughput.
- [ ] Record non-gating local baseline.
- [ ] Run benchmark suite.
- [ ] Expected: benchmarks complete and emit machine-readable results.
- [ ] Commit: `perf(phase1): add pipeline baselines`

**Phase 1 Gate**

- [ ] Silent A0 normal encounter win rate >= 95% on 10k held-out seeds.
- [ ] Oracle top-1 agreement >= 90% where Q2 solves under 1s.
- [ ] Inference/search latency <100ms per decision at 64 MCTS simulations.
- [ ] Q1 save/restore and replay deterministic.
- [ ] Q3 ingest >= Q10 consumption in steady-state smoke.
- [ ] Docker Compose local stack works.
- [ ] Q6 `phase_1` gate is machine-readable.

## Phase 2: Full Run Card-Pick

### Task 17: Full-Run and Card-Pick Extension

**Files:**
- Modify: `services/sim-headless/**`
- Modify: `schemas/q3/trajectory.proto`
- Modify: `services/trainer/**`
- Modify: `services/experience-store/**`
- Modify: `services/eval-harness/**`
- Create/modify: `services/curriculum-generator/**`

- [ ] Add Q1 full-run entrypoint for `(seed, character=Silent, ascension)`.
- [ ] Add card reward hooks and card-pick decision logging.
- [ ] Add trajectory v1 with run decision tuples.
- [ ] Add prioritized replay in Q3.
- [ ] Add Q10 card-pick head and run-value head; keep combat frozen by default.
- [ ] Add Q11 synthetic mid-run state generation.
- [ ] Add Q12 full-run A0 eval and calibration deciles.
- [ ] Run Phase 2 integration tests.
- [ ] Commit: `feat(phase2): add card pick training loop`

**Phase 2 Gate**

- [ ] Silent A0 full-run win rate >= 70%.
- [ ] Run-value calibration within +/-10% by predicted-value decile.
- [ ] Combat policy degradation on live deck distribution <5%.

## Phase 3: Full Run Planning

### Task 18: Decision-Type Heads and Run Planning

**Files:**
- Modify: `services/sim-headless/**`
- Modify: `services/trainer/**`
- Modify: `services/rollout-workers/**`
- Modify: `services/eval-harness/**`
- Modify: report schemas/storage if needed.

- [ ] Add Q1 hooks for map, shop, event, rest, potion, and reward decisions.
- [ ] Add shared encoder plus decision-type heads.
- [ ] Add Q11 curriculum across decision types.
- [ ] Add Q12 counterfactual map evaluator.
- [ ] Add Q12 exploit detector.
- [ ] Add Q6 full ladder and exploit reports.
- [ ] Run Phase 3 integration tests.
- [ ] Commit: `feat(phase3): add full run planning`

**Phase 3 Gate**

- [ ] Silent A0 win rate >= 85%.
- [ ] Silent A10-A15 win rate >= 70%.
- [ ] Map decision agreement with offline EV oracle >= 80%.
- [ ] Zero unresolved exploit incidents.

## Phase 4: Multi-Character

### Task 19: Multi-Character Generalization

**Files:**
- Modify: `content/registry/**`
- Modify: `services/trainer/**`
- Modify: `services/curriculum-generator/**`
- Modify: `services/eval-harness/**`

- [ ] Expand Q4 registry to all character content.
- [ ] Add character token.
- [ ] Add per-character heads and shared fallback head.
- [ ] Add round-robin curriculum.
- [ ] Add cross-character regression checks.
- [ ] Add per-character ladder reports.
- [ ] Run Phase 4 eval suite.
- [ ] Commit: `feat(phase4): add multi-character training`

**Phase 4 Gate**

- [ ] A0 win rate >= 80% every character.
- [ ] A10 win rate >= 60% every character.
- [ ] No >5% win-rate degradation after adding a character.

## Phase 5: Superhuman and Patch Loop

### Task 20: High-Ascension and Patch Adaptation

**Files:**
- Modify: Q8/Q10/Q11/Q12/Q6/Q4 as needed.

- [ ] Add run-level MCTS hybrids.
- [ ] Add adversarial scenario generation.
- [ ] Add held-out content and OOD combo evals.
- [ ] Add patch adaptation timing reports.
- [ ] Add world model only if simulator throughput or search depth proves it necessary.
- [ ] Run Phase 5 eval suite.
- [ ] Commit: `feat(phase5): add patch adaptation loop`

**Phase 5 Gate**

- [ ] A20 win rate >50% every character.
- [ ] Held-out content win rate within 5% of training distribution.
- [ ] Patch fine-tune <2 weeks.
- [ ] Full-search latency <500ms per decision.

## Acceptance Commands

Use these as the baseline verification set. Agents may add narrower commands inside their own scopes.

```bash
cmake --preset ninja-debug
cmake --build --preset ninja-debug
ctest --preset ninja-debug
make -C services/sim-headless ci
```

Phase 1 local stack command, once Docker Compose exists:

```bash
docker compose -f services/observability/docker-compose.phase1.yml up --build
```

## Assumptions

- Silent is present and is the lead character.
- Existing Q1 headless work is migrated, not discarded.
- No existing internal orchestration exists; use Docker Compose locally and Kubernetes later.
- MLflow is preferred over W&B to keep experiment tracking self-hostable.
- Q4 is file/artifact based, not a live registry service.
- Counterfactual evaluator is observational only.
- Full C++ game reimplementation is deferred unless C# headless profiling proves it necessary.

## Unresolved Questions

None.

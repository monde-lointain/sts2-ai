# 01 — Architectural Decision Log

ADRs that shape `docs/specs/`. Each entry: Title, Status, Context, Decision, Consequences (negatives first).

| # | Title | Status |
|---|---|---|
| ADR-001 | Service-Based Architecture with Event-Driven Pipeline | Accepted |
| ADR-002 | Headless C# Core as Game Simulator | Accepted |
| ADR-003 | Single Shared Token Registry as Patch-Adaptation Lever | Accepted |
| ADR-004 | Two State Representations — CompactState (verifier) and RichState (network) | Accepted |
| ADR-005 | Worker↔Sim Integration via Shared-Memory IPC | Accepted (default) |
| ADR-006 | Experience Store as the Principal Async Backbone | Accepted |
| ADR-007 | Model Registry Separated from Serving Authority | Accepted |
| ADR-008 | Sequential Targeting for Multi-Target Cards | Accepted |
| ADR-009 | AlphaZero at Combat Layer; Hierarchical Heads at Run Level | Accepted |
| ADR-010 | Content Registry Packaged with Model Artifact | Accepted |
| ADR-011 | Oracle Owns the Engine→CompactState Adapter | Accepted |
| ADR-012 | Compute Hosting (cloud vs on-prem) | Deferred |
| ADR-013 | Megacrit Headless / Automation API | Deferred |

---

## ADR-001 — Service-Based Architecture with Event-Driven Pipeline

**Status:** Accepted.

**Context.** 12 quanta with disjoint scaling profiles: workers want hundreds of cheap CPU cores; trainer wants one GPU host; inference wants batched GPU; storage wants NVMe; observability wants its own TSDB. A monolithic process cannot host all of these without wasting 90% of the hardware on whichever component is idle. Microservices are too fine for a research-team headcount and would exceed the Worker↔Inference latency budget (<50µs).

**Decision.** ~10 coarse services aligned to functional domains (see `00-system-overview.md` §4). Async pipeline backbone via Q3 (Experience Store). Sync IPC at the two latency-critical hot paths: Worker↔Sim and Worker↔Inference.

**Consequences.**

- *Negative:* distributed-system overhead from day one — schema versioning, observability, and per-service deploy story are required to even reach a baseline. No "run `python main.py`" mode.
- *Negative:* schema migrations are first-class events. Adding a field to a trajectory or a hook message requires a versioned release, not a Git push.
- *Negative:* two transport modes (sync IPC + async queue) means two failure modes to debug. A worker stall could be either.
- *Negative:* time-to-first-result is later than a monolith would give us; Phase 1 has more plumbing.
- *Positive:* throughput scales horizontally; determinism contained per-service; patch impact bounded to specific quanta; team boundaries map to service boundaries.

---

## ADR-002 — Headless C# Core as Game Simulator

**Status:** Accepted (per project direction).

**Context.** Three options for Q1: (a) headless C# Core with Godot rendering/audio/main-loop stripped; (b) Godot in headless mode; (c) full C++ reimplementation. Strategy doc (§0, §2.3, end-of-doc) recommends (a). Project decision confirms.

**Decision.** Q1 is the C# Core compiled headless, run on the .NET runtime directly. Out-of-tree mod via `Core/Modding` where possible. Full C++ reimplementation reserved as a *targeted* optimization for the combat hot path if and only if profiling demands.

**Consequences.**

- *Negative:* C# GC is the headline throughput risk (`scaling-strategy.md` §5.2 #4). We will hit GC-pause issues and have to pool buffers / use struct types / tune the GC.
- *Negative:* polyglot pipeline (C# + Python + C++) — context-switch tax on engineers; build system must coordinate three toolchains.
- *Negative:* determinism audit must cover C# threading, async/await, and GC timing — easy places for hidden nondeterminism.
- *Negative:* bound to the Godot package's evolution — if a Godot ABI change breaks our headless build on a patch, we react.
- *Positive:* leverages all existing game logic — no reimplementation risk, no card-mechanic divergence, no whole-game test plan to write.
- *Positive:* mod-shaped extension via `Core/Modding` rebases cleanly across STS2 patches.
- *Positive:* `Core/AutoSlay/AutoSlayer.cs` already implies an internal automation harness we can lift.

---

## ADR-003 — Single Shared Token Registry as Patch-Adaptation Lever

**Status:** Accepted.

**Context.** `scaling-strategy.md` §0 risk #1, §2.7, §6.2: content patches threaten model relevance. Without a stable identity for cards/relics/enemies/buffs across patches, every patch resets the agent. Token IDs are the load-bearing primitive for the card-text subnetwork, replay schemas, and shared embeddings.

**Decision.** Q4 (Content Registry) is the canonical, versioned source of all content tokens. Stable IDs for unchanged content; new IDs for additions; deprecation log for removals. Card-text DSL records co-located so the description subnetwork has a single place to read content semantics.

**Consequences.**

- *Negative:* single source of truth = single point of *wrong*. A misnumbered token corrupts every model trained against it. Mitigation: token registry has its own regression battery; schema-versioned releases with reviewer sign-off.
- *Negative:* coordinating registry updates with STS2 patch releases is non-trivial. Forgetting the registry update before training breaks reproducibility — silently if the embedding initialization swallows the divergence.
- *Negative:* registry version becomes part of every model's provenance: `(code, dataset, seed, hyperparameters, token-registry SHA)`. Promotion checks must include token-compat verification.
- *Positive:* makes patch adaptation a bounded engineering task instead of a project-wide retraining event.

---

## ADR-004 — Two State Representations: CompactState and RichState

**Status:** Accepted.

**Context.** `scaling-strategy.md` §1.5 #1, §4.2. Verifier (expectimax) needs hashable POD with TT-friendly equality. Network needs token sequences with embeddings, history features, and variable-arity inputs (deck size, enemy count). Conflating produces a state type that is good at neither.

**Decision.** Keep `CompactState` (existing hashable type, `include/sts2/ai/state.h`) as the verifier-side representation owned by Q2. Introduce `RichState` as the network-input type, derivable from any serialized engine state, owned jointly by Q8 (encoding for inference) and Q10 (encoding for training). `CompactState` is derivable from `RichState` only when the state is small enough to fit the compact schema.

**Consequences.**

- *Negative:* two types means two derivation paths to maintain. A bug in either invalidates the oracle-agreement metric.
- *Negative:* cannot reuse `CompactState`'s TT for the deep network. The TT lives at the search layer; network caching, if any, is separate (see ADR-009 for how this is resolved at search time).
- *Negative:* RichState schema is now a first-class versioned artifact alongside the trajectory schema. Two schema lifecycles.
- *Positive:* verifier stays exact and fast; network sees a representation built for its job; neither pays the other's tax.

---

## ADR-005 — Worker↔Sim Integration via Shared-Memory IPC

**Status:** Accepted (default; reconsidered if profiling demands in-process embedding).

**Context.** Hot path: ~30 player decisions per combat × 10⁵ combats/day/worker target. Two integration modes: (a) in-process, embedding C# in the Python worker via Python.NET; (b) out-of-process, shared-memory IPC across local processes. (a) is faster (nanoseconds) but couples failure modes and complicates debugging. (b) is slightly slower (microseconds) but isolates crashes and lets Q1 evolve independently.

**Decision.** Shared-memory IPC by default. Worker is Python; Sim is a C# process. Communication via a memory-mapped ring buffer with two semaphores. Per-decision target <500µs. The decision is revisited if profiling shows IPC accounts for >10% of worker time.

**Consequences.**

- *Negative:* higher latency than in-process embedding (microseconds instead of nanoseconds). At 30 decisions/combat that is microseconds × 30 = sub-millisecond per combat — acceptable, but real.
- *Negative:* two processes per worker slot; memory budget roughly doubles.
- *Negative:* needs a worker-level supervisor that restarts both processes together on crash.
- *Negative:* shared-memory protocol is its own schema to version (covered by Q1 hook protocol).
- *Positive:* clean process boundary; deterministic bug isolation; can swap Q1 implementation (C# headless ↔ future C++) without touching workers.
- *Positive:* keeps Python worker free of CLR runtime weight.

---

## ADR-006 — Experience Store as the Principal Async Backbone

**Status:** Accepted.

**Context.** Q3 sits between Q8 (writers) and Q10 (reader) operating at very different rates — ~10³:1 in steady state. Either direct RPC (sync) from worker to trainer, or queue-mediated (async) via Q3.

**Decision.** Async via Q3. Workers append trajectories on their own clock; trainer samples on its own. Backpressure via trainer-controlled retention policy. Hot tier RocksDB on local NVMe; cold tier Parquet on S3-equivalent. Schema: versioned protobuf or flatbuffers.

**Consequences.**

- *Negative:* trainer can be sampling stale trajectories if the worker fleet is fast. Off-policy correction is the trainer's burden.
- *Negative:* schema migrations are downtime. In-flight trajectories on the old schema must be drained or migrated; we plan for this.
- *Negative:* two-tier storage adds operational complexity — lifecycle policies, sampling that crosses tiers, cold-tier read latency.
- *Negative:* no end-to-end backpressure from trainer to worker. Workers won't slow down if Q3 is full; we rely on retention to drop oldest.
- *Positive:* standard pipeline pattern; either side scales independently; replay-as-event-log enables retroactive analysis (re-train from same data, replay debugging).

---

## ADR-007 — Model Registry Separated from Serving Authority

**Status:** Accepted.

**Context.** Open question Phase 1: does Q5 also decide which model is "current production" (i.e., what Q9 serves)? Conflating registry and authority simplifies the diagram but couples promotion to storage.

**Decision.** Q5 is a versioned blob store + metadata table only. Q9 reads a configured model URI; promotion is a config update with reviewer sign-off (per `scaling-strategy.md` §5.4 checkpointing). Q5 owns artifacts; the promotion workflow owns "current."

**Consequences.**

- *Negative:* two control planes (registry vs serving config). Possible drift if a checkpoint is deleted from Q5 while still referenced by Q9 config — needs a deletion guard ("can't delete if referenced").
- *Negative:* promotion workflow lives outside Q5; needs a documented owner (Eval Engineer per `scaling-strategy.md` §8.2).
- *Negative:* "what's in production right now" requires reading two sources.
- *Positive:* registry stays simple and immutable; promotion gating is auditable separately; rollback is a config change, not a registry write.
- *Positive:* multiple Q9 instances can serve different models for A/B without registry semantics getting weird.

---

## ADR-008 — Sequential Targeting for Multi-Target Cards

**Status:** Accepted.

**Context.** `scaling-strategy.md` §4.3. Multi-target cards (AoE, choose-N, "deal X to all") need an action representation. Two options: (a) sequential — each target picked as a sub-action; (b) joint — action is a vector of target indices.

**Decision.** Sequential targeting. Each target choice is a sub-action within a turn; ply count grows but action space per ply stays small and uniform.

**Consequences.**

- *Negative:* deeper MCTS trees for the same combat — throughput hit; budget more sims per combat or accept lower depth.
- *Negative:* search must reason about partial-targeting states ("card committed, target N selected, awaiting target N+1") — additional state-machine complexity in Q1's hook protocol.
- *Negative:* makes some natural priors (target the lowest-HP enemy across an AoE) harder to express in a single network output.
- *Positive:* action space per ply remains compact and uniform; mask shape stable; matches the existing prototype's `(card, target)` flow with minimal rework.
- *Positive:* commutative target orderings can be canonicalized at search time (move grouping) without expanding the action representation.

---

## ADR-009 — AlphaZero at Combat Layer; Hierarchical Heads at Run Level

**Status:** Accepted.

**Context.** `scaling-strategy.md` §2.1. Pure AlphaZero does not fit run-level (variable horizon, no shared evaluator across decision types). Pure hierarchical RL does not fit combat (decision-level granularity is exactly where MCTS shines). Strategy commits to a hybrid; this ADR makes that commitment structural.

**Decision.** Combat: AlphaZero (PUCT MCTS, prior + value heads, network leaf evaluator, expectimax oracle on small states). Run-level: hierarchical, shared encoder, per-decision-type heads (map, card-pick, shop, event, rest, potion), shared run-value function. Combat policy exposes a `value(deck, encounter)` oracle interface for run-level search to query.

**Consequences.**

- *Negative:* two stacks to maintain — search infrastructure plus per-head training. Higher engineering surface and two distinct failure modes.
- *Negative:* joint training of both layers is a known instability source (Phase 2 failure modes in `scaling-strategy.md`). Freeze-unfreeze schedule must be explicit.
- *Negative:* the combat-policy `value(deck, encounter)` interface is now load-bearing. Changes here ripple to the run-level search.
- *Negative:* Phase 5 may add MCTS at run-level too (`scaling-strategy.md` §3.5). That is a second integration we may have to add — ADR-revisit reserved.
- *Positive:* combat works as a standalone deliverable in Phase 1 — verifiable against the existing expectimax oracle before run-level even exists.
- *Positive:* matches the strengths of each algorithm to the layer where it works.

---

## ADR-010 — Content Registry Packaged with Model Artifact

**Status:** Accepted.

**Context.** Open question Phase 1: should Q4 be a centralized service (DB, hot-readable) or a versioned file packaged with each model artifact in Q5?

**Decision.** Q4 is a versioned, schema-stable file packaged inside every Q5 artifact. Workers, trainer, evaluator load Q4 from the same artifact they load weights from.

**Consequences.**

- *Negative:* updating tokens means a new artifact roll. No hot-fixing tokens for a deployed model.
- *Negative:* artifact size grows by token registry size — megabytes-scale, not gigabytes.
- *Negative:* two services running different model versions hold different token interpretations; coordination is the deployer's burden.
- *Negative:* curriculum / eval workflows that "use the latest tokens" must explicitly reference a model artifact, not a registry endpoint.
- *Positive:* one SHA describes the full bundle (weights, tokens, dataset reference) — reproducibility is structural, not procedural.
- *Positive:* trivially supports per-character or per-patch shards; no shared registry consistency problem.
- *Positive:* no new service to operate.

---

## ADR-011 — Oracle Owns the Engine→CompactState Adapter

**Status:** Accepted.

**Context.** Q1 (after the headless port) emits a versioned binary state. Q2 (Oracle) needs `CompactState` for expectimax. Someone owns the adapter. Either Q1 emits CompactState directly (couples Q1 to verifier semantics) or Q2 reads Q1's binary and derives CompactState.

**Decision.** Q2 owns the adapter. Q1 emits its versioned binary; Q2 consumes it and produces `CompactState`. Q2 is the single owner of verifier-side types.

**Consequences.**

- *Negative:* Q2's responsibility expands from "expectimax search" to "expectimax search + state translation." Larger module.
- *Negative:* adapter tracks Q1's schema version explicitly; coupled by version contract, not by in-process types.
- *Negative:* if Q1 schema changes faster than Q2 can absorb, oracle-agreement metrics stall.
- *Positive:* Q1 stays free of verifier concerns; cleaner Q1 boundary.
- *Positive:* one adapter, one owner, isolated blast radius. Failures here do not corrupt the engine.

---

## ADR-012 — Compute Hosting (cloud vs on-prem)

**Status:** Deferred. Owner: Research Lead. Target decision: end of month 1.

**Context.** `scaling-strategy.md` Appendix A #3. GPU + CPU fleet on cloud (elastic, pay-per-use) vs. on-prem (cap-ex but cheaper at full utilization). Affects cost model, latency, and orchestration tooling (k8s vs Slurm vs in-house).

**Decision deferred.** Phase 1 work is single-host (1 GPU + 64–128 CPU cores) and can run on whichever path is convenient for prototyping. Decision moved to a separate document when Phase 2 fleet sizing becomes concrete.

**Consequences of deferral.**

- *Negative:* orchestration tooling choice is on a deadline tied to Phase 2 fleet ramp; deferral past month 6 forces a parallel orchestration spike.
- *Positive:* Phase 1 unblocked; the choice is reversible at low cost while the worker fleet is small.

---

## ADR-013 — Megacrit Headless / Automation API

**Status:** Deferred. Owner: Research Lead. Target decision: pending Megacrit conversation.

**Context.** `scaling-strategy.md` Appendix A #2. If Megacrit exposes an official headless / automation API, much of Q1's port collapses from months to weeks.

**Decision deferred.** We proceed with our own headless port (ADR-002) on the assumption that no official API will exist. If one materializes, we re-baseline Q1 — most of the rest of the architecture is unaffected because Q1's interface (hook protocol, save/restore, replay) stays the same.

**Consequences of deferral.**

- *Negative:* may end up doing work Megacrit would have done for us.
- *Positive:* not blocked on an external partner; Q1's interface contract is what other quanta depend on, and that is ours to define regardless.

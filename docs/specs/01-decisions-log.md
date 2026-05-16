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
| ADR-009 | AlphaZero at Combat Layer; Hierarchical Heads at Run Level | Accepted (Amended 2026-05-14) |
| ADR-010 | Content Registry Packaged with Model Artifact | Accepted |
| ADR-011 | Oracle Owns the Engine→CompactState Adapter | Accepted |
| ADR-012 | Compute Hosting (cloud vs on-prem) | Deferred |
| ADR-013 | Megacrit Headless / Automation API | Deferred |
| ADR-014 | Combat Oracle Output Uses Samples + Summary | Accepted |
| ADR-015 | Combat Conditioned by Observable Run State + Explicit Macro Context | Accepted |
| ADR-016 | Deployed Policy Inputs Are Player-Observable / Belief-Sampled | Accepted |
| ADR-017 | Counterfactuals Stay Observational | Accepted |
| ADR-018 | Reward Valuation Stays Macro-Owned | Accepted |
| ADR-019 | Macro Context Derivation Policy | Accepted |
| ADR-020 | Oracle-Agreement Sideband Routes through Q3 | Accepted |
| ADR-021 | Phase-1 `combat_outcome_samples[]` Degenerate-Single Convention | Accepted |
| ADR-022 | Trajectory Protobuf Binding Homed at `pipeline/common/` | Accepted |
| ADR-023 | Spec Status Badges + Module-Spec Frontmatter | Accepted |

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

**Status:** Accepted. **Amended 2026-05-14** — see ADR-014..018; the run↔combat interface specification is superseded per `docs/micro-macro-policy-architecture-note.md`.

**Context.** `scaling-strategy.md` §2.1. Pure AlphaZero does not fit run-level (variable horizon, no shared evaluator across decision types). Pure hierarchical RL does not fit combat (decision-level granularity is exactly where MCTS shines). Strategy commits to a hybrid; this ADR makes that commitment structural.

**Decision.** Combat: AlphaZero (PUCT MCTS, prior + value heads, network leaf evaluator, expectimax oracle on small states). Run-level: hierarchical, shared encoder, per-decision-type heads (map, card-pick, shop, event, rest, potion), shared run-value function.

Combat policy exposes a run-conditioned outcome oracle:

```
evaluate_combat(observable_run_state, encounter_spec, macro_context, budget)
  -> combat_outcome_samples + summary_stats.
```

Run-level search composes these outcomes with reward generation, reward-choice heads, and V_run. Combat HP loss remains an auxiliary prediction target, not the run-level objective. (See ADR-014..018 for the five resolved decisions that constitute this amendment.)

**Consequences.**

- *Negative:* two stacks to maintain — search infrastructure plus per-head training. Higher engineering surface and two distinct failure modes.
- *Negative:* joint training of both layers is a known instability source (Phase 2 failure modes in `scaling-strategy.md`). Freeze-unfreeze schedule must be explicit.
- *Negative:* the combat-policy `evaluate_combat` interface is now load-bearing; sample-based output and `macro_context` input both ripple to run-level search and trainer loss heads.
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

---

## ADR-014 — Combat Oracle Output Uses Samples + Summary

**Status:** Accepted (2026-05-14).

**Context.** `docs/micro-macro-policy-architecture-note.md` §Resolved Decisions #1. ADR-009's original `value(deck, encounter)` interface produced a scalar HP-loss estimate. STS2 combat is a run-state transformer: a single fight mutates HP, potions, energy/stars, card piles, relic counters, power state, RNG counters, reward hooks. A scalar return destroys strategic correlations (e.g. "low HP but potion preserved" vs. "higher HP but potion spent"). Averaging away the correlation makes the macro-policy strictly weaker than playing the structured tradeoff.

**Decision.** Canonical combat output is `{ samples: CombatOutcomeSample[], summary: CombatOutcomeSummary }`. Each sample is a terminal-state snapshot with `(survived, after_combat_observable_state, hp_delta, potion_delta, card_instance_deltas, relic_counter_deltas, rng_public_belief_delta, turns_taken, timeout, probability_weight)`. The summary aggregates for cheap pruning: `(survival_probability, expected_hp_delta, hp_delta_quantiles, potion_use_probabilities, expected_turns, timeout_probability, uncertainty)`.

**Phase-1 transitional convention.** Phase-1 training keeps scalar HP-fraction prediction as the bootstrap target (architecture note §Training Implications). Phase-1 trajectory rows populate `summary.expected_hp_delta` from the scalar value; the exact `samples[]` populating convention (empty vs. degenerate single-sample with `probability_weight=1.0`) is deferred to the Q3 boot directive. Phase-2+ produces real multi-sample output.

**Consequences.**

- *Negative:* samples preserve resource-correlation in Phase-2+ but at storage cost: each sample carries `after_combat_observable_state` (a nested struct). K samples × N decisions/combat × M combats/day multiplies fast. Mitigation: design delta encoding / sample-by-reference / sample-count reduction at Q3 storage layer.
- *Negative:* combat-policy training pipeline grows a sample-prediction head; summary head is auxiliary.
- *Negative:* Phase-1 trajectories carry partly-populated v1 rows; downstream code must handle degenerate-sample case.
- *Positive:* macro-policy can reason about resource tradeoffs natively.
- *Positive:* summary supports cheap pruning at search nodes without inspecting samples.

**Origin.** Architecture note 2026-05-14.

---

## ADR-015 — Combat Conditioned by Observable Run State + Explicit Macro Context

**Status:** Accepted (2026-05-14).

**Context.** `docs/micro-macro-policy-architecture-note.md` §Resolved Decisions #2. Combat outcome is not a function of `(deck, encounter)` alone — it depends on HP/max HP, potion slots, relic counters, current relic/power hooks, and *macro-strategic prices* (how valuable is HP relative to gold this run, what is the per-potion shadow price given remaining elites). Pure inference from facts requires the combat head to re-derive prices every call.

**Decision.** `evaluate_combat` inputs are `(observable_run_state, encounter_spec, macro_context, budget)`. `macro_context` carries: HP shadow price, per-potion shadow prices, risk tolerance, upcoming pressure (next elite / next boss / shop / rest indicators), search budget. `observable_run_state` is the full visible run snapshot (deck, relics, gold, floor, etc.).

**Consequences.**

- *Negative:* combat policy's input surface grows; network must encode shadow-price tokens.
- *Negative:* `macro_context` derivation introduces a circular dependency (shadow prices are macro-policy outputs but combat-policy inputs) — see ADR-019.
- *Negative:* `evaluate_combat` interface must version `macro_context` shape alongside the rest of the wire contract (ADR-001).
- *Positive:* combat head no longer has to infer strategic preferences from scratch.
- *Positive:* clean separation: facts (observable_run_state) vs. prices (macro_context). Auditability + observability simpler.

**Origin.** Architecture note 2026-05-14.

---

## ADR-016 — Deployed Policy Inputs Are Player-Observable / Belief-Sampled

**Status:** Accepted (2026-05-14).

**Context.** `docs/micro-macro-policy-architecture-note.md` §Resolved Decisions #3. Q1's serialized state contains hidden source state useful for simulator correctness, labeling, debugging, and counterfactual analysis: RNG counters, future encounter/event queues, action queues, hook-private saved fields, unpopulated rewards. Training on perfect-source-info teaches choices a player cannot justify from visible information, overstating agent strength and producing exploit-shaped policies.

**Decision.** Deployed-policy inference inputs are restricted to player-observable or explicitly belief-sampled fields. Hidden source state remains in the simulator's state schema but is filtered out at the inference boundary. Each field in Q1's emitted state schema is classified `SOURCE_PERFECT` / `POLICY_VISIBLE` / `BELIEF_SAMPLED`. Q1 emits all fields; Q8/Q9/Q10 enforce the filter consumer-side. When hidden state matters for a decision, the policy reads sampled beliefs (e.g., posterior over draw-pile ordering) rather than the true value.

**Consequences.**

- *Negative:* every Q1 schema field needs a tag; tag manifest is now a versioned artifact alongside the state schema.
- *Negative:* belief-sampling infrastructure is Phase-2+ scope (no implementation exists today).
- *Negative:* Q12 evaluation must include observable-input audits (no hidden-state leak) to catch regressions.
- *Positive:* training-time and deployed-time information surfaces match; no skill overstatement.
- *Positive:* simulator-internal use (correctness checks, labeling, counterfactual analysis) is unrestricted.

**Origin.** Architecture note 2026-05-14.

---

## ADR-017 — Counterfactuals Stay Observational

**Status:** Accepted (2026-05-14).

**Context.** `docs/micro-macro-policy-architecture-note.md` §Resolved Decisions #4. Q12 path-counterfactual rollouts (taken-vs-not-taken map paths, alternative card-pick branches) are high-value diagnostics: they reveal whether the agent's choices were defensible relative to alternatives the same run could have produced. They are also high-variance: simulator stochasticity compounds with value-head bias when used as a training signal, amplifying noise rather than informativeness.

**Decision.** Q12 path-counterfactual rollouts drive evaluation, curriculum scenario selection, replay priority metadata, and human debugging — not direct supervised training targets.

**Carve-out.** Q2's oracle-agreement signal is NOT a path-counterfactual. It is a labeled comparison between expectimax ground truth and model output on the same state, not "what would have happened if a different path had been taken." Oracle-agreement continues to feed Q10 prioritized sampling per ADR-006 / ADR-009-amended, unchanged.

**Reopen trigger.** Revisit if a later ADR proves counterfactual estimates are calibrated and not variance-amplifying — for example, doubly-robust estimators with bounded variance proofs against the production policy distribution.

**Consequences.**

- *Negative:* counterfactual data is high-value but training-ineligible; expensive to compute and use only diagnostically.
- *Negative:* Q12 must clearly separate counterfactual-derived rows from labeled-comparison rows (oracle-agreement); schema discipline at Q6 report ingestion.
- *Positive:* variance-amplification risk avoided; Q12 reports stay diagnostic.
- *Positive:* oracle-agreement remains training-eligible — no impact on Q10 prioritization pipeline.

**Origin.** Architecture note 2026-05-14.

---

## ADR-018 — Reward Valuation Stays Macro-Owned

**Status:** Accepted (2026-05-14).

**Context.** `docs/micro-macro-policy-architecture-note.md` §Resolved Decisions #5. Combat outcome contains the post-combat state, but the *valuation* of room rewards (card / gold / relic / potion offers) is macro-level: it depends on archetype, current deck composition, upcoming pressure, alternative-path opportunity cost. Conflating reward valuation into combat output double-counts card/gold/relic/potion value and forces the combat head to learn the run-level value function in miniature.

**Decision.** Combat policy estimates fight costs + terminal combat state only. Reward generation and reward-choice valuation compose at macro/reward heads *after* combat. Combat may expose room-completion facts needed by reward hooks (e.g., "boss defeated", "elite cleared without potion use") but does not bake reward value into its output.

**Consequences.**

- *Negative:* macro-policy carries the full reward-valuation surface (card-pick head, relic-pick head, potion-pick head, event head).
- *Negative:* combat-vs-reward boundary needs disciplined enforcement — easy to leak reward value into combat training accidentally.
- *Positive:* no double-counting of reward value.
- *Positive:* reward-specific learning stays in the run layer where the right context (archetype, opportunity cost, multi-floor planning) lives.
- *Positive:* combat policy stays compact; transfer across decks improves because reward-specific noise is excluded from combat training signal.

**Origin.** Architecture note 2026-05-14.

---

## ADR-019 — Macro Context Derivation Policy

**Status:** Accepted (2026-05-15). Supersedes Deferred (2026-05-14).

**Context.** ADR-015 specifies `macro_context` as input to `evaluate_combat` (HP shadow price, potion shadow prices, risk tolerance, pressure indicators) without specifying *how* those values are computed. The Deferred entry listed four candidate derivation policies (bootstrap / learned / heuristic / joint-proximal) without ratification, citing chicken-and-egg: macro-policy needs sp as input to score paths via combat queries, but sp are Lagrangian duals of the macro-policy itself.

Research-lead decision on 2026-05-15 to ratify ahead of Phase-2 boot. Grounded in terminal reward `R = 1[won] · (1 + α · HP_terminal / HP_max)` with `α ∈ [0.01, 0.10]` (terminal HP strictly tie-breaks among winning trajectories; concrete value pending empirical contact), so `V_run = E[R | s]` and `sp_r = ∂V_run/∂r`.

**Decision.**

1. **Pricing scope — fungibles only.** Scalar shadow prices for HP, MaxHP, Gold, and per-potion-slot. Card and relic valuation stays with the per-instance evaluators ratified in ADR-018; no scalar `sp(card)` or `sp(relic)` — identity dominates (577 card classes, 295 relic classes, 38 with stateful counters), and a scalar aggregate would average out the signal the per-instance evaluators are designed to expose. Energy-per-turn deferred; reopen on Phase-3 content demand.

2. **Derivation — hybrid (c) → (b), reserve (d), retire (a).**
   - Phase-2 cold start: **(c) heuristic-curve warmup** from oracle rollout statistics (HP-per-floor, gold-per-floor curves).
   - Phase-2 primary: **(b) learned sp head co-trained with V_run**, supervised by finite-difference targets from V_run (autodiff first; FD fallback if numerically unstable for the state encoding).
   - Phase-3 reserve: **(d) joint proximal updates with damping** — reopen only if empirical evidence shows macro/micro coupling diverges under (b).
   - **(a) bootstrap-from-prior** retired as primary; permitted only as inference-time fallback when the learned head returns unreachable.

3. **Positioning — output of V, input to decision heads.** sp head sits downstream of the shared run encoder; stop-gradient on the consumption path, gradient flow on the supervision path against V_run derivatives. sp consumed by `evaluate_combat`, shop / event / swap evaluators, and card/relic-pick valuators (auxiliary signal). sp is **not** an input to V_run — eliminates the feedback loop motivating the original chicken-and-egg framing.

4. **Schema extension — trajectory.proto v1 → v1.1 (additive).** Adds `gold_shadow_price (float)` and `max_hp_shadow_price (float)` to `macro_context` with new tag numbers; forward-compatible. v1 rows treat missing fields as NaN-sentinel during transition. `derivation_method` allowed values tighten to: `"warmup_heuristic_curve" | "learned_autodiff" | "learned_finitediff" | "joint_proximal" | "fallback_lagged"`.

5. **Calibration acceptance.** Q12 sideband: empirical win rate of sp-favorable resource-exchange decisions (shops, swap events) on held-out evaluation converges to baseline + ε. Failure triggers reopen to (d).

**Consequences.**

- *Negative:* macro_context grows by 2 scalars in v1.1; Q3 sampler and Q10 reader must handle defaults for in-flight v1 rows during transition.
- *Negative:* Q10 trainer carries a new auxiliary loss head and FD-supervision pipeline against V_run derivatives; adds compute and a numerical-stability failure mode.
- *Negative:* Q12 adds a calibration report covering shop / swap-event decisions; couples ADR-019 acceptance to Q12 pipeline readiness.
- *Negative:* heuristic-curve warmup requires baseline rollout statistics; ADR-019 soft-couples to baseline-policy availability at Phase-2 boot.
- *Negative:* `α` range `[0.01, 0.10]` is empirically ungrounded; concrete value pending Phase-2 contact (tracked as reopen trigger).
- *Negative:* cards/relics deliberately get no scalar sp; surface users requesting "one number per resource" redirected to per-instance evaluators per ADR-018.
- *Positive:* closes the last deferred item in the ADR-014..018 cascade; unblocks Phase-2 substrate work needing concrete `macro_context` derivation semantics.
- *Positive:* fungibles-only scope avoids over-promising scalar valuation where identity dominates; theoretically clean.
- *Positive:* hybrid derivation gives a deterministic cold-start and a principled primary mechanism without locking in.
- *Positive:* output-of-V positioning eliminates the chicken-and-egg framing; V is the single source of truth, sp its summary.
- *Positive:* sparse-reward densification — sp auxiliary loss provides per-step gradient signal correlated with the binary terminal reward, improving sample efficiency.
- *Positive:* content-patch resilience — sp interface stable across Q4 token churn; only the encoder retrains to absorb identity changes.

**Reopen triggers.**

- (b) derivation fails to stabilize → escalate to (d) joint proximal.
- `α` empirics distort policy near boss → reopen reward shape (potentially split to a future ADR).
- Card/relic ΔV evaluators show enough regularity to make a scalar aggregate useful → add `sp(card_slot)` / `sp(relic_slot)`.
- Phase-3 content frequently modifies energy-per-turn → add `sp(Energy)`.

**Origin.** Research dialogue 2026-05-15 (research lead + Claude). Closes the Deferred entry surfaced during cascade ADR-014..018 on 2026-05-14.

---

## ADR-020 — Oracle-Agreement Sideband Routes through Q3

**Status:** Accepted (2026-05-14). Mirrors Q3-ADR-004 at `pipeline/experience-store/docs/specs/01-decisions-log.md`.

**Context.** Per ADR-017 carve-out, oracle-agreement (Q2-vs-network labeled comparison) remains a training-eligible signal feeding Q10 prioritized sampling. Routing options identified during Q3 boot: (a) Q2 → Q3 SidebandRouter → PriorityIndex → served via Sampler prioritized mode; (b) Q2 → direct table consumed by Q10 out-of-band; (c) Q2 → Kafka-like stream, no Q3 storage.

**Decision.** Route through Q3. Q2 emits to `POST /sideband/oracle-agreement` on Q3; Q3's SidebandRouter writes to `sideband/oracle.ndjson` and (Phase 2+) updates Q3's PriorityIndex. Trainer reads via Sampler `mode=prioritized` — one front door, one durability surface.

**Consequences.**

- *Negative:* Q3 becomes a hard dependency for oracle-agreement durability — Q3 outage queues at Q2 with a bounded buffer; long Q3 outage drops oracle-agreement signals. Mitigation: alerting on Q2-side queue depth, not architecture.
- *Negative:* Q3 must ship SidebandRouter at Q3 boot even though Q2's emit path may lag — SidebandRouter ships as a no-op write-and-store stub Phase 1 until Q2 wires it.
- *Negative:* couples oracle-agreement schema evolution to Q3 schema lifecycle; future oracle-agreement format changes require Q3 schema-migration coordination.
- *Positive:* trainer has one read path for everything it samples (trajectories + oracle-agreement); no out-of-band table to track.
- *Positive:* priority scores live next to the data they prioritize (same RocksDB DB file under different CF per Q3-ADR-010).
- *Positive:* migration path simple — Phase 2 just wires SidebandRouter into PriorityIndex; no consumer-side change.

**Origin.** Q3 boot directive cascade 2026-05-14. Q3-ADR-004 is the load-bearing version; this entry is the cross-quantum mirror.

---

## ADR-021 — Phase-1 `combat_outcome_samples[]` Degenerate-Single Convention

**Status:** Accepted (2026-05-14). Mirrors Q3-ADR-005 at `pipeline/experience-store/docs/specs/01-decisions-log.md`.

**Context.** Per ADR-014 and `contracts/schemas/trajectory/trajectory.proto:35-37`, Phase-1 trajectories populate `combat_outcome_summary.expected_hp_delta` from the scalar HP-fraction prediction. The exact `combat_outcome_samples[]` populating convention was deferred to Q3 boot. Two viable options: (i) empty array, (ii) degenerate single sample with `probability_weight=1.0` and `hp_delta` mirroring the summary.

**Decision.** Degenerate single sample. Phase-1 combat steps populate `combat_outcome_samples = [Sample(hp_delta=summary.expected_hp_delta, probability_weight=1.0, ...other_fields_zero)]`. Phase-2+ swaps to a real K-sample distribution as a population change — no schema bump required.

**Consequences.**

- *Negative:* downstream distributional analyses must filter degenerate rows (`len(samples)==1 AND probability_weight==1.0 AND sample.hp_delta==summary.expected_hp_delta`) to avoid biasing Phase-2+ variance estimates. Mitigation: filter recipe documented at `pipeline/experience-store/docs/specs/modules/sampler.md`.
- *Negative:* Q10 trainer reader code must account for the convention from boot; readers expecting "non-empty samples = real distribution" need to update detection logic. Mirror entry surfaces this for the cross-quantum reader.
- *Negative:* slightly larger Phase-1 rows than the empty-array option (one zero-padded `CombatOutcomeSample` per combat step).
- *Positive:* one code path for samples iteration on the consumer side (no `if samples_empty` branches in Q10).
- *Positive:* Phase-2 transition is a population change, not a schema bump — no migration event during the cascade.
- *Positive:* preserves the invariant that combat steps always carry at least one sample (good for sample-quality dashboards per `docs/specs/modules/observability.md:40`).

**Origin.** Q3 boot directive cascade 2026-05-14. Q3-ADR-005 is the load-bearing version; this entry is the cross-quantum mirror.

---

## ADR-022 — Trajectory Protobuf Binding Homed at `pipeline/common/`

**Status:** Accepted (2026-05-14). Mirrors Q10-ADR-005 at `pipeline/trainer/docs/specs/01-decisions-log.md`.

**Context.** The generated `trajectory_pb2.py` originally lived at `pipeline/experience-store/proto/`. Q10 (Trainer) needs to deserialize the same trajectory protobuf wire format from `POST /sample` responses. Options identified during Q10 boot: (a) vendor the generated binding into `pipeline/trainer/proto/` (duplication every `.proto` change); (b) lift to `pipeline/common/trajectory_proto.py` and have both Q3 and Q10 import from there; (c) move to top-level `contracts/generated/python/` per Q2-ADR-001 §4 (the spec-correct long-term home, but it requires a codegen pipeline Q3 explicitly deferred at boot).

**Decision.** Option (b). The generated trajectory binding lives at `pipeline/common/trajectory_proto.py`. Q3's `pipeline/experience-store/proto/__init__.py` becomes a thin re-export. Q10 (and any future quantum) imports from `pipeline.common.trajectory_proto`. The contract: any change to `contracts/schemas/trajectory/trajectory.proto` regenerates the file at one location; Q3 + Q10 consume unchanged.

**Consequences.**

- *Negative:* Q3 carries a no-op re-export shim for the Phase-1 lifetime — small cost, but it is API surface area to maintain.
- *Negative:* the `pipeline/common/` package gains a binding that is semantically owned by Q3's wire format. If `pipeline/common/` later develops its own ownership rules, this entry must be re-homed.
- *Negative:* eventual move to `contracts/generated/python/` is a future refactor (option c). Postponing it is a known tech-debt item.
- *Positive:* zero duplication between Q3 and Q10; single source of truth for the wire format.
- *Positive:* Q3 boot-time invariants (schema version checks in `schema_registry`) operate on the same generated module Q10 imports — no skew possible.

**Origin.** Q10 boot directive cascade 2026-05-14. Q10-ADR-005 is the load-bearing version; this entry is the cross-quantum mirror.

---

## ADR-023 — Spec Status Badges + Module-Spec Frontmatter

**Status:** Accepted (2026-05-16).

**Context.** Agent-dispatch workflows depend on module specs (`docs/specs/modules/*.md`) for context-bootstrap. A doc-sync audit on 2026-05-16 found that timestamp-drift is small (1–3 days) but **semantic drift is severe**: specs describe Phase-2+ aspirations as Phase-1 responsibilities. Concrete examples — Q4 spec promises a "token-coherence regression battery" that does not exist; Q10 spec lists oracle-agreement priority replay but `pipeline/trainer/` has zero Q2 RPC plumbing; Q1 spec implies 22 encounters working though only 1 has per-step probes. Subagents inherit confident-but-wrong context and waste cycles. There is no machine-readable way to tell, per section, what is shipped vs deferred vs aspirational.

**Decision.** Two coupled conventions.

*Per-section status badges.* Every Responsibilities/Interfaces/Coupling section of every module spec MUST carry at least one inline badge:

- `[SHIPPED]` — section describes code that exists and passes a gate.
- `[PHASE-N]` — implementation deferred to a named phase (e.g., `[PHASE-1.5]`, `[PHASE-2]`).
- `[ASPIRATION]` — design intent only; no implementation roadmap committed.

Mixed badges within a section are allowed and encouraged. Per-bullet badges are allowed when a section straddles shipped + aspirational claims. **No fourth `[CONTRADICTS-CODE]` badge exists**: contradictions are merge-boundary states. When a subagent finds spec-says-X / code-does-Y during rebadging, it must EITHER update the spec to match code (the common case — spec is stale) OR add inline `[NOTE: contradicts code at <path>; tracking <issue/ADR>]` and surface to project-lead.

*Module-spec frontmatter.* Every `docs/specs/modules/*.md` MUST carry YAML frontmatter declaring `quantum` and `substrate` path:

```yaml
---
quantum: Q1
substrate: engine/headless/
---
```

`substrate: n/a` is allowed for quanta whose substrate is derived (Q6) or Phase-2+ TBD (Q11). Frontmatter is the machine-readable source-of-truth consumed by the Phase 3 spec-edit gate (see ADR-024) — it substitutes for parsing the quantum-map markdown table in `.claude/CLAUDE.md`.

**Consequences.**

- *Negative:* badges add visual noise to every spec section. Trade-off accepted because the noise is the signal.
- *Negative:* requires discipline to maintain — subagents may forget to badge, mis-badge, or remove badges during unrelated edits. ADR-024 installs a soft-enforcement gate; until then, this is a convention.
- *Negative:* frontmatter introduces a sync surface between this convention and the quantum-map table in `.claude/CLAUDE.md`. Drift is possible. Mitigation: Phase 1 sweep uses the table as source-of-truth at install time; future divergence flagged for a follow-up CI consistency check.
- *Negative:* the "no `[CONTRADICTS-CODE]` badge" rule pushes work onto the merge boundary — subagents finding contradictions must resolve, not annotate. Slows down some PRs.
- *Positive:* agents can scan top-of-section to determine implementation status without reading the substrate. Phantom-feature mirage (specs ahead of dormant substrate) becomes immediately visible.
- *Positive:* frontmatter unlocks deterministic gate logic (ADR-024) without convention-fragile markdown parsing.
- *Positive:* the badge taxonomy mirrors the project's phase-gate structure already in use (`docs/scaling-strategy.md` Phase 1 / 1.5 / 2+), so no new mental model.

**Origin.** Doc-sync planning session 2026-05-16. Triggered by project-lead status report flagging Q7/Q8/Q9/Q12 specs newer than dormant substrates ("phantom features") and spot-checks of Q1/Q4/Q10 surfacing aspirational-as-current responsibilities.

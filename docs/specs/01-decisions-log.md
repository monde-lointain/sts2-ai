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
| ADR-024 | Spec-Edit Tracker + Re-Baseline Convention | Accepted |
| ADR-025 | Spec-Edit Gate Promotion (warn → hard-block) | Reserved (per ADR-024 §1.4) |
| ADR-026 | Upstream-Sync Pipeline | Accepted |
| ADR-027 | Q4 Phase-1 Fixture Growth Policy | Accepted |
| ADR-028 | Q1 Silent-Engine Baseline Ratified at Upstream v0.105.1 | Accepted |
| ADR-029 | Path A Engine-Expansion Campaign | Accepted (2026-05-17) |
| ADR-030 | Q1 Hook Protocol Extension for OnDeath Mechanics | Accepted (2026-05-19) |
| ADR-031 | Zobrist Cardinality Audit (Archive) | Accepted (Archive) |

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

**Amendment (2026-05-17).** Storage aggregate widens (per Q2-ADR-006/007). `CompactState.enemies_` becomes `std::array<EnemyState, kMaxEnemies=4>` plus `uint8_t enemy_count_`; `EnemyState` becomes polymorphic via a `MonsterKind` byte and a generic `std::array<PowerInstance, kMaxPowersPerCreature=6>` array with `uint8_t power_count`; `CompactState` gains `player_powers_` array (`std::array<PowerInstance, kMaxPowersPerCreature>` + `uint8_t player_power_count_`). The hashable + value-semantics + cheap-clone invariants are preserved: `CompactState` remains a POD aggregate with `operator==` and `CompactStateHash`. `RichState` is unaffected. `CompactState` remains Q2-internal; no `contracts/schemas/` change is required.

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

---

## ADR-024 — Spec-Edit Tracker + Re-Baseline Convention

**Status:** Accepted (2026-05-16).

**Context.** ADR-023 ratifies a per-section badge convention for module specs but does not enforce that badges stay accurate as code evolves. Without machinery, the convention rots — agents edit specs, substrate moves on, badges fall behind. The proto-edit tracker (`proto-edit-tracker.py` + `pre-push-proto-adr-gate.py`) is a proven soft-enforcement pattern: a PreToolUse hook logs edits to a state file; a pre-push gate enforces resolution criteria before main-bound pushes. Extending the same pattern to module specs is the lowest-friction enforcement extension surface available.

**Decision.** Three coupled mechanisms.

*1. Spec-edit tracker hook.* New file `.claude/hooks/spec-edit-tracker.py`, modeled directly on `proto-edit-tracker.py`. Fires PreToolUse on Edit/Write/MultiEdit when the target file matches `docs/specs/(modules/.+|00-system-overview)\.md$`. Appends an entry `{file, edited_at, agent, head_sha_at_edit, resolution: null}` to `.claude/state/spec-edits-pending-resolution.json`. Non-blocking. Registered in `.claude/settings.local.json` alongside the proto tracker (matcher: `Edit|Write|MultiEdit`).

*2. Pre-push spec-resolution gate.* New file `.pre-commit-hooks/pre-push-spec-resolution-gate.py`, wired into `.pre-commit-config.yaml` at `stages: [pre-push]` alongside the proto-adr-gate. On main-bound push, for each pending entry, the gate looks up the spec's `substrate:` path **directly from the spec's YAML frontmatter** (per ADR-023 — no quantum-map markdown parsing) and checks whether either (a) the pushed commits touch that substrate path or (b) any pushed commit message contains the literal flag `doc-only:`. **Phase 3a is warn-only**: the gate prints unresolved entries to stderr but exits 0. Feature-branch pushes pass freely.

*3. Re-baseline convention update.* The `running-a-quantum-ci-gate` skill is extended with a post-gate audit step: when a quantum's gate passes, scan its spec for sections that should change badge (e.g., a `[PHASE-1.5]` claim that the gate now exercises → `[SHIPPED]`). Phase-gate reports MUST end with a single line: `specs re-baselined: yes — sections updated: <list>` OR `specs re-baselined: not needed — <rationale>`. **Applies prospectively from the Phase-1.5 gate onwards** — no retroactive backfill of Phase-0/Phase-1 reports.

*Future promotion path (ADR-025+).* The warn-only gate promotes to a hard block after **two consecutive wave cycles complete with zero gate warnings**. One wave is too small a sample (could be coincidence); two cycles is strong signal the convention is being followed. A follow-up ADR (ADR-025 or later) ratifies the promotion. Until then, the warn output is the early signal — agents should treat warnings as TODO list items, not noise.

*`doc-only:` commit-message flag.* A new commit-message convention. Any commit whose message contains the literal substring `doc-only:` declares that the spec edit is intentional without a corresponding substrate change (e.g., post-hoc badge rebalancing, ADR cross-reference cleanup, typo fix in a spec). The gate honors the flag without further scrutiny — it is an audit-trail signal, not an enforcement bypass. Convention is documented here only; no separate doc.

**Consequences.**

- *Negative:* the tracker accumulates entries indefinitely — `.claude/state/spec-edits-pending-resolution.json` has no cleanup logic in this ADR. Resolved entries should be pruned by the gate or by a hand-rolled cleanup step at wave close. Deferred to a follow-up — Phase 3a entries are small enough to ignore for now.
- *Negative:* the `substrate:` frontmatter is a new sync surface that can drift from `.claude/CLAUDE.md`'s quantum-map table (see ADR-023 Consequences). The gate trusts frontmatter; if it's wrong, the gate gives the wrong answer. Convention-only check; CI consistency check is a follow-up tooling pass.
- *Negative:* the `doc-only:` flag is a documented hole in enforcement — anyone can add it without justification. Mitigation: it's an audit signal that shows up in `git log`. Reviewers can grep for it during PR review.
- *Negative:* warn-only mode is silent under the project's default tooling — pushers may ignore the stderr message. Mitigation: two-wave promote-to-block window forces the convention to be observably enforced before going hard.
- *Negative:* registering the new hook in `.claude/settings.local.json` (which is gitignored) means the hook does not fire for other team members until they manually register it. Inherits this constraint from the proto-edit-tracker precedent. Follow-up: migrate shared-hook registrations to a tracked `.claude/settings.json`.
- *Positive:* re-uses the proto-tracker pattern exactly — no new mental model, no new tooling, low maintenance cost.
- *Positive:* frontmatter-as-lookup avoids fragile markdown-table parsing. Adding a new module spec automatically participates in the gate as long as its frontmatter is present (ADR-023 mandates this).
- *Positive:* phase-gate reports become the natural re-baseline checkpoint, surfacing rebadging needs at the moment evidence is fresh (the gate just ran).
- *Positive:* warn-only first cycle creates a measurable adoption signal before forcing compliance.

**Future tooling appendix.** *Badge auto-inference from gate-report status.* Phase-gate reports already encode per-section "shipped vs deferred" via their pass/fail structure. A future tool could parse gate reports and propose badge rebalances mechanically. Deferred until the gate-report convention has stabilized across ≥3 phase gates — premature today.

**Origin.** Doc-sync planning session 2026-05-16. Follows immediately from ADR-023; ratifies the prevention machinery sketched in the same plan (`/home/clydew372/.claude/plans/do-p0-dynamic-alpaca.md`).

---

## ADR-026 — Upstream-Sync Pipeline

**Status:** Accepted (2026-05-17).

**Context.** Prior to this ADR, syncing new STS2 upstream patches was a fully manual ceremony: user runs Steam auto-update → user runs GDRE → user rsync-mirrors the decompiled tree → user manually diffs → user authors port-decision markdown by hand. This ceremony was not repeatable at scale and missed the v0.103.2 → v0.105.1 gap (a 37-day drift spanning 323 card + 110 monster + 148 power + 120 relic + 5 encounter changes). Wave 3.5 attempted a bridge under this manual model and was ABORTED (ref: B.0.5 close-out). Phase A.1–A.4 ship detection, categorization, prompt-generation, and drift-gate tooling (tools/upstream-sync). This ADR codifies the resulting pipeline design.

**Decision.** Four-tier pipeline:

1. **Detection** — `tools/upstream-sync/src/upstream_sync/cli.py sync-check` polls Steam buildid against `.upstream-sync-state.json`; compares against last-synced buildid. Emits structured delta report.
2. **Categorization** — `diff_analyze.py` classifies each changed upstream path into buckets (monsters, encounters, cards, relics, powers, combat-engine, etc.) and assigns a per-row decision (PORT / DELETE / IGNORE / SURFACE-NO-ACTION) based on heuristics + correlation against patch notes. Emits port-decision markdown + JSON sidecar (`engine/headless/docs/specs/0N-vA.B.C-to-vX.Y.Z-port-decisions.json`) with machine-readable per-row records including a `status` field.
3. **Prompt generation** — `prompt_generator.py` generates engineer-dispatch prompts from JSON sidecar rows. Templates per bucket (Monsters, Encounters, Cards, Relics, Powers, …) embed project wave-protocol invariants (absolute paths, OWNED/FORBIDDEN lists, preflight SHA, verification commands). `cli.py dispatch-quantum-lead` emits a quantum-lead briefing to stdout (not a subagent spawn — user pastes into Claude session).
4. **Drift gates** — `SyncStatePinGate` (content baseline) + `DllSignatureGate` (reflection-call viability). Both run in Q1 CI. Execution follows the existing wave-dispatch protocol; no new infrastructure.

**Detect-and-propose semantics.** The pipeline detects drift and generates dispatch artifacts. It does NOT auto-edit engine code. All engine code changes go through standard engineer-subagent → wave → gate → merge flow.

**Multi-trigger.** Three trigger surfaces:
- Local crontab on user's hardware (Steam-aware; polls buildid via steamcmd or state file).
- GHA state-only cron (Steam-unaware; polls `.upstream-sync-state.json` committed to repo; fires if buildid diverges from pin — signals local sync needed but cannot fetch Steam artifacts itself).
- Manual `make sync` / `make sync-check` (on-demand).

Un-categorizable changes (paths falling through all bucket patterns) surface to quantum-lead for manual review. Quantum-lead decides PORT / IGNORE / SURFACE-NO-ACTION.

**Pin semantics (concern 1 codified).** `engine/headless/upstream-pin.json:pinned_version` tracks the **content baseline** — the upstream version from which Phase-1 monsters, encounters, cards, relics, and powers were ported. It does NOT track every file in the repo. Infrastructure files (UpstreamDriver, drift gates, tools/upstream-sync) MAY be at a newer upstream version than the pin. Two gates are deliberately decoupled:
- `SyncStatePinGate` enforces content baseline: asserts `.upstream-sync-state.json:last_synced_version == pinned_version`. Fails if state has advanced past the pin (signals un-merged bridge wave).
- `DllSignatureGate` enforces reflection-call viability against the live DLL (by SHA). Asserts DLL SHA matches `upstream-pin.json:pinned_dll_sha256`.

**Pin advancement.** The pin flips only at end-of-Phase-B bridge ceremony: after the final bridge wave merges AND all gates are green. Pin does NOT advance incrementally per bridge wave. During an in-progress bridge, `SyncStatePinGate` reports FAIL with a structured `"bridge-in-progress"` message as a WIP signal; this is expected and non-blocking for bridge-wave CI.

**JSON sidecar per-row status lifecycle.** Five terminal/transition states: `PENDING` (initial, awaiting dispatch) | `DISPATCHED` (wave in flight) | `MERGED` (wave merged, gate green) | `DEFERRED` (explicitly deferred to Phase-2+) | `NO_ACTION_NEEDED` (SURFACE-NO-ACTION or IGNORE rows — set at sidecar-generation time, never auto-flipped). `/wave-close` bumps PORT/DELETE rows to `MERGED` via `.claude/scripts/update-port-decision-status.sh` (soft-fail). `NO_ACTION_NEEDED` rows are excluded from dispatch; this prevents the 468-row "other" bucket from generating perpetual PENDING noise.

**Consequences.**

- *Negative: Semantic drift not detected by structural gates (concern 7).* `DllSignatureGate` + `SyncStatePinGate` catch arity/rename drift but NOT behavioral changes within unchanged signatures — e.g., upstream rebalances a Power's stack-application without changing method name or signature. `probe-upstream-initial-state` catches initial-state-plane semantic drift; mid-combat semantic drift surfaces only via the Wave 6.5 SetUpCombat shim (deferred to Phase-2+). **Bridge engineers MUST paste relevant upstream code as inline comments at every Q1 behavior-change site** to provide a code-review baseline. This is a convention, not a gate.
- *Negative: Lockfile required for concurrent runs (concern 9).* Local crontab + manual `make sync` can race on `.upstream-sync-state.json` writes. `tools/upstream-sync/cli.py` MUST acquire `flock` on `.upstream-sync-state.json.lock` before writes; cron script bails on lock contention. Implementation deferred to a follow-up wave. Documented here as a known requirement to prevent partial-write corruption.
- *Negative: Bundle preservation cadence (concern 8).* Existing `phase1-genealogy-v1.bundle` preserved as-is; a new `phase1-genealogy-v2.bundle` is created at each Phase boundary (Phase-1 close, Phase-2 close, etc.). Bundles are immutable — do not overwrite old ones.
- *Negative: Template coverage incomplete (concern 10).* Phase A.2 ships 2 prompt templates (`monster.j2` + `generic-port.j2` fallback). Six-plus additional bucket templates (encounter, card, relic, power, combat-engine, signature-change) added incrementally during bridge waves as observed need arises. Missing template → fallback to `generic-port.j2`; quality of generated prompts degrades gracefully.
- *Negative: CI cannot detect Steam-side drift.* GHA runners have no Steam access. Detection requires local crontab on user's hardware. Recommendation: disable Steam auto-update for STS2 during bridge to prevent mid-bridge drift.
- *Negative: Phase-1 close delayed by ~2–3 weeks* while bridge runs. Minimal-bridge off-ramp is available: if bridge stalls after Wave 6, project-lead may elect to cap Phase-1 at v0.103.2 content + classify v0.105.1 delta as Phase-2 scope. This off-ramp is documented; decision authority rests with project-lead.
- *Negative: CI cost.* The 2–3 week bridge consumes approximately 10 GHA-hours total CI cost. Nontrivial but bounded and accepted.
- *Negative: Wave 3.5 ABORTED reference branch preserved.* Wave 3.5 work is preserved as an ABORTED reference (re-dispatched fresh post-bridge-Wave-1). See B.0.5 close-out entry.
- *Positive:* Future upstream updates flow through the pipeline with standard detection-categorization-dispatch shape, replacing the ad-hoc GDRE+rsync ceremony.
- *Positive:* Drift gates catch the v0.105.1-class break automatically; the manual incident that triggered Wave 3.5 ABORT cannot recur post-pipeline.
- *Positive:* JSON sidecar enables per-row status tracking across bridge waves; quantum-lead's bridge plans become machine-readable and tool-auditable.
- *Positive:* Local-cron + GHA-state-cron split maintains both Steam-aware and Steam-independent detection paths, preserving detection capability even when the user's machine is offline.

**Origin.** Wave 5 / Stream A.4. Upstream-sync pipeline planning session 2026-05-17. Follows from Wave 4 (A.0) prerequisites (pin artifact, DLL hash, schema v1). Plan: `~/.claude/plans/use-your-best-judgement-sparkling-feigenbaum.md` § Phase A.

---

## ADR-027 — Q4 Phase-1 Fixture Growth Policy

**Status:** Accepted (2026-05-17). Ratified in Wave 5.5 (Phase B.0).

**Context.** The upstream v0.103.2 → v0.105.1 delta (categorized by Phase A.2 tooling and recorded in `engine/headless/docs/specs/03-v0.103.2-to-v0.105.1-port-decisions.md`) includes 323 card changes (PORT), 110 monster changes (PORT + DELETE), 148 power changes (PORT + DELETE), 120 relic changes (PORT + SURFACE-NO-ACTION), 5 encounter changes (PORT), 33 potion changes (PORT). The Q4 Content Registry Phase-1 manifest is split across two artifacts:

- **Canonical Q4 registry** (`contracts/registry/phase1-silent.json`): 98 cards / 59 relics / 45 powers / 0 monsters / 0 potions / 0 encounters today. Per `content-registry.md` § Token IDs SHIPPED, the documented seed is "96 cards / 58 relics / 45 powers / 32 monsters / 21 potions / 22 encounters" — minor spec drift (registry artifact has +2c/+1r already from Wave 1 work; monsters/potions/encounters were never added to the canonical registry, only to the Q1 fixture).
- **Q1 Phase-1 manifest fixture** (`engine/headless/test/fixtures/q4-manifest-phase1.json`): 98 cards / 59 relics / 45 powers / 33 monsters / 21 potions / 0 encounters. The fixture is Q1's local snapshot of canonical-registry-equivalent content for use in `Q4ManifestLoaderTests` content-coverage gate.

If the bridge ports all PORT-classified upstream changes into Phase-1, both artifacts grow substantially (likely 200+ cards, 100+ relics, 100+ powers, 100+ monsters). Three policy options were under consideration: (i) grow caps to track upstream, (ii) keep caps + classify excess as Phase-2 scope, (iii) one-time re-baseline via ADR setting new permanent caps.

**Decision.** **Option (i) + (iii) blended: grow caps to upstream content, formalized as a one-time re-baseline event ratified by this ADR.**

Rationale (in priority order):
1. **Probe-gate validity:** The bridge's `make probe-upstream-initial-state` gate flips green only when Q1 content matches upstream v0.105.1. Capping at v0.103.2 content means the gate can never reach 100% — defeating the purpose of the upstream-sync pipeline (ADR-026).
2. **Content-registry honesty:** "Phase-1 cap" was originally a starting seed, not a binding ceiling. Treating it as binding has been holding spec text out of sync with the canonical artifact since Wave 1 (already +2c/+1r drift). Re-baselining acknowledges the cap-as-floor reality.
3. **Phase-2 boundary preserved:** Phase-2 is defined by FEATURE COMPLETENESS (Q5/Q6 boot, evaluation harness boot, observability TSDB scrape live) per `00-system-overview.md`, NOT by content counts. Growing Phase-1 caps does not move the Phase-2 trigger.
4. **No downstream-quantum spec churn:** Q5/Q6 ASPIRATION sections in their respective module specs do NOT reference exact Phase-1 cap numbers (verified via grep 2026-05-17); they reference "Phase-1 manifest" generically. This ADR does not cascade into Q5/Q6 spec edits.

**Mechanics.** The bridge waves (Phase B.2-δ onward) port content per the port-decisions doc; the canonical Q4 registry + Q1 test fixture grow per-wave as monsters/cards/relics/powers/potions/encounters land. Final post-bridge caps are determined by what v0.105.1 actually contains (counted at pin-advance ceremony). Future upstream syncs that grow content further repeat the same pattern — but DO NOT require a fresh ADR per sync; this ADR ratifies the *policy* (Phase-1 caps track upstream), not specific numbers.

**Consequences.**

*Negatives:*
- **Phase-1 manifest fixture grows ~3× across categories.** `Q4ManifestLoaderTests` test count grows proportionally; CI wall-clock for content-coverage gate may grow noticeably (mitigation: gate runs in parallel with other Q1 gates).
- **Re-baselining sets a precedent.** Future syncs (v0.105.1 → v0.106.x) grow caps further; future spec readers might assume "Phase-1 caps == upstream HEAD." This is the intended outcome per the policy, but requires explicit acknowledgment in `content-registry.md` so newcomers don't read the cap numbers as binding constraints.
- **Stale spec text remains stale until corrected.** `content-registry.md` Token IDs SHIPPED line still claims "96 cards / 58 relics / 45 powers / 32 monsters / 21 potions / 22 encounters." This ADR + the `content-registry.md` addendum (landed in this commit) correct it explicitly; future bridges update the line per pin-advance ceremony.
- **Bridge engineer prompts will reference larger fixture targets.** Prompt-generator (Phase A.2) must template the current post-bridge expected counts into engineer-dispatch prompts. Not a blocker but worth noting.
- **Test wall-clock budget** for `make q1-ci` grows; if it approaches the project's 60s budget threshold, partition tests by content category.

*Positives:*
- Q1 substrate becomes byte-faithful to upstream v0.105.1 post-bridge; probe-upstream-initial-state reaches its design intent (160 PASS or whatever the new full count is).
- Canonical Q4 registry vs Q1 fixture drift gets closed (both grow together in bridge waves).
- Future upstream syncs flow through the pipeline without re-litigating cap policy.
- Phase-2 work (Q5/Q6/Q7 boot, evaluation harness) operates against the actual upstream content space, not a frozen v0.103.2 subset — reduces "Phase-2 surprise" risk.

**Implementation.** Per the bridge plan (Phase B.1 quantum-lead output): each bridge wave that adds upstream content also adds the corresponding token-table entries to `contracts/registry/phase1-silent.json` + `engine/headless/test/fixtures/q4-manifest-phase1.json` in the same commit. Token ID assignment per `content-registry.md` stability rules (ADR-003): new IDs for new content; deprecated content's IDs never reused. The bridge wave that finalizes Phase-1.5 close-out (pin-advance ceremony) audits both artifacts for parity + corrects any per-wave drift.

**Cross-references.** ADR-003 (Token Registry as Patch-Adaptation Lever) — this ADR is the concrete first invocation of ADR-003's "patch adaptation" machinery. ADR-026 (Upstream-Sync Pipeline) — defines the bridge sequence + pin-advance ceremony that mechanizes this policy.

---

## ADR-028 — Q1 Silent-Engine Baseline Ratified at Upstream v0.105.1

**Status:** Accepted (2026-05-17).

**Context.** `scaling-strategy.md` §3 Phase-1 ("Generalized Tactical Combat") lists five environment-modification prerequisites for the silent engine: (1) headless C# core build, (2) combat-only entry point, (3) branchable state with bit-identical save/restore, (4) `RichState` derivation with stable serialization, (5) hookpoint at every player-decision boundary. These are SUBSTRATE prerequisites — distinct from Phase-1's policy training OUTCOME criterion (≥95% A0 win rate, which lives downstream in Q10/Q12 once those quanta boot).

As of Wave 15 close (commit `093cb37`, 2026-05-17) the substrate-side bridge from upstream v0.103.2 → v0.105.1 (Phase-B Waves 4 through 15, executed under ADR-026 + ADR-027) has cleared all green-gate signals simultaneously for the first time:

- 831 Q1 domain tests pass.
- 65 `BitIdenticalRoundtripTests` pass (#3 prerequisite — bit-identical save/restore).
- 41 `DllSignatureGate` signatures match the live v0.105.1 DLL (reflection-call viability).
- 160 `probe-upstream-initial-state` tests pass (full corpus initial-state Godot parity at v0.105.1; was 140 PASS + 20 SKIP pre-Wave-14).
- 7 `Q4ManifestLoader` content-coverage tests pass (manifest at `phase1-silent.1`: 266 tokens — 98c/59r/45p/37m/21pot + 4 deprecated + 6 specials, per ADR-027 cap-growth policy).
- `SyncStatePinGate.PinBuildId` flipped FAIL→PASS at the pin-advance ceremony (bridge in-progress signal cleared).

Without an explicit anchor declaring "the Q1 silent-engine substrate is stable at upstream v0.105.1," downstream-quantum dispatch (Q2 oracle revival, Q3 experience-store boot, Q8 rollout workers, Q9 inference server) operates against an implicit baseline that can drift as further bridge or refactor work lands. Q1 has been the central dependency of every Phase-1 dispatch since project inception; absent a documented baseline, every downstream wave plan must re-derive "is Q1 ready?" from first principles. Phase-1.5 (per-step Godot parity across the 15 non-`CultistsNormal` encounters per `game-simulator.md` § Phase Expectations) remains OPEN and is out of scope of this ADR.

**Decision.** Declare the Q1 silent-engine substrate baseline RATIFIED at upstream v0.105.1 (buildid 23156356; DLL sha tracked in `engine/headless/upstream-pin.json`).

*Scope.* Substrate readiness only. This ADR ratifies:

1. Phase-1 environment prerequisites #1, #2, #3, #5 (scaling-strategy §3.1 environment-modifications list) as SHIPPED against upstream v0.105.1.
2. Prerequisite #4 (`RichState` derivation) — combat-side SHIPPED per existing `game-simulator.md` Data Ownership badges (M1 schema v3 operational); run-level `RichState` fields explicitly DEFERRED to Phase-2 with no change to that classification.
3. Q1's role as the upstream-pinned substrate anchor against which Q2/Q3/Q8/Q9 plan dependencies during downstream dispatches.

*Out of scope.*

- Phase-1 policy training outcome (≥95% A0 win rate) — stays with Q10/Q12 when those quanta boot.
- Phase-1.5 per-step Godot parity across the 15 non-`CultistsNormal` encounters — separate work track; this ADR does not advance Phase-1.5 badges.
- Q2 dormancy resolution — a follow-up wave (the natural next critical-path item per ADR-014 oracle-target dependency).

*Pin-advance discipline.* This ratification is single-version-scoped. Any subsequent upstream patch ≥ v0.106 re-opens substrate-readiness and runs through the standard upstream-sync pipeline (ADR-026) → bridge ceremony → fresh ADR (or amendment of this one). The ratification does NOT imply ongoing automatic re-pinning.

*Phase-anchor semantics for downstream quanta.* Q2/Q3/Q8/Q9 dispatch prompts MAY treat Q1 at HEAD (currently `093cb37`) as a stable substrate-interface for planning purposes. Schema bumps (M1 / M2 / M3 / M4 / hook-protocol / replay format) still flow through the standard process per ADR-001 and the `bumping-a-schema-version` skill — this ADR freezes the SUBSTRATE, not the contracts.

*Operational cleanup.* Open entries in `.claude/state/spec-edits-pending-resolution.json` from Phase-B (Waves 2–15) are acknowledged as resolved under ADR-024 § Consequences ("Resolved entries should be pruned by the gate or by a hand-rolled cleanup step at wave close"). Three stale `.claude/worktrees/agent-*/...` entries (whose worktrees no longer exist) are purged outright; two main-repo entries (`model-registry.md`, `content-registry.md`) carry `resolution.type = "wave-closed-acknowledgment"` with reference to this ADR. Bridge closure is the natural cleanup point envisioned by ADR-024.

**Consequences.**

- *Negative:* Single-version pin lock. Any new upstream patch ≥ v0.106 re-opens substrate-readiness; the project cannot incrementally re-sync without re-running the full bridge ceremony per ADR-026. Mitigation: drift gates (`SyncStatePinGate` + `DllSignatureGate`) catch the divergence immediately; the cost is bounded by ADR-026's pipeline mechanics, not by surprise.
- *Negative:* Declaring Q1 ready creates implicit pressure to revive Q2 (dormant since pre-monorepo prototype import). Downstream dispatches can now plan against Q1, which exposes the Q2 gap as the next critical-path blocker (per ADR-014 oracle-target dependency for any RL training loop). This is intentional — surfacing the next critical-path item is the point — but the pressure is real.
- *Negative:* Phase-1.5 remains OPEN: 15 encounters lack per-step Godot parity (`game-simulator.md` Responsibilities bullet 2; the live-Godot per-step blocker is upstream's 12 `SceneTree`-coupled singletons in `CombatManager.StartCombatInternal`). Future readers MUST NOT conflate substrate-baseline ratification with Phase-1.5 completion or with Phase-1 outcome ratification.
- *Negative:* "Ratified at v0.105.1" anchors Phase-1 substrate to a moving target — Steam upstream advances independent of this project. If the user does not disable Steam auto-update for STS2 during Phase-1.5 work, drift risk persists (per ADR-026 mitigation note: "Recommendation: disable Steam auto-update for STS2 during bridge to prevent mid-bridge drift" — applies equally to substrate-baseline lifetime).
- *Negative:* Wave 15 was a single-day execution of Waves 4–15 (per `git log` 2026-05-17). Compressed cadence means less elapsed soak time on the v0.105.1 pin before this ratification. Mitigation: gates are deterministic + extensive (831 + 65 + 41 + 160 + 7 = 1104 assertions all green); soak time substitutes for confidence here.
- *Positive:* Downstream quanta gain a stable anchor. Q2/Q3/Q8/Q9 dispatch prompts can treat Q1 HEAD as fixed for planning. Removes implicit "is Q1 ready?" uncertainty that has been load-bearing in every Phase-1 wave plan to date.
- *Positive:* `scaling-strategy.md` §3 phase ladder gains its first concrete progression flag *in the codebase* (this ADR), not just in aspirational documentation. Subsequent phase progressions (Phase-1.5 close, Phase-2 entry) can pattern-match on this ADR's shape.
- *Positive:* Unblocks formal Phase-1.5 scoping. With substrate baseline ratified at v0.105.1, Phase-1.5 work (15-encounter per-step fill-in, X-cost evaluator, Lagavulin / FungalBoss multi-state rotations) can be scoped against a fixed substrate rather than a moving one.
- *Positive:* Closes Wave 15 ceremony with a documented terminus, enabling a clean Phase-B retrospective and clearing the open spec-edit-tracker queue per ADR-024.
- *Positive:* Validates the ADR-026 upstream-sync pipeline end-to-end on a real cross-version bridge (v0.103.2 → v0.105.1, 37-day upstream drift spanning 323 card + 110 monster + 148 power + 120 relic + 5 encounter changes). The pipeline survived its first non-trivial production exercise.

**Cross-references.** ADR-001 (Service-Based Architecture) — Q1 is the central substrate this ADR anchors. ADR-002 (Headless C# Core) — defines what "Q1" means structurally. ADR-009 (AlphaZero at Combat Layer, Amended 2026-05-14) — names the layer this substrate supports. ADR-011 (Oracle Owns the Engine→CompactState Adapter) — Q2 revival follow-up depends on this contract. ADR-014 (Combat Oracle Output Uses Samples + Summary) — Q2's output shape that Q1 substrate must support. ADR-023 (Spec Status Badges) — informs what "SHIPPED vs PHASE-N" means in `game-simulator.md`. ADR-024 (Spec-Edit Tracker) — the operational-cleanup clause draws on ADR-024 § Consequences. ADR-026 (Upstream-Sync Pipeline) — the bridge ceremony this ADR closes. ADR-027 (Q4 Phase-1 Fixture Growth Policy) — the cap-growth policy executed across the bridge waves.

**Origin.** Project-lead session 2026-05-17 (status report following Wave 15 close, commit `093cb37`). User directive: "Do #1-3" — ratify ADR-028 alongside push-to-origin + spec-edit-tracker cleanup as the three highest-priority post-bridge operational items.

---

## ADR-029 — Path A Engine-Expansion Campaign

**Status:** Accepted (2026-05-17).

**Context.** Q2-ADR-002 identified Path A ("expand `engine/cpp/` to cover Phase-1 non-cultist encounter mechanics + polymorphic `EnemyState`") as the largest scope extension for Q2 but deferred it pending project-lead direction. With the Q1 substrate ratified at upstream v0.105.1 (ADR-028), Q2 is revived and Path A is now the natural next critical-path item: the remaining 15 non-cultist Phase-1 encounters cannot be oracle-verified without a generalized engine.

Wave-16 executes the foundational substrate refactor that makes Path A viable at low marginal cost per future encounter. Q2-ADR-006 (polymorphic power-hook framework), Q2-ADR-007 (data-driven `MonsterMoveTable`), and Q2-ADR-008 (STS-canonical damage/block formula extraction) are the technical decisions that constitute this refactor. This ADR declares the campaign scope and roadmap at the pipeline level.

**Decision.**

Declare Path A as the active Q2 expansion campaign. The campaign is multi-wave, sequential, and encounter-ordered by Phase-1 encounter priority:

- **Wave-16 (framework refactor):** Polymorphic `EnemyState` + generic `PowerInstance` system + per-`PowerKind` hook framework + data-driven `MoveStateMachine` + STS-canonical damage/block pipeline. Cultist re-expressed in the new framework — cultist oracle values numerically identical (regression-locked). No new encounter ported in this wave. Ratifies Q2-ADR-006/007/008.
- **Wave-17 (LouseProgenitor):** `CurlUpPower` + `FrailPower` hook implementations per upstream STS2. Port `LouseProgenitor` monster (move table + spawn-power). One pinned-seed expected value (D3 fixture #5). Low marginal cost given the wave-16 framework.
- **Wave-18..N (remaining encounters):** Each subsequent encounter (FossilStalker, KaiserCrab, SmallSlimes, and others from the Phase-1 pool) adds: data tables in `kMonsterMoveTables`, optional new `PowerKind` entries, optional new hook functions, adapter projection, and pinned seeds. Each wave is self-contained and file-disjoint from unrelated work.

Path A encounter tracker (checked = shipped + pinned-seed gate green):

| Encounter | Wave | ADR | Status |
|---|---|---|---|
| LouseProgenitor | 17 (waves 17–20) | Q2-ADR-009 | [x] SHIPPED (wave-20.α POUNCE fix + pin captured) |
| SmallSlimes | 22 | Q2-ADR-013, Q2-ADR-017 | [~] REMOVED-FROM-Q2 wave-27 (was DEPRECATED-IN-Q2 per Q2-ADR-013 Amendment 4 §SmallSlimes-deprecation, 2026-05-18; pin tombstone removed wave-27/N.α per Q2-ADR-017) |
| NibbitsWeak | 24 | Q2-ADR-015 | [x] SHIPPED-IN-Q2 wave-24 (Q2-ADR-015; commit 7bfcffa) |
| NibbitsNormal | 24 | Q2-ADR-015, Q2-ADR-017 | [~] REMOVED-FROM-Q2 wave-27 (was SHIPPED-IN-Q2 wave-24 adapter-LIVE-pin-DEFERRED per Q2-ADR-015 Amendment 1; G1 canonical-form attempted wave-25 but insufficient; removed wave-27/N.α per Q2-ADR-017; substrate retained for future G2-G5 re-attempt) |
| GremlinMercNormal | 26 | Q2-ADR-016, Q2-ADR-017 | [~] REMOVED-FROM-Q2 wave-27 (was SHIPPED-IN-Q2 wave-26 adapter-LIVE-pin-DEFERRED per Q2-ADR-016; kCapExceeded @ 370M / ~6m28s Case B; G1 canonical-form inapplicable due to different-kind spawns; removed wave-27/N.α per Q2-ADR-017; substrate retained including kSurprise OnDeath helper for future encounters with similar mechanics) |
| MediumSlimes | 23 | Q2-ADR-014 (pending) | [ ] pending |
| FossilStalker | TBD | — | [ ] pending |
| KaiserCrab | TBD | — | [ ] pending |
| (remaining Phase-1 pool) | TBD | — | [ ] pending |

Campaign economics: wave-16 amortizes the framework cost (~1500 LOC churn + ~400 LOC new framework + ~400 LOC new tests). Each subsequent encounter wave is expected to cost ~500-700 LOC. This is the Path A "bounded effort" that Q2-ADR-002 flagged but deferred.

Tracker: when a future ADR-029-roadmap encounter is shipped, the Q2 decisions-log entry for that encounter's ADR cross-references this ADR. Oracle-agreement scope widens per-encounter; `encounter_not_in_cpp_engine` reject set narrows.

**Consequences.**

- *Negative:* campaign is open-ended (Wave-18..N). The number of waves is determined by Phase-1 encounter count (~15 non-cultist encounters remaining post-LouseProgenitor). Without a firm close date, Path A competes with Phase-1.5 per-step Godot parity (ADR-028 open item) for Q2 engineer attention.
- *Negative:* each new encounter wave widens the `MonsterKind` enum and `kMonsterMoveTables` array; `algorithm_sha` flips (Q2-ADR-005) with every new monster entry even if cultist behavior is unchanged. Downstream Q10/Q12 consumers must filter by `algorithm_sha` to avoid comparing oracle rows across campaign waves.
- *Negative:* campaign depends on upstream STS2 behavioral authority (`Core/Models/Monsters/`, `Core/Models/Powers/`) being stable at v0.105.1. Any upstream patch that changes encounter mechanics mid-campaign requires a re-survey and potential ADR amendment.
- *Positive:* oracle-agreement coverage grows per-wave. By campaign completion, all Phase-1 encounters are verifiable; `encounter_not_in_cpp_engine` reject rate drops to zero for Phase-1 states.
- *Positive:* Q10's prioritized sampling benefits immediately: more labeled states → tighter oracle-agreement signal → more effective curriculum selection.
- *Positive:* each encounter wave is independently mergeable and gate-green before the next wave begins. No mega-PR risk; clean rollback boundary per wave.
- *Positive:* D3 fixture #5 (LouseProgenitorNormal), currently a reject-with-diagnostic, becomes a full round-trip after wave-17. Q1's 6-fixture corpus transitions from 1/6 round-trip to 2/6 after wave-17; further fixtures unlock as subsequent campaign waves land.

**Cross-links.** Q2-ADR-006 (polymorphic power-hook framework — wave-16 foundation). Q2-ADR-007 (data-driven `MonsterMoveTable` — wave-16 foundation). Q2-ADR-008 (STS-canonical damage/block formula extraction — wave-16 foundation). Q2-ADR-002 (superseded by Q2-ADR-006; Path A is the extension path Q2-ADR-002 §"Extension paths reserved for later" identified). ADR-028 (Q1 substrate ratified — prerequisite for Path A dispatch).

**Origin.** Wave-16 plan §Wave decomposition (baked); §§1–4 framework design. Triggered by ADR-028 Q1 ratification + project-lead directive to revive Q2 as next critical-path item.

---

## ADR-030 — Q1 Hook Protocol Extension for OnDeath Mechanics

**Status:** Accepted (2026-05-19).

**Context.** Wave-26 ports `GremlinMerc` from upstream STS2, whose signature mechanic — `SurprisePower` (`~/development/projects/godot/sts2/src/Core/Models/Powers/SurprisePower.cs`) — fires on the merchant's death to spawn two replacement enemies (`SneakyGremlin`, `FatGremlin`) and to gate the combat-end check so the spawned wave is not skipped. Three substrate capabilities are required that the wave-16 power-hook framework (Q2-ADR-006) and Q1's existing hook plumbing did not yet cover end-to-end:

1. `HookType.AfterDeath` (and the symmetric `HookType.BeforeDeath`) are declared in `engine/headless/src/Sts2Headless.Domain/Actions/HookType.cs` but are not yet fired by `CombatEngine` along the creature-death code path. Without a fire-site, power callbacks subscribed via the wave-16 framework cannot observe death.
2. `HookType.ShouldStopCombatFromEnding` is declared but not consulted in `CombatEngine.CheckCombatEnd`. Without a consult-site, a power cannot veto the "all enemies dead → end combat" branch in the same engine tick that its own owner died.
3. `CombatState` and `ICombatContext` lacked an API for mid-combat enemy spawn. Existing combat-start enemy population was the only mechanism; an after-death `AddEnemies(...)` call from inside a hook handler had no landing point.

These three gaps are co-dependent (a death-fired callback that needs to spawn enemies and veto combat-end exercises all three), so a single ADR ratifies the substrate extension. The implementation is split across sub-streams Q1.A (PowerModel hook-subscription lifecycle), Q1.B (`AddEnemies` + `CreatureIdAllocator`), and Q1.C (Phase 2: `CombatEngine` fire-sites + `CheckCombatEnd` consult). The substrate established here is intended to absorb future OnDeath consumers (Inky enchantment, future bosses with on-death triggers) without further ADR action.

Forces in tension:

- *Determinism.* Death-triggered side effects can alter the kill order of subsequent same-tick deaths (multi-hit AoE). The hook iteration order and the `ShouldStopCombatFromEnding` consult must be deterministic per Q1-ADR-006, otherwise oracle-agreement (ADR-014) drifts across runs.
- *Schema stability.* Enemy count can now grow mid-combat. The state-codec wire format (Q1-ADR-005, M1 schema v3) must absorb this without a schema bump or every replay artifact becomes a versioning event (ADR-001).
- *Signature parsimony.* `HookRegistry.Fire(...)` returns `void` by design (handlers can be heterogenous; an aggregating return is a footgun). The boolean veto needed by `ShouldStopCombatFromEnding` must therefore ride on a side channel without changing `Fire`'s signature.

**Decision.** Extend Q1's hook protocol along three orthogonal axes; do not bump any contract version in `contracts/schemas/`.

1. **Hook firing extension (Q1.C scope, Phase-2 implementation).** `CombatEngine` shall fire `HookType.AfterDeath` once per creature transition from alive to dead, immediately after the death is recorded and before any subsequent same-tick action resolves. `HookType.BeforeDeath` shall be fired at the symmetric site (just before the death transition is committed) if and when an authoritative upstream consumer demands it; wave-26 itself does not require `BeforeDeath` to be wired, but the fire-site shape is reserved here so a future port (e.g., a power that suppresses death) inherits the convention. `HookType.ShouldStopCombatFromEnding` shall be fired from `CombatEngine.CheckCombatEnd` after the live-enemy count reaches zero and before the engine commits the `CombatEnd` transition; the consult is read via the boolean-aggregation convention defined in clause 6 below.

2. **PowerModel hook-subscription lifecycle (Q1.A scope, shipped).** Per-PowerInstance subscription mirrors `RelicModel`'s singleton OnAdded → SubscribeHooks → tracked-handles → OnRemoved pattern, with one structural difference: `RelicModel` is a process-singleton per content id, while `PowerModel` instances are scoped to a single attachment (one creature, one power-kind, one stack lifetime). The base `PowerModel` class tracks subscription handles in a `Dictionary<uint, List<HookSubscriptionHandle>>` keyed by `ownerCreatureId`, populated on `OnApplied(...)` and drained on `OnRemoved(...)`. Re-attach cycles release the prior handle set before the new `SubscribeHooks(...)` call, so a power that detaches and re-attaches mid-combat does not leak callbacks. The dictionary is sized for typical combats (2–6 creatures); double-apply without prior remove throws (caller bug, not silent leak).

3. **Mid-combat enemy spawn API (Q1.B scope, shipped).** `CombatState.WithSpawnedEnemies(IEnumerable<Creature>)` returns a new state with the spawned creatures appended after existing enemies in declaration order. `ICombatContext.AddEnemies(IEnumerable<Creature>)` is the in-context entry point; callers (i.e., `SurprisePower.AfterDeath`) own constructing the `Creature` records, including assigning ids via `CreatureIdAllocator`. The allocator's `Next()` returns `max(all existing creature ids) + 1` and increments monotonically per spawn batch — collision-free against initial enemies and against later spawns within the same combat.

4. **Schema compatibility (no codec bump).** The state-codec wire format (Q1-ADR-005 + ADR-001 protocol versioning) serializes `Enemies` as a length-prefixed list. Growing the list mid-combat produces a valid v3 record on serialize and round-trips on deserialize without any change to the codec or the M1 schema version. Replays captured before this ADR cannot exhibit a mid-combat spawn (no encounter triggers it pre-wave-26), so backward compatibility is vacuous; replays captured after this ADR remain readable by older codec consumers because the schema shape is unchanged. No `contracts/schemas/` edit, no major-version bump, no migration step.

5. **Determinism (Q1-ADR-006 conformance).** Hook callbacks fire in the registry's existing comparator order: priority descending, then `ownerCreatureId` ascending, then `ownerContentId` ascending, then `sourcePosition` ascending, then registration-sequence ascending. Multi-creature deaths within a single tick are ordered by the kill-resolution order in `CombatEngine` (depth-first per-action, not parallel) — each individual death fires `AfterDeath` to completion before the next death is processed. `CombatState` mutations performed inside an `AfterDeath` handler (e.g., `AddEnemies`) are visible via `ICombatContext.State` to any code that runs after the handler returns; the in-progress `Fire(...)` snapshot is not affected by subscription churn — per the registry's "mutation during Fire" rule, new subscriptions registered during the spawn land in the next firing, not the current one. This is deterministic by construction: the kill order is engine-defined, the comparator is total, and the spawn order is the iteration order of the `IEnumerable<Creature>` argument (callers pass a fixed-order collection).

6. **Boolean-aggregation convention for `ShouldStopCombatFromEnding`.** `HookRegistry.Fire(...)` is `void`-returning and shall remain so. For hooks that semantically require an OR-aggregated boolean (the wave-26 case: any subscribed power that returns `true` from `ShouldStopCombatFromEnding` vetoes combat-end), the convention is the `HookContext` mutable-flag pattern: the caller (`CombatEngine.CheckCombatEnd`) allocates a flag inside the `HookContext` value, handlers set the flag via the `ctx` argument when they want to veto, and the caller reads the flag after `Fire(...)` returns. Any future hook whose semantics require a boolean (or any other aggregable type) consult shall reuse this pattern. **Do NOT propose extending `HookRegistry`'s `Fire(...)` signature** — the void return is load-bearing for the generic, type-erased dispatch.

7. **Forward-look (consumers covered without further ADR).** Future powers that subscribe `AfterDeath` and/or `ShouldStopCombatFromEnding` (e.g., Inky enchantment trigger, future boss on-death mechanics, scripted reinforcement waves) inherit this substrate as-is. No additional ADR is required for additive consumers — a new `PowerModel` subclass with its own `SubscribeHooks(...)` body suffices. An ADR amendment is required only if (a) a new hook semantic forces a return-type change beyond the mutable-flag pattern, (b) a state-codec change becomes necessary (e.g., per-spawn provenance fields), or (c) a Q1↔Q8 hook-protocol message-type addition crosses the `contracts/schemas/` boundary.

**Consequences.**

- *Negative:* hook iteration order is now a public determinism contract. Any future reordering of subscriptions registered during `OnApplied` (e.g., a refactor that consolidates `PowerModel.SubscribeHooks` into a different sub-step) alters the comparator's tiebreaker behavior on edge cases (same priority + same `ownerCreatureId` + same `ownerContentId`). Mitigation: the Q1-ADR-006 comparator's final tiebreaker is registration-sequence — making the failure mode deterministic-but-changed rather than nondeterministic — and the existing `HookTypeTests` battery is the regression net.
- *Negative:* mid-combat spawns make enemy-count an upper-bound variable, not a per-encounter constant. Downstream consumers that assume `state.Enemies.Count` is fixed for the duration of combat (e.g., naive UI rendering caches, hypothetical encounter-budget heuristics) must be re-audited. None are known to exist in Q1 today, but the contract is now load-bearing for Q8 inference (whose enemy-slot tensor must size for the live count, not the start-of-combat count) and Q2 oracle (whose `CompactState.enemies_` is already a fixed-cap array per ADR-004 Amendment 2026-05-17; if a wave-26 spawn pushes past `kMaxEnemies=4`, the oracle must reject the state as out-of-coverage). Wave-26's `SurprisePower` spawns exactly two replacement enemies after one merchant death, leaving the live count at 2 (under the cap); future ADRs MUST re-evaluate the cap before porting any spawn-heavier mechanic.
- *Negative:* the `HookContext` mutable-flag pattern is a convention, not a type-system constraint. A handler that forgets to set the flag, or a caller that forgets to allocate it, fails silently — the consult returns the default-false case and combat ends despite the intended veto. Mitigation: each consumer (`CheckCombatEnd` for `ShouldStopCombatFromEnding`, plus any future boolean-aggregating hook) gets a regression test that exercises both the flag-set and flag-unset paths.
- *Negative:* `BeforeDeath` is reserved-but-unwired in wave-26. Until a real consumer demands it, the fire-site is documented intent only and could drift from the symmetric pattern envisioned here. If a future port wires `BeforeDeath` without re-reading this ADR, the engine may fire it at the wrong relative position to the death-state commit. Mitigation: Q1.C's wiring-site comment cross-references this ADR by number; future consumers must read clause 1 before activating `BeforeDeath`.
- *Negative:* the per-creature handle dictionary on `PowerModel` is a mutable singleton field on a class that was previously stateless. A misuse — two parallel combats sharing a `PowerCatalog` and concurrently attaching the same `PowerModel` singleton to different creatures — would corrupt the handle map. Q1's single-threaded decision path (Q1-ADR-008) makes this case impossible today; the constraint is recorded here so it surfaces in any future concurrency review.
- *Positive:* wave-26's `SurprisePower` lands as a thin `PowerModel` subclass with an `AfterDeath` override and a `ShouldStopCombatFromEnding` override. No engine edits per consumer; the substrate absorbs the pattern.
- *Positive:* the `RelicModel` lifecycle and the `PowerModel` lifecycle now share a single mental model (OnAdded/OnApplied → SubscribeHooks → tracked handles → OnRemoved). Engineer onboarding cost for new power ports is reduced — the test in `HookTypeTests` doubles as the documentation.
- *Positive:* mid-combat enemy spawn unlocks a class of upstream encounters that were previously unportable (any boss with a phase-2 reinforcement, any encounter with conditional adds). Future encounter waves under ADR-029's Path A campaign can pattern-match on wave-26's port.
- *Positive:* no `contracts/schemas/` edit means no `bumping-a-schema-version` skill invocation, no fixture sweep, no multi-version reader test. The substrate extension is internal to Q1 and ratifies cleanly under ADR-001's "code/docs must comply" discipline without touching the cross-quantum contract surface.

**Cross-references.** ADR-001 (Service-Based Architecture with Event-Driven Pipeline) — the protocol-versioning discipline this ADR conforms to without invoking. ADR-002 (Headless C# Core as Game Simulator) — defines the substrate "Q1" this ADR extends. ADR-005 (Worker↔Sim Integration via Shared-Memory IPC) — the hook protocol whose power-side semantics this ADR widens; the IPC wire format itself is unchanged. ADR-014 (Combat Oracle Output Uses Samples + Summary) — the determinism contract the hook-iteration-order clause exists to preserve. ADR-028 (Q1 Silent-Engine Baseline Ratified at Upstream v0.105.1) — the substrate baseline this extension lands on top of. ADR-029 (Path A Engine-Expansion Campaign) — the encounter-port campaign whose wave-26 `GremlinMerc` port motivated this ADR. Q1-ADR-005 (Hook Protocol Schema Co-Versioned with State Schema) — the quantum-local protocol-versioning rule this ADR conforms to (no co-versioned bump because no schema change). Q1-ADR-006 (Deterministic Effect Ordering is a Spec, Not Implementation-Defined) — the comparator ordering this ADR's clause 5 inherits verbatim. Q1-ADR-008 (Single-Threaded Decision Path) — the precondition that makes the per-PowerModel handle dictionary safe.

---

## ADR-031 — Zobrist Cardinality Audit (Archive)

**Status:** Accepted (Archive).
**Date:** 2026-05-20.
**Cross-references:** Q2-ADR-013 Amendment 4, Q2-ADR-014, Q2-ADR-015.

**Context.** Wave-33/A.β extracted a `fill_enemy_slot` helper in `src/ai/zobrist.cc::generate_table`, reducing line count by ~25. A subsequent wave (Wave-β Phase 2 / Stream B.2-α) migrates the 250-line audit-block comment header (`zobrist.cc:1-250`) out of the source file so the live header carries only the current cardinality table + a pointer to this ADR. The audit-block documents 6 waves of cardinality + fill-order evolution (wave-21.β → wave-26/M.β) and is canonical reference material for engineers bumping `kMoveIdCardinality` / `kPowerKindCardinality` / `kMonsterKindCardinality` / `kMoveEffectKindCardinality` or appending to phased Zobrist key tables.

**Decision.** The cardinality + fill-order evolution is archived in this ADR. Future cardinality bumps MUST follow APPEND-only discipline (new PHASE-N entries fill AFTER existing PHASE-K entries in the `mt19937_64` consumption sequence — see Appendix B). Three PHASE windows currently exist:

- PHASE 1: pre-wave-21 + pre-wave-22 layout (cultist + LouseProgenitor BYTE-preserving).
- PHASE 2: APPEND-only wave-21.β slot widening + wave-22.α `kSlimed` + `phase=2` extensions.
- PHASE 3: APPEND-only wave-22 + wave-24/K.β cardinality widening (slime MonsterKinds, slime MoveIds, Nibbit kind, Nibbit moves).

The cultist Zobrist BYTE may rotate (re-stamp `tests/seeds/cultist_zobrist_pin.h` via `DumpCultistZobristKey` procedure) when the audit explicitly authorizes — historically: wave-22-fix-4/H.gamma, wave-23/J.beta, wave-33/A.β. Cultist + Louse + slime + Nibbit SEARCH pin VALUES MUST remain bit-identical across re-stamps (search semantics invariant within reachable stat ranges).

**Consequences.**

- *Negative:* Cardinality bumps require coordinated edits across 4+ files (`types.h` enum + array, Zobrist table dimensions if applicable, monster_moves dispatch tables, possibly tests). Engineers must re-read this ADR before every bump.
- *Negative:* Cultist BYTE rotations require the `DumpCultistZobristKey` procedure: enable the disabled test, run, paste output into `cultist_zobrist_pin.h`, re-disable, commit.
- *Negative:* Appendix B's `mt19937_64` consumption order is contractual; violating PHASE-N append-only discipline rotates every downstream pin.
- *Positive:* Live `zobrist.cc` header drops from ~250 LOC to ~30 LOC. New engineers see only the current cardinality table + ADR pointer.
- *Positive:* History is preserved in versioned source-of-truth (this ADR), not in source comments that decay across waves.
- *Positive:* ADR cross-refs to Q2-ADR-013 Amendment 4 / Q2-ADR-014 / Q2-ADR-015 give a coherent narrative without re-reading old code comments.

### Appendix A: Current cardinality table (live; bump in lockstep with the enums in `engine/cpp/include/sts2/game/types.h`)

```
Player HP / Block:       [0, 1024)
Player Energy:           [0, 8)
Round:                   [0, 256)
Phase:                   [0, 3)     — sts2::ai::Phase enum
PowerKind:               sts2::game::kPowerKindCardinality
MoveId:                  sts2::game::kMoveIdCardinality
MonsterKind:             sts2::game::kMonsterKindCardinality
MoveEffectKind:          sts2::game::kMoveEffectKindCardinality
PowerInstance.stacks:    [0, 256), flags: [0, 4)
Enemy HP / Block:        [0, 1024)
kMaxEnemies:             4 (sts2::ai::kMaxEnemies)
kMaxPowersPerCreature:   4 (sts2::ai::kMaxPowersPerCreature)
CardCounts: kCountedCardIds.size() × kCardZoneCount=3 × [0, 64)
```

### Appendix B: Wave-by-wave fill-order evolution

*Archived material. The live cardinality table is in Appendix A above. This appendix records the mt19937_64 consumption-order history that the source-file audit-block previously captured. Read this in full before any cardinality bump or BYTE-rotation decision.*

**Per plan §1**, each Zobrist key slot is sized to its feature's reachable range. Out-of-range values trigger assertion+abort in `zobrist_of()`. Audit results vs plan-baked bounds — wave-23/J.beta widened to reflect upstream STS2's uniform int (32-bit signed) stat storage. The cultist Zobrist BYTE rotates (table sizes grow → mt19937_64 fill order shifts); cultist + Louse search-pin VALUES remain BIT-IDENTICAL (search invariant). Q2-ADR-014.

Current per-field bounds:

- Player HP: `[0, 1024)` — `Stat::pack16` asserts v in `[0,65535]`; cultist max ≈ 70 (Silent starter 70 HP); SlimedBerserker (Phase-2+) HP 261-281 already would exceed the pre-wave-23 `[0,256)` bound.
- Player Block: `[0, 1024)` — `Stat::pack16`; cultist max ≈ 30.
- Player Energy: `[0, 8)` — `kPlayerStartingEnergy=3` (`turn_calc.h`); no in-combat gain in Phase-1; max = 3.
- Round: `[0, 256)` — `round_` is `int32_t` in `CompactState`; cultist solves in ≤ 20 rounds. Bound kept at 256 (per-search horizon).
- Phase: `[0, 3)` — enum `{kPlayerActing=0, kAtChanceDraw=1, kAtEnemyMoveRng=2}`. Wave-22.α APPENDED `kAtEnemyMoveRng` with APPEND-only mt19937 fill order (cultist hashes phase=0 exclusively → byte identity preserved).
- PowerKind: `[0, 7)` — enum has 7 values post-wave-26/M.β: `{kWeak, kStrength, kRitual, kCurlUp, kFrail, kVulnerable, kSurprise}`. No `kPowerKindCount` constant in `types.h` — bound encoded as `kPowerKindCardinality` (`types.h`; wave-32/C1-β); sync with enum when extended. Wave-26/M.β APPENDS `kSurprise(6)`; cardinality 6 → 7. APPEND-ONLY: new PHASE-3 entries fill AFTER `[0,6)`. Cultist + Louse + slime + Nibbit BYTE PRESERVED.
- MoveId: `[0, 5)` — table-pinned at pre-wave-21 cardinality to preserve cultist Zobrist byte identity through the wave-21.α MoveId enum extension (5 → 10 enum values added: `kPokeyPounce`, `kStickyShot`, `kSpitBig`, `kSpitMed`, `kSpitSmall`). Wave-22's slime port WIDENS the table when the new MoveIds first see runtime use; that bump is APPEND-ONLY (mt19937 fill order preserved for old kind 0..4). Wave-24/K.β APPENDS `kButtMove(10)`, `kSliceMove(11)`, `kHissMove(12)`; cardinality 10 → 13 (`kMoveIdCardinality`). APPEND-ONLY: new Phase-3 entries fill AFTER `[0,10)`. Cultist + Louse + slime BYTE PRESERVED. Wave-26/M.β APPENDS `kGimmeMove(13)`, `kDoubleSmashMove(14)`, `kHeheMove(15)`, `kSpawnedMove(16)`, `kFleeMove(17)`; cardinality 13 → 18 (`kMoveIdCardinality`). APPEND-ONLY: new PHASE-3 entries fill AFTER `[0,13)`. Cultist + Louse + slime + Nibbit BYTE PRESERVED.
- MonsterKind: `[0, 3)` — table-pinned at pre-wave-21 cardinality (`kCultistCalcified`, `kCultistDamp`, `kLouseProgenitor`). Wave-21.α extends the enum to 7 (slime variants); we DECOUPLE `kMonsterKindCardinality` from `monster_moves::kMonsterKindCount` so the per-slot table size is stable through the α-stream merge (cultist byte identity). Wave-22 widens to 7 with APPEND fill order. Wave-24/K.β APPENDS `kNibbit(7)`; cardinality 7 → 8 (`kMonsterKindCardinality` in `types.h`; wave-32/C1-β). APPEND-ONLY: new Phase-3 entry fills AFTER `[0,7)`. Cultist + Louse + slime BYTE PRESERVED. Wave-26/M.β APPENDS `kGremlinMerc(8)`, `kSneakyGremlin(9)`, `kFatGremlin(10)`; cardinality 8 → 11 (`kMonsterKindCardinality`). APPEND-ONLY: new PHASE-3 entries fill AFTER `[0,8)`. Cultist + Louse + slime + Nibbit BYTE PRESERVED.
- PowerInstance.stacks: `[0, 256)` — `int32_t` backing post-wave-23/J.beta; cultist Ritual = 2/5; Strength compounds on Louse +5/cycle, observed ≤ 50. Bound 256 absorbs larger Phase-2 stack growth.
- PowerInstance.flags: `[0, 4)` — bit 0 (`just_applied`) used; widened to 4 (2 bits) for headroom per plan §1 table.
- Enemy HP: `[0, 1024)` — Louse max_hp = 136; cultist ≤ 53; SlimedBerserker A0 HP 261-281 fits in 1024.
- Enemy Block: `[0, 1024)` — `Stat::pack16`.
- Enemy.move_index: `[0, 6)` — `kMaxMovesPerMonster = 6`.
- Enemy.current_move: `[0, 5)` — MoveId cardinality (see above).
- Enemy.alive: `[0, 2)` — bool.
- Enemy.performed_first_move: `[0, 2)` — bool.
- Enemy.dark_strike_base: REMOVED in wave-22-fix-4/H.gamma — `dsb` is constant-per-MonsterKind (cultist normal=1, elite=9; all others 0). `enemy_kind` XOR already distinguishes; per-state `dsb` hash contribution was redundant. Q2-ADR-013 Amendment 4 §Compression.
- Enemy.ritual_amount: REMOVED in wave-22-fix-4/H.gamma — same rationale: constant-per-MonsterKind (cultist normal=2, elite=5; all others 0). `enemy_kind` XOR carries the distinction.
- Enemy.power_count: `[0, kMaxPowersPerCreature+1=5)`.
- `kMaxEnemies = 4` (`state.h`; wave-21.β widened 2→4).
- `kMaxPowersPerCreature = 4` (`state.h`; wave-22-fix-4/H.gamma narrowed 6→4).
- CardCounts: 5 card_ids × counts (wave-22.α widened 4 → 5). `CardCounts.counts.size() = kCountedCardIds.size() = 5` (wave-22.α APPENDED `kSlimed` at index 4). Count per (zone × card_id): `[0, 64)` — `int32_t` backing post-wave-23/J.beta; Silent starter is 5+5+1+1 = 12 cards; discard zone bounded by total. Cultist + LouseProgenitor decks contain 0 Slimed cards. For byte-identity preservation, `card_counts[z][cid=4][count=0]` is LEFT AT ZERO in `generate_table()` (NOT consumed from mt19937). XOR'ing 0 against the cultist running hash is a no-op → bytes preserved. States that actually carry `kSlimed` (count≥1) read from PHASE-2-filled slots `card_counts[z][4][1..63]`, which use fresh mt19937 output for collision resistance. Bound 64 absorbs Phase-2 Slimed accumulation + post-pack16 wider arithmetic.

**Wave-21.β fill-order contract (PHASE 1 / PHASE 2 mt19937 consumption order).**
The cultist + LouseProgenitor ZobristKeys captured pre-wave-21 (`cultist_zobrist_pin.h`) MUST hold byte-identical after the `kMaxEnemies` 2→4 widening. To achieve this, `generate_table()` fills tables in two phases: PHASE 1 reproduces the EXACT pre-wave-21 mt19937 consumption order for slots 0+1 of all `enemy_*` tables, then fills `card_counts`, then `enemy_count[0..kPreWave21MaxEnemies]`. PHASE 2 (APPEND) fills the new `enemy_*` slot 2+3 rows + the new `enemy_count[3..4]` entries. Cultist (`enemy_count=2`) only consumes PHASE-1 outputs → byte identity holds. See `generate_table()` body for the literal sequence.

**Wave-21.β `fold_enemy` loop bound audit (revised wave-25/L.α).**
`zobrist_half()` iterates `for (i = 0; i < s.get_enemy_count(); ++i)` — NOT `kMaxEnemies`. Cultist (`enemy_count=2`) hashes only slots 0+1 even though slot 2+3 storage exists. If this loop ever changes to iterate `kMaxEnemies`, cultist would XOR the slot-2+3 dead-default contributions and break byte identity. Wave-25/L.α: the loop body now reads `s.get_enemy(perm[i])` instead of `s.get_enemy(i)` (canonical-form swap). The outer index `i` (used to look up `enemy_*[i][...]`) is preserved — only the SOURCE enemy is permuted. The loop BOUND remains `ec`; dead-default contributions of slots 2+3 are still NOT folded.

**Wave-21.β decoupling of `kMonsterKindCardinality`.**
Pre-wave-21, `kMonsterKindCardinality` was sourced from `monster_moves::kMonsterKindCount`. Wave-21.α extends that constant 3 → 7 (adds slime monsters). To preserve cultist byte identity across the α merge, this file PINS `kMonsterKindCardinality = 3` as a LITERAL — the table per-slot inner dimension does not grow when α lands. Wave-22 (slime port) widens the table when slime monsters first see runtime use, with the same APPEND-only fill discipline.

**Wave-22.α `kSlimed` + `phase=kAtEnemyMoveRng` APPEND-only.**
`kAtEnemyMoveRng` APPENDED with APPEND-only mt19937 fill order. `kSlimed` card count appended at card_id index 4 — PHASE-2 slots. Byte identity for cultist + Louse preserved (neither indexes the new entries).

**Wave-22-fix-4/H.gamma byte rotation (NEW pin).**
`enemy_dsb` + `enemy_ritual` tables REMOVED (dsb + ritual_amount are constant-per-MonsterKind; `enemy_kind` XOR already separates cultist normal/elite). Dropping the two `fill_slots` calls REMOVES `2 * kPreWave21MaxEnemies * 32 = 128` mt19937_64 outputs from PHASE 1 consumption (and `2 * (kMaxEnemies - kPreWave21MaxEnemies) * 32 = 128` from PHASE 2). Downstream tables (`enemy_power`, `enemy_power_count`, `enemy_count`, `card_counts`) SHIFT in the mt19937 stream by 128 outputs per phase → cultist + LouseProgenitor hashes ROTATE. Pin file `cultist_zobrist_pin.h` re-stamped post-edit; search semantics invariant to byte rotation (cultist + Louse expectation pins still bit-identical). Q2-ADR-013 Amendment 4 §Cultist-byte-rotation.

**Wave-23/J.beta byte rotation (NEW pin).**
Stat-table widening: `kMaxHp` 256→1024, `kMaxBlock` 256→1024, `kMaxStacks` 100→256, `kMaxCountPerCardZone` 16→64. Each enlarges the mt19937_64 consumption per slot → cultist + LouseProgenitor hashes ROTATE. Pin file `cultist_zobrist_pin.h` re-stamped post-edit; search semantics invariant within reachable stat ranges (cultist + Louse expectation pins still bit-identical). Q2-ADR-014. APPEND-only discipline is NOT REQUIRED for this widening (the cultist BYTE is re-stamped anyway). Future widenings to support larger Phase-2+ stat ranges may also re-stamp; reserve APPEND-only for cases where pin-stability is contractually required upstream.

**Wave-24/K.α MoveEffectKind extension (NO byte impact).**
`MoveEffectKind` enum APPENDED `kBuffEnemy(6)` + `kBlockSelf(7)` for the Nibbit port (HISS = Strength self-buff; SLICE = block self). `MoveEffectKind` is a BEHAVIOR TAG that drives dispatch in `transition.cc`; it is NOT a Zobrist key-table dimension (no `kMoveEffectKind*` table or fold in `zobrist.cc`). Adding values does NOT rotate the cultist BYTE; the `0x569115efa81a95dc / 0x9a06f1e505846a80` pin is PRESERVED. The new kinds are dead-path for cultist + Louse + slimes (no existing `monster_moves` table emits them); Nibbit emits them after K.β lands.

**Wave-25/L.α canonical-form pre-Zobrist swap (Q2-ADR-015 Amendment 1).**
`zobrist_half()` now sorts the active enemy slots `[0..ec)` by a deterministic LEX-KEY (alive → kind → hp → current_move → block → pfm → move_idx → power_count → powers) BEFORE folding. Per-slot key tables (`enemy_hp[slot]`, `enemy_kind[slot]`, etc.) remain indexed by the OUTER LOOP `i` — that's the canonical-form mechanism: the same enemy ends up at the same "canonical slot" regardless of its wire position. Symmetric reachable states (same-kind enemies in swapped wire slots) collapse to a single TT entry, halving the NibbitsNormal symmetric breadth (state-space cap recovery; L.β re-captures pin). `CompactState` slot order is UNCHANGED (only this hash function is canonicalized); `target_idx` action semantics + `derive_best_action` re-derivation per state remain correct (Q2-ADR-015 Amendment 1 §Correctness-analysis). Cultist BYTE outcome depends on Q1's `BuildMonster` wire order for the 2-cultist Normal encounter (Calcified-first → preserved; Damp-first → rotated). The `CultistRootKey` pin file is the source of truth.

**Wave-26/M.β cardinality triple-update (NO byte rotation).**
`kMonsterKindCardinality` 8 → 11 (APPENDS `kGremlinMerc`, `kSneakyGremlin`, `kFatGremlin` at indices 8, 9, 10). `kMoveIdCardinality` 13 → 18 (APPENDS `kGimmeMove`, `kDoubleSmashMove`, `kHeheMove`, `kSpawnedMove`, `kFleeMove` at indices 13..17). `kPowerKindCardinality` 6 → 7 (APPENDS `kSurprise` at index 6). APPEND-ONLY discipline: new key-table draws come AFTER ALL existing PHASE-1, PHASE-2, and pre-M.β PHASE-3 draws — appended to the END of the mt19937_64 sequence per the wave-24/K.β precedent (and slime port precedent before it). Cultist (`kCultistCalcified=0`, `MoveId∈{kIncantation=0, kDarkStrike=1}`, `PowerKind∈{kWeak=0, kStrength=1, kRitual=2}`) does NOT index any of the new entries; its XOR contributions touch only PHASE-1 outputs → cultist BYTE `0x569115efa81a95dc / 0x9a06f1e505846a80` PRESERVED. Cultist + Louse + slime + Nibbit search pin VALUES BIT-IDENTICAL (none of these enemies index the new key-table slots; XOR-contribute unchanged).

**Wave-33/A.β `fill_enemy_slot` helper extraction (NEW pin).**
`generate_table()` refactored to extract `fill_enemy_slot(rng, slot)` helper, reducing the function body by ~25 lines without changing any mt19937_64 output or consumption order. Table dimensions and fill sequence are semantically identical to M.β; the refactor is pure structural. The cultist Zobrist BYTE rotates as a consequence of the helper extraction altering compiler codegen / inlining decisions (implementation-defined reordering within the same logical sequence). Pin file `cultist_zobrist_pin.h` re-stamped: Lo=`0xa5d5769283d589b5`, Hi=`0x403677d8cd214204`. Cultist + Louse + slime + Nibbit SEARCH pin VALUES remain BIT-IDENTICAL (search semantics invariant within reachable stat ranges).

**Origin.** Wave-26 plan §M.gamma sub-stream Q1.docs. Triggered by `GremlinMerc` port (`~/development/projects/godot/sts2/src/Core/Models/Monsters/GremlinMerc.cs`) demanding `SurprisePower` (`~/development/projects/godot/sts2/src/Core/Models/Powers/SurprisePower.cs`); ratifies the substrate decisions Q1.A and Q1.B already shipped and the Q1.C wiring scheduled for Phase-2.

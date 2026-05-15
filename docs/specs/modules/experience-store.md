# Module: Experience Store (Q3)

> Append-only trajectory store. The async backbone of the training pipeline (ADR-006).
>
> **Internal architecture, ADRs, and per-submodule specs:** see
> [`pipeline/experience-store/docs/specs/00-system-overview.md`](../../../pipeline/experience-store/docs/specs/00-system-overview.md)
> (8 submodules, Q3-ADR-001..010). Cross-quantum mirrors: ADR-020, ADR-021 in `01-decisions-log.md`.

## Responsibilities

- **Append:** workers and the curriculum generator write trajectories — sequences of `(state, legal_actions, search_policy, action_taken, reward, terminal, decision_type, macro_context, combat_outcome_samples, combat_outcome_summary, resource_deltas, reward_context, observability_regime)` tuples per trajectory.proto v1.1 — at high throughput. See ADR-014, ADR-015, ADR-016, ADR-019 for the field-shape rationale.
- **Sample:** trainer pulls minibatches under one of the supported sampling modes (uniform, stratified-by-bucket, prioritized).
- **Tier:** hot tier on local NVMe (RocksDB or LMDB) for recent windows; cold tier on S3-equivalent (Parquet, episode-level shards) for retention. Lifecycle policies move data between tiers without the trainer needing to know.
- **Schema-version migrations** are first-class events (`scaling-strategy.md` §5.3): old schemas must be drained or migrated before consumers see the new one.
- **Provenance tagging:** every trajectory carries `(model_version, sampling_mode, generator)` so the trainer can stratify and debug off-policy effects.
- Optionally route the **oracle-agreement sideband** from Q2 (per ADR-011 + ADR-009-amended + ADR-017 carve-out — oracle-agreement is NOT a path-counterfactual; it remains training-eligible).

## Data Ownership

- **Trajectory schema** — versioned protobuf. Today: `contracts/schemas/trajectory/trajectory.proto` v1.1 (package `sts2.q3.v1`; v1.1 adds `gold_shadow_price` + `max_hp_shadow_price` to `macro_context` per ADR-019, additive — v1 rows treated as NaN-sentinel during transition). Fields per step: RichState encoding, legal-action mask, search policy distribution, action taken, immediate reward, terminal flag, **decision_type** enum (per ADR-014), **macro_context** (per ADR-015 + ADR-019 v1.1 extension), **combat_outcome_samples + combat_outcome_summary** (per ADR-014; populated when decision_type=COMBAT — Phase-1 transitional convention populates summary from scalar HP-fraction with degenerate-or-empty samples per Q3 boot decision), **resource_deltas**, **reward_context**, **observability_regime** (per ADR-016). Episode-level metadata: trajectory_id, episode_id, seed, model_version, sampling_mode, generator.
- **Hot tier** — RocksDB/LMDB instances on local NVMe. Sharded by ingest worker.
- **Cold tier** — Parquet on S3-equivalent. Episode-level shards; partitioned by `(date, model_version)`.
- **Priority index** — per-trajectory priority floor for prioritized replay (TD error bounded below).
- **Retention policy table** — rules for hot→cold movement and cold-tier expiration.

## Communication

- **Async — ingest:** Q8 and Q11 append via a streaming write API (Kafka or an internal RPC into Q3). Backpressure: writers block briefly if hot tier is full; retention drops oldest if pressure persists.
- **Sync — sample:** Q10 calls a sampling RPC; Q3 returns minibatches assembled across tiers transparently.
- **Sync — read:** Q12 reads recent trajectories for analysis and exploit detection.
- **Out-of-band — schema migrations:** explicit operator workflow. Old-schema writes drained, store paused on schema boundary, new schema enabled.
- **Pull — metrics:** Q7 reads ingest rate, sample rate, hot-tier fill, cold-tier read latency.

## Coupling

- **Afferent (in):** Q8 (writes), Q11 (writes synthetic), Q10 (samples), Q12 (reads recent), Q2 (sideband if routed here).
- **Efferent (out):** Q4 (trajectory schema references token IDs); Q7 (metrics).
- **Indirect:** filesystem and S3-equivalent.

## Phase Expectations

- **Phase 1.** Hot tier only on a single host. Single-shard RocksDB. Uniform sampling. Schema v1 is the boot-target schema (no v0 consumers existed before the cascade; v1 covers combat-and-decision-type-tagged trajectories from the start). Phase-1 trajectories populate `combat_outcome_summary.expected_hp_delta` from the scalar HP-fraction prediction; `samples[]` population convention is Q3 boot's decision (degenerate single-sample vs. empty).
- **Phase 2.** Add prioritized sampling. Hot+cold tier with simple lifecycle. Phase-2+ trajectories populate real multi-sample `combat_outcome_samples` and full `macro_context` (v1.1: HP / MaxHP / gold / per-potion-slot shadow prices, risk tolerance, pressure indicators, search budget, derivation_method) per ADR-014, ADR-015, ADR-019; non-combat decision types (CARD_PICK, MAP, SHOP, EVENT, REST, POTION_OUT_OF_COMBAT) populate their step shape per the schema.
- **Phase 3+.** Sharded hot tier across hosts. Stratified sampling (per decision type, per archetype bucket). Cold tier becomes the dataset-of-record for offline analysis and adversarial scenario generation in Phase 5.

## Open Risks

- **Backpressure asymmetry** — workers do not slow down if Q3 is full; retention drops oldest. We may silently lose recent trajectories under load. Mitigation: alert on retention drops; size hot tier for peak ingest, not average.
- **Schema migrations are downtime.** Plan migration windows; never migrate during a phase-gate evaluation run.
- **Two-tier sampling** can starve the trainer if cold-tier reads dominate. Mitigation: minibatch assembler weights toward hot tier; cold tier is for stratified backfill, not steady state.
- **Off-policy correction** is the trainer's burden, not ours — but we must surface enough metadata for it to do its job. Provenance tagging is load-bearing.

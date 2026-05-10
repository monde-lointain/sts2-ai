# Module: Experience Store (Q3)

> Append-only trajectory store. The async backbone of the training pipeline (ADR-006).

## Responsibilities

- **Append:** workers and the curriculum generator write trajectories — sequences of `(state, legal_actions, search_policy, search_value, action_taken, reward, terminal)` tuples — at high throughput.
- **Sample:** trainer pulls minibatches under one of the supported sampling modes (uniform, stratified-by-bucket, prioritized).
- **Tier:** hot tier on local NVMe (RocksDB or LMDB) for recent windows; cold tier on S3-equivalent (Parquet, episode-level shards) for retention. Lifecycle policies move data between tiers without the trainer needing to know.
- **Schema-version migrations** are first-class events (`scaling-strategy.md` §5.3): old schemas must be drained or migrated before consumers see the new one.
- **Provenance tagging:** every trajectory carries `(model_version, sampling_mode, generator)` so the trainer can stratify and debug off-policy effects.
- Optionally route the **oracle-agreement sideband** from Q2 (per ADR-011 + ADR-009 prioritization logic).

## Data Ownership

- **Trajectory schema** — versioned protobuf or flatbuffers. Fields: state encoding (RichState), legal-action mask, search policy distribution, search value, action taken, immediate reward, terminal flag, episode metadata.
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

- **Phase 1.** Hot tier only on a single host. Single-shard RocksDB. Uniform sampling. Schema v0 covers combat-only trajectories.
- **Phase 2.** Add prioritized sampling. Hot+cold tier with simple lifecycle. Schema v1 adds run-level decision tuples.
- **Phase 3+.** Sharded hot tier across hosts. Stratified sampling (per decision type, per archetype bucket). Cold tier becomes the dataset-of-record for offline analysis and adversarial scenario generation in Phase 5.

## Open Risks

- **Backpressure asymmetry** — workers do not slow down if Q3 is full; retention drops oldest. We may silently lose recent trajectories under load. Mitigation: alert on retention drops; size hot tier for peak ingest, not average.
- **Schema migrations are downtime.** Plan migration windows; never migrate during a phase-gate evaluation run.
- **Two-tier sampling** can starve the trainer if cold-tier reads dominate. Mitigation: minibatch assembler weights toward hot tier; cold tier is for stratified backfill, not steady state.
- **Off-policy correction** is the trainer's burden, not ours — but we must surface enough metadata for it to do its job. Provenance tagging is load-bearing.

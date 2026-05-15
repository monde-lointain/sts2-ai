# 01 — Q3 Architectural Decision Log

Q3-internal ADRs. Each entry: Title, Status, Context, Decision, Consequences
(negatives first).

Cross-quantum ADRs live at `docs/specs/01-decisions-log.md`; Q3 inherits
ADR-001, ADR-006, ADR-014..019 there. ADRs marked ✱ below are mirrored in
the cross-quantum log (Q3-ADR-004 → ADR-020, Q3-ADR-005 → ADR-021,
Q3-ADR-011 → ADR-019).

| # | Title | Status |
|---|---|---|
| Q3-ADR-001 | Modular Monolith over Mini-Fleet for Phase 1 | Accepted |
| Q3-ADR-002 | RocksDB over LMDB for Hot Tier | Accepted |
| Q3-ADR-003 | HTTP/protobuf Transport (extend stdlib) | Accepted |
| Q3-ADR-004 ✱ | Oracle-Agreement Sideband Routes through Q3 | Accepted |
| Q3-ADR-005 ✱ | Phase-1 `combat_outcome_samples[]` Degenerate-Single Convention | Accepted |
| Q3-ADR-006 | Schema-Migration UX via 503 + `retry_after_sec` | Accepted |
| Q3-ADR-007 | Hot-Tier Phase-1 Sizing: 50 GiB high-water, 100 GiB overflow | Accepted |
| Q3-ADR-008 | Sustained-Pressure: Dual Time-Windowed Condition | Accepted |
| Q3-ADR-009 | Cross-Tier Sampling Bias toward Hot | Accepted |
| Q3-ADR-010 | PriorityIndex Colocated in HotStore RocksDB CF | Accepted |
| Q3-ADR-011 ✱ | Trajectory Schema v1.0 → v1.1 Additive Bump (ADR-019) | Accepted |

---

## Q3-ADR-001 — Modular Monolith over Mini-Fleet for Phase 1

**Status:** Accepted.

**Context.** Q3's eight internal submodules (Ingest, SchemaRegistry, HotStore,
ColdStore, Lifecycle, Sampler, PriorityIndex, ControlPlane) could be deployed
as one process or as 3+ processes (e.g., ingest / sampler / lifecycle as
separate services on adjacent ports). Phase-1 throughput target is 10⁶–10⁷
trajectory-steps/day on a single host; the seven sibling pipeline services
(`pipeline/{rollout-workers,trainer,inference-server,model-registry,
observability,evaluation-harness}` plus `common`) all follow a one-process,
one-port pattern enforced by `pipeline/tests/smoke_services.py`.

**Decision.** One Python service on port 18103 hosting all 8 submodules.
Internal submodules are pure-Python packages with typed-dataclass request /
response boundaries — shaped as if RPC-able. Cross-submodule calls are
in-process today; Phase-3 hot-tier shard-out extracts the HotStore submodule
into its own process(es) by replacing the in-proc call with HTTP without
changing the call sites' types.

**Consequences.**

- *Negative:* in-proc coupling can leak across submodules if reviewers don't
  enforce dataclass-only boundaries. Mitigation: schema-owning rule prevents
  shared tables; code review checks for cross-submodule attribute access.
- *Negative:* a crash in any submodule kills the whole service. Mitigation:
  worker-side retry / supervisor restart per the pipeline's general
  "services are cattle" posture.
- *Negative:* memory budget is shared — a hot-tier RocksDB allocator spike
  competes with sampler buffers. Mitigation: explicit per-submodule budgets
  in `config/local.json` Phase 2+.
- *Positive:* Phase 1 throughput is not transport-bound; one process avoids
  three serialization hops per request.
- *Positive:* one writer = one append order = deterministic provenance log,
  no cross-process ordering ADR needed.
- *Positive:* schema drain-and-flip is a one-process operation, ≤5 min.

---

## Q3-ADR-002 — RocksDB over LMDB for Hot Tier

**Status:** Accepted.

**Context.** Hot tier is write-heavy (ingest at 10⁶–10⁷ steps/day) and
prefix-scan-heavy on the read side (sampler scans by ingest_ts_ns range;
PriorityIndex by score bucket). `docs/scaling-strategy.md` §5.3 lists both
RocksDB and LMDB as candidates. LMDB is a memory-mapped B-tree, great for
read-heavy workloads with small DBs; RocksDB is LSM-tree, optimized for
sustained writes and range scans.

**Decision.** RocksDB, with column families: `traj` (trajectory bytes by
`(ingest_ts_ns, trajectory_id)`), `by_id` (id → ts), `step_idx` (optional
Phase-3 stratified index), `priority` (PriorityIndex submodule data).
Column-family separation enforces the no-shared-table rule while keeping the
DB instance single — one process, one DB handle, one set of compaction
threads.

**Consequences.**

- *Negative:* RocksDB write amplification at small batch sizes; per-trajectory
  POSTs can fragment the WAL. Mitigation: mandate trajectory-grain batching
  from writers (Q3-ADR-003); enforce minimum batch size at IngestAPI Phase 2.
- *Negative:* compaction of `priority` CF (Phase 2+) competes for IO with
  `traj` CF compaction. Mitigation per Q3-ADR-010.
- *Negative:* RocksDB Python bindings (rocksdb-py or python-rocksdb) are
  C-extension dependencies — first non-stdlib pipeline dependency. Adds a
  build step.
- *Positive:* LSM matches the workload shape (write-heavy, range-scan reads).
- *Positive:* column families colocate the PriorityIndex without
  table-sharing, simplifying ops vs separate DB files.
- *Positive:* mature ecosystem; tunable per-CF (`write_buffer_size`,
  `compaction_pri`) for Phase-2+ specialization.

---

## Q3-ADR-003 — HTTP/protobuf Transport (extend stdlib)

**Status:** Accepted.

**Context.** Every existing pipeline service uses stdlib
`http.server.ThreadingHTTPServer` (`pipeline/common/service_host.py:52-56`).
gRPC and Kafka are mentioned in `docs/specs/modules/experience-store.md:24`
("via Kafka or an internal RPC") but neither exists anywhere in the pipeline
today. Introducing either is a project-wide dependency choice.

**Decision.** Phase 1 ingest and sample transports extend the existing
stdlib HTTP path. Write API: `POST /trajectories` with
`Content-Type: application/x-protobuf`, body is wire-format `Trajectory` bytes
per `contracts/schemas/trajectory/trajectory.proto`. Batch endpoint
`POST /trajectories:batch` accepts length-delimited frames. Read API:
`POST /sample` returns a length-delimited protobuf stream. Mandates writers
batch at trajectory granularity (not per-step) so per-second request rate
stays well below `ThreadingHTTPServer`'s thread-per-request cap.

Revisit trigger: profiling shows transport overhead >10% of write-path
budget. Phase-2 ADR fork to gRPC or Kafka at that point.

**Consequences.**

- *Negative:* `ThreadingHTTPServer` spawns one thread per request — a misbehaving
  writer that POSTs every step will overrun the server. Mitigation: writers
  must batch per trajectory; mandate documented at Q8/Q11 boundary.
- *Negative:* protobuf-over-HTTP requires Q3 to handle partial reads / EOF
  carefully on the read path; length-delimited framing adds complexity.
- *Negative:* deferring gRPC means a future transport swap touches Q8, Q10,
  Q11, Q12 simultaneously.
- *Positive:* no new dependency for Phase 1; matches existing seven-sibling
  pattern; smoke tests work unchanged.
- *Positive:* HTTP overhead (tens of µs) is a rounding error against RocksDB
  write cost at Phase-1 scale.
- *Positive:* trivially debuggable with curl + protobuf decoder.

---

## Q3-ADR-004 ✱ — Oracle-Agreement Sideband Routes through Q3

**Status:** Accepted. Mirrored at `docs/specs/01-decisions-log.md` ADR-020.

**Context.** Per ADR-017 carve-out, oracle-agreement (Q2-vs-network labeled
comparison) remains a training-eligible signal feeding Q10 prioritized
sampling. Routing options: (a) Q2 → Q3 SidebandRouter → PriorityIndex →
served via Sampler prioritized mode; (b) Q2 → direct table consumed by Q10
out-of-band; (c) Q2 → Kafka-like stream, no Q3 storage.

**Decision.** Route through Q3. Q2 emits to `POST /sideband/oracle-agreement`;
Q3 SidebandRouter writes to `sideband/oracle.ndjson` and (Phase 2+) updates
PriorityIndex. Trainer reads via Sampler `mode=prioritized` — one front
door, one durability surface.

**Consequences.**

- *Negative:* Q3 becomes a hard dependency for oracle-agreement durability —
  Q3 outage queues at Q2 with a bounded buffer; long Q3 outage drops
  oracle-agreement signals. Mitigation: alerting on Q2-side queue depth, not
  architecture.
- *Negative:* SidebandRouter is a Q3 submodule that needs to ship at Q3 boot
  even though Q2's emit path may lag — Phase-1 ships SidebandRouter as a
  no-op write-and-store stub until Q2 wires it.
- *Negative:* couples oracle-agreement schema evolution to Q3 schema lifecycle.
- *Positive:* trainer has one read path for everything it samples
  (trajectories + sideband); no out-of-band table to track.
- *Positive:* priority scores live next to the data they prioritize (same
  RocksDB DB file under different CF per Q3-ADR-010).
- *Positive:* migration path simple — Phase 2 just wires SidebandRouter into
  PriorityIndex; no consumer-side change.

---

## Q3-ADR-005 ✱ — Phase-1 `combat_outcome_samples[]` Degenerate-Single Convention

**Status:** Accepted. Mirrored at `docs/specs/01-decisions-log.md` ADR-021.

**Context.** Per ADR-014 (cross-quantum) and `contracts/schemas/trajectory/
trajectory.proto:35-37`, Phase-1 trajectories populate
`combat_outcome_summary.expected_hp_delta` from the scalar HP-fraction
prediction. The exact `combat_outcome_samples[]` populating convention is
deferred to Q3 boot's decision. Two viable options: (i) empty array, (ii)
degenerate single sample with `probability_weight=1.0` and `hp_delta`
mirroring the summary.

**Decision.** Degenerate single sample. Phase-1 combat steps populate
`combat_outcome_samples = [Sample(survived=summary-derived, hp_delta=
summary.expected_hp_delta, potion_delta=[], card_instance_deltas=b"",
relic_counter_deltas=b"", rng_public_belief_delta=b"", turns_taken=0,
timeout=false, probability_weight=1.0)]`. Phase-2+ swaps to real K-sample
distribution without schema change — populate-only difference.

**Consequences.**

- *Negative:* downstream distributional analyses must filter degenerate rows
  (`len(samples)==1 AND probability_weight==1.0 AND sample.hp_delta ==
  summary.expected_hp_delta`) to avoid biasing Phase-2+ variance estimates.
  Mitigation: documented filter recipe in `modules/sampler.md` testing
  section.
- *Negative:* trainers that expect "non-empty samples = real distribution"
  must update their detection logic. Mitigation: Q3-ADR-005 mirrored in the
  cross-quantum log so Q10's reader code accounts for it.
- *Negative:* slightly larger Phase-1 rows than empty-array option (one
  zero-padded `CombatOutcomeSample` per combat step).
- *Positive:* one code path for samples-iteration on consumer side (no
  `if samples_empty` branches).
- *Positive:* Phase-2 transition is a population change, not a schema bump —
  no migration event during the cascade.
- *Positive:* preserves the invariant that combat steps always carry at
  least one sample (good for sample-quality dashboards per
  `docs/specs/modules/observability.md:40`).

---

## Q3-ADR-006 — Schema-Migration UX via 503 + `retry_after_sec`

**Status:** Accepted.

**Context.** `docs/specs/01-decisions-log.md` ADR-006 marks schema migrations
as first-class operational events ("old-schema writes drained, store paused
on schema boundary, new schema enabled"). During the drain-and-flip window,
ingest and sample APIs are partially or fully unavailable. UX choices: (i)
silently buffer at IngestAPI; (ii) reject with structured retry hint; (iii)
block until flip completes.

**Decision.** During drain, IngestAPI returns HTTP 423 Locked for stale-version
writes (target-version writes still succeed) and Sampler returns HTTP 503
with body `{"reason":"schema_drain","retry_after_sec":N}`. Trainer is expected
to honor `retry_after_sec` and back off. Q3 ships a documented retry helper
snippet at `pipeline/experience-store/docs/specs/modules/sampler.md`.

**Consequences.**

- *Negative:* trainer must implement retry logic — one more failure mode in
  the training loop. Mitigation: helper snippet ships with the Sampler spec.
- *Negative:* during a planned 5-min flip, the training loop reports samples
  paused for ~5 min; observability dashboards show a flat spot. Mitigation:
  document expected behavior in operator runbook (Phase-2 deliverable).
- *Negative:* 423 vs 503 distinction adds nuance writers must handle.
- *Positive:* explicit structured error beats silent buffering — operators
  know the system is in migration, not stuck.
- *Positive:* no in-flight loss: writers that respect 423 simply queue at
  the writer side and retry, preserving order per writer.
- *Positive:* matches the broader pipeline's "structured failures over
  silent ones" posture.

---

## Q3-ADR-007 — Hot-Tier Phase-1 Sizing: 50 GiB high-water, 100 GiB overflow

**Status:** Accepted.

**Context.** Phase 1 runs single-host on local NVMe. No measured load
evidence yet. Per-step trajectory size estimate (rich_state + masks +
policy + degenerate sample + macro_context + provenance) is order-of-
magnitude ~5–10 KiB, putting 10⁶ steps/day at ~10 GiB/day.

**Decision.** Default Phase-1 hot-tier thresholds:
- `high_water_bytes = 50 GiB` — Lifecycle begins age-drop above this.
- `overflow_bytes = 100 GiB` — Lifecycle aggressively drops oldest above this;
  retention-drop metric incremented.

Operator override via `pipeline/experience-store/config/local.json` keys
`hot_high_water_bytes`, `hot_overflow_bytes`.

Revisit trigger: first sustained-load run during Phase-1 mid-milestone.

**Consequences.**

- *Negative:* defaults are educated guesses; real per-step size depends on
  rich_state encoding choices not finalized at boot. Mitigation: revisit
  trigger explicit.
- *Negative:* Phase-2 real K-sample combat output multiplies per-step size
  by K — same buckets may hold <1 day at K=10. Q3-ADR-005 revisit will fold
  in resizing.
- *Negative:* under-sized NVMe (<200 GiB free) breaks the default; operator
  must size config.
- *Positive:* concrete starting numbers unblock Phase-1 ship.
- *Positive:* per-environment override keeps the spec stable while letting
  ops tune.
- *Positive:* high_water/overflow split avoids cliff-edge drop behavior —
  Lifecycle has graceful warning before catastrophic eviction.

---

## Q3-ADR-008 — Sustained-Pressure: Dual Time-Windowed Condition

**Status:** Accepted.

**Context.** `docs/specs/modules/experience-store.md:24` specifies "writers
block briefly if hot tier is full; retention drops oldest if pressure
persists." "Persists" is undefined; needs a concrete operational definition.

**Decision.** Sustained pressure fires when EITHER:
- `hot_bytes > hot_high_water_bytes` for ≥ 60 seconds, OR
- `ingest_queue_depth > 0.8 × queue_capacity` for ≥ 30 seconds.

When fired, Lifecycle escalates to age-only drop (oldest ingest_ts_ns
first). No priority-based drop in Phase 1; PriorityIndex is Phase-2+ and
drop decisions stay simple.

When neither condition holds, normal hot-only operation; no drops.

**Consequences.**

- *Negative:* a flash spike retreating within the time window goes undetected
  — recent-trajectory-loss alert is the residual catch. Mitigation: alert
  rule in Q7 observability dashboard.
- *Negative:* age-only drop loses recent oracle-agreement-prioritized rows
  if those happened to land oldest. Mitigation: Phase-2 PriorityIndex
  participation in drop policy revisits this.
- *Negative:* 60s/30s windows are educated; first load run is the ratify
  point.
- *Positive:* simple, debuggable rule — operators can verify it from
  `/metrics`.
- *Positive:* age-only drop preserves "single writer, single order"
  invariant from Q3-ADR-001.
- *Positive:* age-monotonic eviction makes provenance reconstruction
  predictable (oldest provenance entries match oldest-dropped trajectories).

---

## Q3-ADR-009 — Cross-Tier Sampling Bias toward Hot

**Status:** Accepted. (Phase-2+; Phase-1 not active.)

**Context.** Phase-2 introduces the cold tier; uniform sampling could in
principle draw rows proportionally from hot vs cold by row count. But cold-
tier reads are slower (S3-equivalent latency); allowing them to dominate
starves trainer (`docs/specs/modules/experience-store.md:46`).

**Decision.** Uniform mode draws from hot with probability
`p_hot = min(1.0, hot_size_rows / (hot_size_rows + cold_size_rows) × α)`
where α=2.0 (configurable). At equal sizes, 67% of samples come from hot.
Stratified backfill jobs that explicitly want cold-tier-only rows must
pass `cold_only=true` filter.

**Consequences.**

- *Negative:* the α factor biases the dataset distribution toward recent
  data; off-policy correction depends on the trainer accounting for this.
  Mitigation: trainer reads `sampling_mode` provenance per row and applies
  importance correction.
- *Negative:* stratified backfill jobs need explicit `cold_only=true` flag —
  one more knob in the sampler API.
- *Negative:* α=2.0 is heuristic; calibration evidence comes during Phase 2.
- *Positive:* trainer never starves on cold-tier latency under default
  uniform sampling.
- *Positive:* cold tier remains useful for stratified analyses without
  blocking the steady-state read path.
- *Positive:* one tunable captures the behavior — operators don't need to
  reason about row-count math.

---

## Q3-ADR-010 — PriorityIndex Colocated in HotStore RocksDB CF

**Status:** Accepted. (Phase-2+; Phase-1 stub only.)

**Context.** PriorityIndex stores per-trajectory or per-step priority scores
for prioritized replay. Storage options: (a) separate RocksDB column family
in the HotStore DB file; (b) a second RocksDB DB file under PriorityIndex
sole ownership; (c) a separate process (mini-fleet, rejected by Q3-ADR-001).

**Decision.** Same RocksDB DB file as HotStore, separate column family
named `priority`. The CF keyspace is owned exclusively by PriorityIndex —
HotStore submodule code never reads or writes `priority` CF (the no-shared-
tables rule applies to ownership of the keyspace, not the file). Single DB
handle = one set of compaction threads; per-CF tuning available.

**Consequences.**

- *Negative:* compaction of `priority` CF competes for IO with `traj` CF.
  Workload shapes differ (priority writes are point updates; traj writes
  are bulk appends). Mitigation: per-CF tuning (write_buffer_size,
  compaction_pri); split to separate DB file Phase-3 if profiling shows
  contention.
- *Negative:* a corruption that takes down the DB file kills both submodules.
  Mitigation: WAL + periodic snapshots; RocksDB checksums.
- *Negative:* enforcement of the keyspace-ownership rule is by code review,
  not by process boundary. Risk of cross-submodule sneak-access.
- *Positive:* one DB instance, one set of operational concerns, one mount
  point. Simpler ops.
- *Positive:* atomic batch writes across CFs (e.g., trajectory append +
  initial priority entry) possible if Phase-2+ wants them.
- *Positive:* Phase-3 split path well-defined: move `priority` CF to its own
  DB file, then to its own process.

---

## Q3-ADR-011 ✱ — Trajectory Schema v1.0 → v1.1 Additive Bump (ADR-019)

**Status:** Accepted (2026-05-15). Cross-quantum mirror of ADR-019
(`docs/specs/01-decisions-log.md:378-424`); ADR-019 is the load-bearing
ratification authority.

**Context.** ADR-019 ratified macro_context derivation policy on
2026-05-15. Decision 4 commits to appending `gold_shadow_price (tag 10)`
and `max_hp_shadow_price (tag 11)` to `MacroContext` in
`contracts/schemas/trajectory/trajectory.proto`. The bump is additive
(forward-compatible). Q3 SchemaRegistry owns the accept-list and
current-write-target; the bump must land here for Q10 / future consumers
to write the new fields.

**Decision.** Bump trajectory.proto minor version v1.0 → v1.1. Q3
SchemaRegistry:

- `_accepted = [PHASE1, PHASE1_1]` — v1.0 readers and writers remain
  supported during transition; no drain/flip required for additive bumps.
- `_current_write_target = PHASE1_1` — fresh writers (Q1 / Q10) stamp
  v1.1.
- `derivation_method` allowed values tighten to the closed enum from
  ADR-019 Decision 4: `"warmup_heuristic_curve" | "learned_autodiff" |
  "learned_finitediff" | "joint_proximal" | "fallback_lagged"`.

**Consequences.**

- *Negative:* Q3 carries two accepted versions through Phase-2 boot;
  sentinel files multiply (`1.0.active` + `1.1.active`); operator
  cleanup deferred to drain/flip FSM implementation (Phase-1 close).
- *Negative:* Q10 reader treats missing v1.0 fields as proto3 default
  (0.0); ADR-019 §Decision-4 originally specified NaN-sentinel — defer
  reconciliation to Phase-2 sp head boot.
- *Positive:* v1.0 consumers still write/read without code change;
  additive bumps are free under the proto3 minor-bump invariant
  (`contracts/schemas/trajectory/trajectory.proto:21-28`).
- *Positive:* schema is now in sync with ADR-019 ratified content; no
  stale "Deferred" anchor.

**Origin.** Cross-quantum ADR-019 ratification 2026-05-15. Q3-ADR-011
records the Q3-side schema-registry policy bump.

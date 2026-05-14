# 01 — Q10 Architectural Decision Log

Q10-internal ADRs. Each entry: Title, Status, Context, Decision, Consequences
(negatives first per project convention).

Cross-quantum ADRs live at `docs/specs/01-decisions-log.md`; Q10 inherits
ADR-001, ADR-006, ADR-007, ADR-009, ADR-010, ADR-014..018, ADR-020, ADR-021
there. ADRs marked ✱ below require cross-quantum mirrors when Q3 boot
directive next cycles.

| # | Title | Status |
|---|---|---|
| Q10-ADR-001 | Modular Monolith + Producer-Consumer Hot Path | Accepted |
| Q10-ADR-002 | Serial Single-RPC Prefetcher | Accepted |
| Q10-ADR-003 | Phase-1 `dataset_sha` = Hash of Trajectory-ID List | Accepted |
| Q10-ADR-004 | Phase-1 Fail-Fast on Q3 Outage | Accepted |
| Q10-ADR-005 ✱ | Trajectory Protobuf Binding Lifted to `pipeline/common/` | Accepted |
| Q10-ADR-006 | ONNX Export at Every Checkpoint | Accepted |
| Q10-ADR-007 | W&B Sidecar Inline Thread with Internal Queue | Accepted |
| Q10-ADR-008 | Q4 Content Registry Frozen at Bootstrap | Accepted |

---

## Q10-ADR-001 — Modular Monolith + Producer-Consumer Hot Path

**Status:** Accepted.

**Context.** Q10's eight internal submodules could be (a) one process,
synchronous top-down call (pure layered); (b) one process with producer-
consumer concurrency on the data path; (c) a mini-fleet of services (data
loader, trainer, publisher). The cross-quantum precedent is one process per
quantum (Q3-ADR-001, enforced by `pipeline/tests/smoke_services.py`). The
performance pressure is GPU stall: Q3 RPC tail (~50 ms) and Q5 publish
wall-time must not block the GPU step (~150 ms target).

**Decision.** One Python service on port 18110 hosting 8 submodules with
functional cohesion. The hot data path runs producer-consumer across two
threads (`data_ingest` prefetcher feeds a bounded `queue.Queue`; `train_driver`
main loop consumes). A third daemon thread drives `artifact_publisher` on
cadence. All threads observe a shared `threading.Event` stop signal.

**Consequences.**

- *Negative:* threading complexity exceeds Q3's 2-thread shape (prefetcher +
  publisher add over the HTTP server thread). Mitigation: bounded queues
  with timeouts (never `block=True` indefinitely); stop-state diagnostics
  exposed at `/metrics`.
- *Negative:* functional cohesion has a learning cost — KL coefficient lives
  in `loss_engine` but weight decay lives in `optim`. Mitigation: per-package
  `__init__.py` docstrings (Q3 pattern); integration tests verify cross-module
  contracts.
- *Negative:* a crash in any submodule kills the whole training run.
  Mitigation: external orchestration restarts the run from the last
  artifact (fail-fast posture, Q10-ADR-004).
- *Negative:* Phase-4 multi-GPU DDP forks within-process; not a multi-process
  service. If single-node 8-GPU saturates by Phase-5, Q10 must add
  `torchrun`-style launcher logic.
- *Positive:* Phase-1 GPU utilization realistic at ~85% (vs. ~55% for the
  pure-layered alternative that would stall GPU on every Q3 RPC).
- *Positive:* head-registration patterns confine Phase-2 head additions to
  one module each (`model`, `loss_engine`) — no service rewrite.
- *Positive:* matches Q3's `ThreadingHTTPServer` + daemon-thread idiom and
  passes `pipeline/tests/smoke_services.py` unchanged.

---

## Q10-ADR-002 — Serial Single-RPC Prefetcher

**Status:** Accepted.

**Context.** `data_ingest` could (a) issue one `POST /sample` at a time and
overlap RPC tail with GPU step via a bounded queue, or (b) maintain a thread
pool of N concurrent RPCs to drive higher Q3 throughput. Phase-1 GPU step
~150 ms vs. Q3 RPC tail ~50 ms means even a serial prefetcher with queue
depth 4 covers a 600 ms Q3 stall before GPU starves.

**Decision.** One outstanding `/sample` request at a time. Prefetch queue
capacity 4 (Phase-1 default; configurable). The prefetcher thread blocks on
`queue.put` when full, blocks on `client.post` when empty.

**Consequences.**

- *Negative:* if Q3 sample latency grows past 600 ms tail, GPU starves.
  Mitigation: monitor `sts2_q10_prefetch_queue_depth` gauge; alert on
  sustained zero-depth; escalate to a 2-worker pool only when Phase-2
  evidence demands.
- *Negative:* loses Q3-side throughput headroom (Q3 could serve more
  concurrent requests). Acceptable: Q10 is the bottleneck candidate, not
  Q3, in Phase 1.
- *Positive:* cursor-position ordering is preserved; sampling is replayable
  given `(mode, seed, cursor)` per Q3-ADR-006 + Q3 sampler.md.
- *Positive:* no per-thread cursor or merge logic in `data_ingest`. Simpler
  to test, audit, and reason about.

---

## Q10-ADR-003 — Phase-1 `dataset_sha` = Hash of Trajectory-ID List

**Status:** Accepted. Phase-2 upgrade target: Q3-side content-addressed hash.

**Context.** Every Q5 artifact must stamp `dataset_sha` for reproducibility
per cross-quantum ADR §5.5 (`scaling-strategy.md`). Options: (a) hash the
cursor tokens consumed during the run (cheapest but tightly couples to Q3
cursor implementation); (b) hash the trajectory-id list consumed (Q10-local,
cheap, replayable if Q3 hasn't compacted); (c) Q3-side content-addressed
hash (cleanest but requires a new Q3 RPC that Q3 has not yet shipped).

**Decision.** Phase 1: `dataset_sha = sha256(sorted(trajectory_id for batch
in batches for step in batch))`. Computed incrementally by `data_ingest`,
captured in `RunProvenance` at publish time. Phase-2 swap target: Q3
exposes `GET /dataset/content-hash?after_ts_ns=...&before_ts_ns=...` and
Q10 reads that instead.

**Consequences.**

- *Negative:* replayability assumes Q3 retains the same trajectory IDs.
  If Q3 retention drops the consumed IDs, the dataset cannot be exactly
  reconstructed — only the SHA is auditable, not the data itself.
  Mitigation: this is acceptable for Phase-1 internal experiments;
  Phase-2 content-hash strengthens the audit.
- *Negative:* growing list of IDs across a long run consumes proportional
  memory. At 10⁶ IDs × 16 bytes = 16 MB — bounded for Phase 1; revisit
  if Phase-3 runs exceed 10⁸ IDs.
- *Negative:* incremental SHA must be deterministic given the same
  trajectory-id arrival order. The serial prefetcher (Q10-ADR-002) makes
  this trivially true.
- *Positive:* zero new Q3 work; Phase-1 Q10 closes independently.
- *Positive:* swap to Q3-side hash in Phase 2 is a one-method substitution
  in `artifact_publisher.manifest`; no schema bump.

---

## Q10-ADR-004 — Phase-1 Fail-Fast on Q3 Outage

**Status:** Accepted. Phase-2 revisit target: retry-with-backoff inside
`data_ingest` when multi-week runs become routine.

**Context.** A Q3 outage during a training run could be handled by (a)
fail-fast — propagate the exception, exit non-zero, let orchestration restart
the run; (b) retry-with-backoff inside `data_ingest` — hide the outage from
`train_driver`, resume seamlessly when Q3 returns. Phase-1 runs are
single-GPU experimental and short (hours, not weeks). Loud failures are
debuggable; silent stalls are not.

**Decision.** Phase 1: Q3 RPC errors propagate as `RuntimeError` from
`data_ingest.get_batch()`. `train_driver` catches, emits a final metrics
flush, signals the stop event, and exits non-zero. Orchestration restarts
the run from the last published artifact. Phase 2: `data_ingest` gains
configurable retry-with-backoff with a circuit-breaker pattern (deferred).

**Consequences.**

- *Negative:* a transient Q3 hiccup (e.g., schema drain per Q3-ADR-006)
  aborts the run. The trainer-side retry recipe documented at
  `pipeline/experience-store/docs/specs/modules/sampler.md` handles 503
  schema-drain at the HTTP layer — Q10 honors that recipe in `data_ingest`
  (treat 503 as transient with the advertised `retry_after_sec`). Only
  hard errors (connection refused, 5xx without retry advice) trigger
  fail-fast.
- *Negative:* lost work between the last checkpoint and the failure. Bounded
  by checkpoint cadence (every N steps + M minutes — typically ≤10 min of
  work).
- *Positive:* simple control flow in `train_driver`. No retry-loop state.
- *Positive:* observable failure surface — operators see exit code + final
  metrics, not a stalled GPU.

---

## Q10-ADR-005 ✱ — Trajectory Protobuf Binding Lifted to `pipeline/common/`

**Status:** Accepted. Cross-quantum mirror required: a corresponding ADR
must land at `docs/specs/01-decisions-log.md` referencing this entry,
and Q3 must update its `experience-store/proto/__init__.py` to re-export
from the shared location.

**Context.** The generated `trajectory_pb2.py` currently lives at
`pipeline/experience-store/proto/`. Q10 needs to deserialize the same
protobuf wire format. Options: (a) vendor `trajectory_pb2.py` into
`pipeline/trainer/proto/` (duplication burden every `.proto` change); (b)
lift to `pipeline/common/trajectory_proto.py` and have both Q3 and Q10
import from there; (c) move to top-level `contracts/generated/python/`
per Q2-ADR-001 §4 (the spec-correct long-term home, but it requires
standing up a codegen pipeline Q3 explicitly deferred at boot).

**Decision.** Option (b): generate `trajectory_pb2.py` at
`pipeline/common/trajectory_proto.py`. Q3's `experience-store/proto/__init__.py`
becomes a thin re-export. Q10 imports from `pipeline.common.trajectory_proto`.
The cross-quantum mirror ADR documents the contract: any change to
`contracts/schemas/trajectory/trajectory.proto` regenerates the file at one
location, and both Q3 and Q10 consume it unchanged.

**Consequences.**

- *Negative:* Q3 carries a no-op re-export shim for the Phase-1 lifetime —
  small cost, but it is API surface area to maintain.
- *Negative:* the `pipeline/common/` package gains a binding that is
  semantically owned by Q3's wire format. If `pipeline/common/` later
  develops its own ownership rules, this entry must be re-homed.
- *Negative:* eventual move to `contracts/generated/python/` is a future
  refactor (option c). Postponing it is a known tech-debt item.
- *Positive:* zero duplication between Q3 and Q10; single source of truth
  for the wire format.
- *Positive:* Q3 boot-time invariants (schema version checks in
  `schema_registry`) operate on the same generated module Q10 imports —
  no skew possible.

---

## Q10-ADR-006 — ONNX Export at Every Checkpoint

**Status:** Accepted.

**Context.** Q10 publishes checkpoints every N steps + every M minutes
(whichever fires first). Each artifact must contain an ONNX export for Q9
inference-server consumption per cross-quantum ADR-007 and Q9 spec.
Options: (a) ONNX-export at every checkpoint (~2 s overhead on a 10 M-param
network); (b) state-dict-only at most checkpoints, ONNX only at
promotion-candidate checkpoints. Q10 cannot know which checkpoints are
promotion candidates — that decision lives in the external promotion
workflow (ADR-007: Q12 + reviewer sign-off).

**Decision.** Every published artifact contains an ONNX export. Export runs
on the publisher thread, off the main GPU loop. Single-threaded
`torch.onnx.export` validated against `onnx.checker.check_model` before
upload.

**Consequences.**

- *Negative:* ~2 s of CPU per checkpoint published. At Phase-1 cadence
  (e.g., every 5 min) this is ~0.7% overhead on a 24/7 run. Negligible.
- *Negative:* artifact size grows by the ONNX bytes (~roughly equal to the
  PyTorch state-dict, depending on quantization). Phase-1 ~40 MB per
  artifact; Phase-5 estimates not yet load-bearing.
- *Negative:* ONNX op-set version is a coupling point with Q9. The
  provenance manifest must stamp the `opset_version`.
- *Positive:* any checkpoint is a promotion candidate; the workflow does
  not have to coordinate with the trainer to request an export.
- *Positive:* CI can validate the ONNX export pipeline on every run,
  catching regressions immediately rather than at promotion time.

---

## Q10-ADR-007 — W&B Sidecar Inline Thread with Internal Queue

**Status:** Accepted.

**Context.** `scaling-strategy.md` §7 commits W&B as the default
experiment-tracking sidecar. Options: (a) inline `wandb.log()` calls from the
training loop (network blip can backpressure GPU step); (b) inline thread
with internal queue (decouples the main path); (c) separate process
(over-engineered for the project's "no async, threading only" convention).

**Decision.** `metrics_emitter` runs a single daemon thread that drains an
internal `queue.Queue` of pending W&B log payloads. The training loop calls
`metrics_emitter.record_step(...)` which enqueues; the daemon thread
calls `wandb.log()` in batches. On stop, the queue is drained with a
bounded timeout (default 10 s) before exit. W&B can be disabled via
`config/local.json` key `wandb_enabled: bool` for offline / smoke runs.

**Consequences.**

- *Negative:* a W&B outage longer than the queue capacity drops log entries.
  Mitigation: queue capacity 1024 entries (covers ~hour at 1Hz logging);
  drop policy is "drop oldest" with a counter
  `sts2_q10_wandb_dropped_total` for observability.
- *Negative:* shutdown adds up to 10 s of wait for queue drain. Bounded.
- *Negative:* one more daemon thread to coordinate at SIGTERM (the third
  after prefetcher and publisher).
- *Positive:* main training path never blocks on network egress.
- *Positive:* W&B disabled mode (Phase-1 smoke tests, CI runs without
  network) works by not constructing the daemon thread.
- *Positive:* mirrors the publisher-thread pattern; consistent shape.

---

## Q10-ADR-008 — Q4 Content Registry Frozen at Bootstrap

**Status:** Accepted.

**Context.** Cross-quantum ADR-010 establishes that Q4 (Content Registry)
is bundled inside every Q5 artifact. Q10 loads the parent artifact at
bootstrap (`artifact_publisher.load_parent()`) and reads the bundled Q4.
Options: (a) freeze the registry at bootstrap — all encoding for the
run uses the same token table; (b) hot-reload when a new registry version
appears in the parent-of-parent artifact lineage.

**Decision.** Freeze at bootstrap. The `ContentRegistry` instance handed to
`tensor_encoder` is constructed once and never mutated. The new artifact
published by this run bundles the same Q4 bytes loaded from the parent.

**Consequences.**

- *Negative:* if a Q4 update lands during a multi-day Phase-3+ run, Q10
  does not pick it up until the run is restarted from the new parent.
  Acceptable: Q4 updates are versioned and tracked separately by curriculum
  / content workflows.
- *Negative:* hot-fixing tokens for a deployed model is not possible
  (this is the cross-quantum trade-off of ADR-010, inherited).
- *Positive:* tokenization is deterministic across the run. Provenance
  stamping of `content_registry_sha` is trivially correct.
- *Positive:* no race between encoding and registry mutation. No locks
  inside `tensor_encoder`.
- *Positive:* the encoded-tensor cache (if Phase-2+ adds one) does not
  need to invalidate on registry changes within a run.

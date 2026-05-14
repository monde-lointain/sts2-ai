# Submodule: metrics-emitter

> Cross-cutting facility. Prometheus text v0.0.4 emitter + W&B sidecar
> inline thread per Q10-ADR-007. Mirrors Q3's
> `control_plane/observability.py:MetricsEmitter` shape with Q10-specific
> counter/gauge names.

## Responsibilities

- Maintain thread-safe counter and gauge maps. Counter and gauge name
  sets are fixed at construction (KeyError on unknown name — Q3 pattern).
- Render Prometheus text v0.0.4 lines matching the format expected by
  `pipeline/tests/smoke_services.py`:
  ```
  sts2_service_up{service="trainer"} 1
  sts2_service_uptime_seconds{service="trainer"} <float>
  <name>{service="trainer", ...labels} <value>
  ```
- Expose `inc(name, n=1)` / `set(name, val)` / `record_step(step,
  loss_components, step_stats)`. The last is the convenience entry
  point `train_driver` calls per step.
- Spawn one daemon thread for W&B uploads (per Q10-ADR-007). The
  thread drains an internal `queue.Queue(maxsize=1024)` of pending log
  payloads, calls `wandb.log(...)` in batches. Drop policy when queue
  full: drop-oldest with a counter `sts2_q10_wandb_dropped_total`.
- Support `wandb_enabled: bool` config: when False, do not construct
  the daemon thread; `record_step` no-ops on the W&B path. Lets
  Phase-1 smoke tests + air-gapped CI runs work.
- Honor `shutdown(timeout=10)`: drain the W&B queue with bounded wait,
  call `wandb.finish()`, exit the daemon thread.

Out of scope: alerting (Q7 + Grafana); dashboard config; counter-name
governance across services (each service owns its own namespace —
Q3 emits `sts2_q3_*`, Q10 emits `sts2_q10_*`).

## Data Ownership

In-process, mutable:

- `_counters: dict[str, int]` — fixed key set; thread-safe via a single
  `threading.Lock`.
- `_gauges: dict[str, float]` — same shape.
- `_started_at: float` — set once at construct from `time.monotonic()`;
  used for `sts2_service_uptime_seconds`.
- W&B queue + daemon-thread handle — present when `wandb_enabled=True`.

Schema-owning? **No.** The Prometheus text format is a stable contract
maintained at `pipeline/tests/smoke_services.py:23-26`; this submodule
adheres to it.

## Communication

**External:** Q7 scrapes `/metrics`. W&B SaaS receives uploads via the
daemon thread.

**Internal (in-process function calls, sync):**

- `MetricsEmitter(service_name: str, started_at: float, config:
  WandbConfig) -> MetricsEmitter` — constructed once in `service.py`.
- `inc(name: str, n: int = 1) -> None` — thread-safe; KeyError on
  unknown.
- `set(name: str, val: int | float) -> None` — same.
- `record_step(step: int, loss_components: dict[str, float], step_stats:
  StepStats) -> None` — called by `train_driver` per step; updates the
  relevant gauges and enqueues a W&B payload.
- `format_metrics() -> bytes` — called by the HTTP `/metrics` handler.
  Returns the full Prometheus text body.
- `shutdown(timeout: float) -> None` — called by `service.py.shutdown_background`.

## Fixed counter / gauge name set (Phase 1)

Counters (always emitted, start at 0):
- `sts2_q10_steps_total`
- `sts2_q10_sample_request_total` (with `{result}` label)
- `sts2_q10_sample_rows_total`
- `sts2_q10_publish_total` (with `{result}` label)
- `sts2_q10_nan_loss_total`
- `sts2_q10_source_perfect_leak_total`
- `sts2_q10_grad_clip_fired_total`
- `sts2_q10_wandb_dropped_total`
- `sts2_q10_consumed_traj_ids_total`
- `sts2_q10_encode_steps_total`

Gauges (always emitted, default 0 / 0.0):
- `sts2_q10_run_dirty`
- `sts2_q10_loss_total`
- `sts2_q10_loss_component` (labelled by `{head}`)
- `sts2_q10_kl_to_prior`
- `sts2_q10_lr`
- `sts2_q10_grad_norm`
- `sts2_q10_weight_norm`
- `sts2_q10_prefetch_queue_depth`
- `sts2_q10_last_published_step`
- `sts2_q10_artifact_size_bytes`
- `sts2_q10_onnx_export_seconds`
- `sts2_q10_publish_latency_seconds` (Phase 1 gauge; histogram Phase 2+)
- `sts2_q10_step_seconds`
- `sts2_q10_encode_seconds_p99`
- `sts2_q10_model_param_count`
- `sts2_q10_model_prior_age_steps`
- `sts2_q10_device`

Future bumps via a new ADR + this module.

## Coupling

- **Afferent (in):** every other Q10 submodule (write-only); `service.py`
  (`/metrics` handler reads).
- **Efferent (out):** W&B SaaS via the daemon thread (when enabled);
  Q7 via passive scrape.
- **Indirect:** none.

## Testing Strategy

### Unit

1. **Unknown counter name raises.** `inc("not_registered")` → `KeyError`.
   Verifies the Q3 strict-name pattern; absent test: typos silently
   drop metrics.
2. **Thread-safe counter increment.** 100 threads × 1000 increments →
   final value is exactly 100_000. Absent test: race conditions
   under load.
3. **Prometheus line shape matches smoke contract.** `format_metrics()`
   output contains `sts2_service_up{service="trainer"} 1` and
   `sts2_service_uptime_seconds{service="trainer"} <float>`. Verifies
   `pipeline/tests/smoke_services.py` compatibility.
4. **W&B disabled means no daemon thread.** Construct with
   `wandb_enabled=False` → `threading.active_count()` after construct
   matches the count before. Absent test: leaking threads in tests.
5. **W&B drop-on-full.** Fill queue to capacity 1024 + 1 more put →
   counter `wandb_dropped_total == 1`; queue size still 1024.
   Verifies the back-pressure policy.
6. **`shutdown(timeout)` drains within bound.** With queue depth 100,
   `shutdown(timeout=10)` returns within 10 s; `wandb.finish()` was
   called once. Absent test: SIGTERM hangs on metric flush.

### Integration

1. **End-to-end scrape.** Spin up the trainer service in `wandb_enabled=False`
   mode; HTTP-GET `/metrics` → response is valid Prometheus text v0.0.4
   parseable by `prometheus_client.text_string_to_metric_families`.
   Verifies the wire format.
2. **`record_step` populates loss components.** Call with `loss_components
   = {"policy": 1.0, "combat_sample": 2.0}` → next `/metrics` includes
   `sts2_q10_loss_component{service="trainer",head="policy"} 1.0`.
   Verifies the labelled gauge path.

### Smoke (mandatory)

- `pipeline/tests/smoke_services.py` GET `/metrics` MUST return a body
  containing `sts2_service_up{service="trainer"} 1`. This is the
  hard CI gate.

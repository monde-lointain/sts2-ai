# Submodule: train-driver

> The training loop. Pulls batches from `data_ingest`, encodes via
> `tensor_encoder`, forwards through `model`, compute loss via `loss_engine`,
> steps `optim`, fires checkpoint requests on cadence, emits metrics.
> The only submodule that orchestrates the others; everything else is a
> capability called from here.

## Responsibilities

- Run the training loop on a daemon thread spawned at
  `start(stop_event)`. Loop body:
  1. `batch = data_ingest.get_batch(timeout=30)` — block briefly; if
     `None` (stop event set), exit cleanly.
  2. `encoded = tensor_encoder.encode_batch(batch.steps)` — pure
     function, CPU tensors.
  3. `encoded = encoded.to(device)` — Phase-1 single GPU.
  4. `model_output = model.forward(encoded)`.
  5. `loss_result = loss_engine.compute(model_output, encoded)`.
  6. `step_stats = optim.step(loss_result.total)`.
  7. Update step counter; check checkpoint cadence; if due, snapshot
     `model.state_dict()` + `optim.state_dict()` and post a
     `PublishRequest` to `artifact_publisher`.
  8. Emit per-step metrics via `metrics_emitter.record_step(step,
     loss_result.components, step_stats)`.
  9. Periodic prior snapshot for KL: every `kl_prior_refresh_steps`
     steps, call `model.snapshot_prior()`.
- Track the two checkpoint cadence counters: `next_checkpoint_at_step`
  and `next_checkpoint_at_time`. Fire whichever first; reset both after
  fire. Phase-1 defaults: N=5000 steps, M=300 seconds.
- Handle SIGTERM via the shared `stop_event`:
  - Finish the current step's optimizer.step() (don't abort mid-backward).
  - Post a final checkpoint request to `artifact_publisher` (best-effort).
  - Flush metrics via `metrics_emitter.shutdown(timeout=10)`.
  - Exit the loop; daemon thread terminates.
- Detect catastrophic conditions and fail loudly per Q10-ADR-004:
  - NaN in `loss_result.total` → log + emit `sts2_q10_nan_loss_total`
    + set stop event + exit.
  - Q3 hard error from `data_ingest` → propagate + exit non-zero.
- Implement Phase-2+ freeze-unfreeze seam (deferred but plumbed):
  expose `apply_schedule_event(name: str)` that calls
  `optim.set_requires_grad(group, value)` based on a Phase-2 schedule
  config block. Phase-1: no-op.

Out of scope: loss math (`loss_engine`); optimizer mechanics (`optim`);
Q3 RPC details (`data_ingest`); artifact serialization
(`artifact_publisher`); metric emission shape (`metrics_emitter`).
`train_driver` is glue.

## Data Ownership

In-process, mutable:

- `step: int` — monotonically increasing step counter.
- `next_checkpoint_at_step: int`, `next_checkpoint_at_time: float` —
  cadence timers.
- `device: torch.device` — Phase-1 `"cuda:0"` if available else `"cpu"`;
  Phase-4 DDP-aware.
- Stop event reference (shared, not owned).

Schema-owning? **No.**

## Communication

**External:** none directly. Indirectly through submodules.

**Internal (in-process function calls, sync):**

- `TrainDriver(config, model, encoder, ingest, loss, optim, publisher,
  metrics) -> TrainDriver` — constructed once with all 7 dependencies.
- `start(stop_event)` — spawn the daemon thread; returns immediately.
- `current_step() -> int` — for `/metrics` polling.
- `apply_schedule_event(name)` — Phase-2+ hook.

**Metrics (emitted via `metrics_emitter`):**

- `sts2_q10_steps_total` — counter; current step.
- `sts2_q10_step_seconds` — gauge; wall-time of the last step body.
- `sts2_q10_loss_total` — gauge (Phase 1; histogram Phase 2).
- `sts2_q10_device{value="cuda:0"|"cpu"}` — gauge (1 when active).
- `sts2_q10_nan_loss_total` — counter; P0 alert if nonzero.

## Coupling

- **Afferent (in):** `service.py.start_background_threads`.
- **Efferent (out):** all other Q10 submodules.
- **Indirect:** Q7 (metrics); Q3 + Q5 (via the submodules).

## Concurrency contract

`train_driver` is the single consumer of `data_ingest`'s prefetch queue.
`train_driver` is the single producer to `artifact_publisher`'s publish
queue. Both queues are bounded; the loop blocks on `get_batch` (so GPU
idles when Q3 stalls — observable as queue-depth zero) but never blocks
on `request_publish` (drop-on-full per `artifact_publisher`).

The main HTTP server thread serves `/health` and `/metrics`. Those
handlers read `current_step()` and `metrics_emitter.format_metrics()`,
both thread-safe by design (`step` is read atomically; `metrics_emitter`
internalizes locking per the Q3 `MetricsEmitter` pattern).

## Testing Strategy

### Unit

1. **Cadence by step.** Configure `N=10`, `M=large` → after 10 steps,
   `request_publish` is called exactly once. Absent test: cadence
   misfires.
2. **Cadence by time.** Configure `N=large`, `M=1s` → after 1 second
   of mock-clocked steps, `request_publish` is called once. Verifies
   the OR semantics.
3. **NaN loss triggers shutdown.** Mock `loss_engine.compute` to return
   `total=NaN` → loop sets stop event and exits within 1 step. Absent
   test: training silently continues on broken gradients.
4. **SIGTERM mid-step completes the step.** Inject stop event mid-loop
   → current step's `optim.step` runs to completion before loop exit.
   Absent test: half-updated weights persisted as checkpoint.
5. **Empty queue blocks bounded.** `data_ingest.get_batch` returns
   `None` immediately (stop) → loop exits in <100ms. Absent test:
   shutdown hangs.
6. **Prior snapshot cadence.** Configure `kl_prior_refresh_steps=50` →
   `model.snapshot_prior()` called exactly 1× per 50 steps. Verifies
   the KL contract.

### Integration

1. **End-to-end 100 steps against mocked Q3/Q5.** Run 100 steps with
   degenerate batches → 1 checkpoint published (step=100 if cadence
   N=100), step counter at 100, no errors. Verifies the full hot path.
2. **Resume-on-restart.** Run 100 steps; SIGTERM; restart with the
   published `parent_artifact_id` → step counter resets to 0 but
   weights match step 100 bit-equal. Verifies the orchestration
   contract.
3. **Phase-2 schedule event seam.** `apply_schedule_event("freeze_combat")`
   → next step has combat-policy parameters with `requires_grad=False`.
   Verifies the Phase-2 readiness without Phase-1 burden.

### Smoke (mandatory)

- `train_driver` is the daemon thread that `pipeline/tests/smoke_services.py`
  spawns indirectly when starting the trainer service. Smoke confirms
  the service does NOT crash on missing Q3 (the prefetcher fails fast
  but the HTTP server remains up and `/health` responds).

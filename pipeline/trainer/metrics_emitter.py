"""Q10 trainer MetricsEmitter — Prometheus text v0.0.4 + W&B sidecar.

Per Q10-ADR-007 and `pipeline/trainer/docs/specs/modules/metrics-emitter.md`.

Counter / gauge name set is fixed at construction (strict-name pattern from
Q3): `inc`/`set` on an unknown name raises `KeyError`. All registered
counters and gauges are emitted from the first scrape with default
0 / 0.0 values, so dashboards have stable label sets.

W&B uploads run on a single daemon thread that drains a bounded
`queue.Queue(maxsize=1024)`. The thread is constructed iff
`wandb_enabled=True`. `wandb` itself is imported lazily inside the
daemon so the module imports without the dependency installed.

# StepStats is provided by pipeline.trainer.optim (S0.D). Until that lands,
# record_step accepts any object exposing `grad_norm_pre_clip`,
# `grad_norm_post_clip`, `lr`.
"""

from __future__ import annotations

import contextlib
import queue
import threading
import time
from typing import Any

from pipeline.common.prometheus import PrometheusLineBuilder

# ----- fixed counter / gauge name registries (spec §Fixed counter/gauge set) -----

# Counters with NO labels (start at 0).
_COUNTER_NAMES_NO_LABEL: tuple[str, ...] = (
    "sts2_q10_steps_total",
    "sts2_q10_sample_rows_total",
    "sts2_q10_nan_loss_total",
    "sts2_q10_source_perfect_leak_total",
    "sts2_q10_grad_clip_fired_total",
    "sts2_q10_wandb_dropped_total",
    "sts2_q10_consumed_traj_ids_total",
    "sts2_q10_encode_steps_total",
)

# Counters with a single label dimension; values per label-value start at 0.
# All declared label-values are emitted at construct so the label set is
# stable for dashboards / smoke tests.
_COUNTER_NAMES_LABELLED: dict[str, tuple[str, tuple[str, ...]]] = {
    "sts2_q10_sample_request_total": ("result", ("ok", "schema_drain", "error")),
    "sts2_q10_publish_total": ("result", ("ok", "error")),
}

# Gauges with NO labels (default 0 / 0.0).
_GAUGE_NAMES_NO_LABEL: tuple[str, ...] = (
    "sts2_q10_run_dirty",
    "sts2_q10_loss_total",
    "sts2_q10_kl_to_prior",
    "sts2_q10_lr",
    "sts2_q10_grad_norm",
    "sts2_q10_weight_norm",
    "sts2_q10_prefetch_queue_depth",
    "sts2_q10_last_published_step",
    "sts2_q10_artifact_size_bytes",
    "sts2_q10_onnx_export_seconds",
    "sts2_q10_publish_latency_seconds",
    "sts2_q10_step_seconds",
    "sts2_q10_encode_seconds_p99",
    "sts2_q10_model_param_count",
    "sts2_q10_model_prior_age_steps",
    "sts2_q10_device",
)

# Gauges with a single label dimension; values per label-value start at 0.0.
_GAUGE_NAMES_LABELLED: dict[str, tuple[str, tuple[str, ...]]] = {
    "sts2_q10_loss_component": (
        "head",
        ("policy", "combat_sample", "combat_summary", "hp_frac_aux", "kl_vs_prior"),
    ),
}

# Gauges rendered as floats (with .6f). Any gauge name not in this set is
# rendered as int. uptime is handled separately at scrape time.
_FLOAT_GAUGE_NAMES: frozenset[str] = frozenset(
    {
        "sts2_q10_loss_total",
        "sts2_q10_loss_component",
        "sts2_q10_kl_to_prior",
        "sts2_q10_lr",
        "sts2_q10_grad_norm",
        "sts2_q10_weight_norm",
        "sts2_q10_onnx_export_seconds",
        "sts2_q10_publish_latency_seconds",
        "sts2_q10_step_seconds",
        "sts2_q10_encode_seconds_p99",
    }
)

_GAUGE_FLOAT_FORMAT = ".6f"
_UPTIME_FLOAT_FORMAT = ".3f"


class MetricsEmitter:
    """Thread-safe Prometheus emitter + W&B sidecar for the Q10 trainer."""

    def __init__(
        self,
        service_name: str,
        started_at: float,
        wandb_enabled: bool,
        wandb_project: str | None = None,
        wandb_entity: str | None = None,
    ) -> None:
        self._service = str(service_name)
        self._started_at = float(started_at)
        self._wandb_enabled = bool(wandb_enabled)
        self._wandb_project = wandb_project
        self._wandb_entity = wandb_entity

        self._lock = threading.Lock()

        # Counter storage. Two flavors:
        #   no-label:  _counters[name] -> int
        #   labelled:  _counters_labelled[(name, label_value)] -> int
        self._counters: dict[str, int] = dict.fromkeys(_COUNTER_NAMES_NO_LABEL, 0)
        self._counters_labelled: dict[tuple[str, str], int] = {}
        for name, (_label_key, values) in _COUNTER_NAMES_LABELLED.items():
            for v in values:
                self._counters_labelled[(name, v)] = 0

        # Gauge storage. Same shape.
        self._gauges: dict[str, float] = dict.fromkeys(_GAUGE_NAMES_NO_LABEL, 0.0)
        self._gauges_labelled: dict[tuple[str, str], float] = {}
        for name, (_label_key, values) in _GAUGE_NAMES_LABELLED.items():
            for v in values:
                self._gauges_labelled[(name, v)] = 0.0

        # W&B daemon-thread plumbing.
        self._wandb_queue: queue.Queue[dict[str, Any]] | None = None
        self._wandb_thread: threading.Thread | None = None
        self._stop_event = threading.Event()
        if self._wandb_enabled:
            self._wandb_queue = queue.Queue(maxsize=1024)
            self._wandb_thread = threading.Thread(
                target=self._wandb_drain_loop,
                name="q10-wandb-drain",
                daemon=True,
            )
            self._wandb_thread.start()

    # -------------------------------------------------------------- inc / set

    def inc(
        self,
        name: str,
        n: int = 1,
        labels: dict[str, str] | None = None,
    ) -> None:
        """Increment a counter. KeyError on unknown name.

        For labelled counters, `labels` must contain the registered label
        key with one of the registered values; otherwise KeyError.
        """
        with self._lock:
            if name in self._counters:
                if labels:
                    # No-label counter cannot accept labels.
                    raise KeyError(f"counter {name!r} does not accept labels")
                self._counters[name] += int(n)
                return
            if name in _COUNTER_NAMES_LABELLED:
                label_key, _values = _COUNTER_NAMES_LABELLED[name]
                if not labels or label_key not in labels:
                    raise KeyError(f"counter {name!r} requires label {label_key!r}")
                key = (name, labels[label_key])
                if key not in self._counters_labelled:
                    raise KeyError(
                        f"counter {name!r} label {label_key}={labels[label_key]!r} not registered"
                    )
                self._counters_labelled[key] += int(n)
                return
            raise KeyError(name)

    def set(
        self,
        name: str,
        val: int | float,
        labels: dict[str, str] | None = None,
    ) -> None:
        """Set a gauge. KeyError on unknown name.

        For labelled gauges, `labels` must contain the registered label
        key with one of the registered values; otherwise KeyError.
        """
        with self._lock:
            if name in self._gauges:
                if labels:
                    raise KeyError(f"gauge {name!r} does not accept labels")
                self._gauges[name] = float(val)
                return
            if name in _GAUGE_NAMES_LABELLED:
                label_key, _values = _GAUGE_NAMES_LABELLED[name]
                if not labels or label_key not in labels:
                    raise KeyError(f"gauge {name!r} requires label {label_key!r}")
                key = (name, labels[label_key])
                if key not in self._gauges_labelled:
                    raise KeyError(
                        f"gauge {name!r} label {label_key}={labels[label_key]!r} not registered"
                    )
                self._gauges_labelled[key] = float(val)
                return
            raise KeyError(name)

    # ---------------------------------------------------------- record_step

    def record_step(
        self,
        step: int,
        loss_components: dict[str, float],
        step_stats: object | None = None,
    ) -> None:
        """Update per-step gauges/counters and enqueue a W&B payload.

        - Increments `sts2_q10_steps_total` by 1.
        - Sets `sts2_q10_loss_total` to `sum(loss_components.values())`.
        - For each `(head, val)` in `loss_components`, sets
          `sts2_q10_loss_component{head=<head>}` to `val`. Unknown head
          values raise `KeyError` (strict-name).
        - If `step_stats` is provided and duck-types as `StepStats`
          (attributes `grad_norm_pre_clip`, `grad_norm_post_clip`, `lr`):
          sets `sts2_q10_grad_norm` and `sts2_q10_lr`.
        - When `wandb_enabled=True`, enqueues
          `{"step": step, **loss_components, "lr": ...?}` (drop-oldest on
          overflow; increments `sts2_q10_wandb_dropped_total`).
        """
        self.inc("sts2_q10_steps_total")

        total = float(sum(loss_components.values()))
        self.set("sts2_q10_loss_total", total)
        for head, value in loss_components.items():
            self.set(
                "sts2_q10_loss_component",
                float(value),
                labels={"head": head},
            )

        payload: dict[str, Any] = {
            "step": int(step),
            **{k: float(v) for k, v in loss_components.items()},
        }

        if step_stats is not None:
            grad_post = getattr(step_stats, "grad_norm_post_clip", None)
            lr = getattr(step_stats, "lr", None)
            if grad_post is not None:
                self.set("sts2_q10_grad_norm", float(grad_post))
                payload["grad_norm"] = float(grad_post)
            if lr is not None:
                self.set("sts2_q10_lr", float(lr))
                payload["lr"] = float(lr)

        if self._wandb_enabled and self._wandb_queue is not None:
            self._enqueue_wandb(payload)

    # -------------------------------------------------------- format_metrics

    def format_metrics(self) -> bytes:
        """Render full Prometheus text v0.0.4 body.

        Output includes:
        - `sts2_service_up{service="<svc>"} 1`
        - `sts2_service_uptime_seconds{service="<svc>"} <float .3f>`
        - all registered Q10 counters and gauges (with their fixed label sets)
        Order: service lines first, then counters (no-label, then labelled
        sorted by (name, label-value)), then gauges (same shape).
        Lines are joined with `\\n` and trailing newline.
        """
        # Snapshot under lock.
        with self._lock:
            counters_no = dict(self._counters)
            counters_lab = dict(self._counters_labelled)
            gauges_no = dict(self._gauges)
            gauges_lab = dict(self._gauges_labelled)

        uptime = time.monotonic() - self._started_at
        builder = PrometheusLineBuilder(self._service)
        builder.gauge("sts2_service_up", value=1)
        builder.gauge(
            "sts2_service_uptime_seconds",
            value=uptime,
            float_format=_UPTIME_FLOAT_FORMAT,
        )

        # Counters — emit in spec-order then label-sorted for stable output.
        for name in _COUNTER_NAMES_NO_LABEL:
            builder.counter(name, value=counters_no[name])
        for name, (label_key, values) in _COUNTER_NAMES_LABELLED.items():
            for lv in values:
                builder.counter(
                    name,
                    labels={label_key: lv},
                    value=counters_lab[(name, lv)],
                )

        # Gauges — likewise.
        for name in _GAUGE_NAMES_NO_LABEL:
            val = gauges_no[name]
            if name in _FLOAT_GAUGE_NAMES:
                builder.gauge(name, value=val, float_format=_GAUGE_FLOAT_FORMAT)
            else:
                builder.gauge(name, value=val)
        for name, (label_key, values) in _GAUGE_NAMES_LABELLED.items():
            for lv in values:
                val = gauges_lab[(name, lv)]
                if name in _FLOAT_GAUGE_NAMES:
                    builder.gauge(
                        name,
                        labels={label_key: lv},
                        value=val,
                        float_format=_GAUGE_FLOAT_FORMAT,
                    )
                else:
                    builder.gauge(
                        name,
                        labels={label_key: lv},
                        value=val,
                    )

        body = b"\n".join(builder.lines()) + b"\n"
        return body

    # --------------------------------------------------------------- shutdown

    def shutdown(self, timeout: float = 10.0) -> None:
        """Stop the W&B daemon thread within `timeout` seconds.

        - Signals the drain loop to stop after its next get.
        - Joins with `timeout` budget. If the queue is large, the loop
          honors the stop event between dequeues, so worst case is one
          `wandb.log` call (or one drain attempt) past the signal.
        - Calls `wandb.finish()` once if W&B was enabled and importable.
        """
        if not self._wandb_enabled:
            return
        self._stop_event.set()
        if self._wandb_thread is not None:
            self._wandb_thread.join(timeout=timeout)
        # Best-effort wandb.finish — only if the module loaded earlier.
        try:
            import wandb  # type: ignore[import-not-found]
        except ImportError:
            return
        try:
            wandb.finish()
        except Exception:
            # Shutdown is best-effort; never raise from finalizer.
            pass

    # -------------------------------------------------------- W&B internals

    def _enqueue_wandb(self, payload: dict[str, Any]) -> None:
        """Put payload onto W&B queue with drop-oldest back-pressure."""
        assert self._wandb_queue is not None
        try:
            self._wandb_queue.put_nowait(payload)
        except queue.Full:
            # Drop oldest.
            with contextlib.suppress(queue.Empty):
                self._wandb_queue.get_nowait()
            # Count the drop. inc() takes the lock; safe under contention.
            self.inc("sts2_q10_wandb_dropped_total")
            try:
                self._wandb_queue.put_nowait(payload)
            except queue.Full:
                # Pathological: drop the newcomer too. Counter already inc'd.
                pass

    def _wandb_drain_loop(self) -> None:
        """Daemon thread body: drain queue → wandb.log; honor stop event.

        Imports `wandb` lazily so the module is importable without the
        dependency. ImportError ⇒ the loop drops all payloads silently
        (W&B was opted-in but the wheel is missing — log once would be
        ideal but the spec doesn't require it).
        """
        try:
            import wandb  # type: ignore[import-not-found]
        except ImportError:
            wandb = None  # type: ignore[assignment]

        # `wandb.init` is lazy — done once before the first log so we don't
        # pay setup cost when no events occur. For air-gapped environments
        # the user is expected to set WANDB_MODE=offline or similar.
        initialized = False

        assert self._wandb_queue is not None
        while not self._stop_event.is_set():
            try:
                payload = self._wandb_queue.get(timeout=1.0)
            except queue.Empty:
                continue
            if wandb is None:
                continue
            if not initialized:
                try:
                    wandb.init(
                        project=self._wandb_project,
                        entity=self._wandb_entity,
                        reinit=True,
                    )
                    initialized = True
                except Exception:
                    # Init failure: silently drop subsequent payloads.
                    wandb = None  # type: ignore[assignment]
                    continue
            try:
                wandb.log(payload)
            except Exception:
                # Per-payload log failures are non-fatal.
                pass

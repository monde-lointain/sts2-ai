"""Prometheus text emitter for Q3 metrics + smoke-required service lines.

Output line shape matches pipeline/common/service_host.py:28-34, i.e.:
    sts2_service_up{service="experience-store"} 1
    sts2_service_uptime_seconds{service="experience-store"} <float>
    sts2_q3_<name>{service="experience-store"} <value>

Phase-1 emitter is a flat counter+gauge map; the spec's full callable-
registry (control-plane.md ObservabilityAdapter section, register(...))
is a Phase-2 extension. Caller (service.py at W4) drives inc/set and
serves format_metrics() output on GET /metrics.

See pipeline/experience-store/docs/specs/modules/control-plane.md.
"""

from __future__ import annotations

import threading
import time

# Counters: monotonic, integer-valued.
_COUNTERS = (
    "sts2_q3_ingest_total",
    "sts2_q3_sample_total",
    "sts2_q3_retention_drops_total",
)
# Gauges: arbitrary numeric snapshots.
_GAUGES = (
    "sts2_q3_hot_tier_bytes",
    "sts2_q3_ingest_queue_depth",
)


class MetricsEmitter:
    """Thread-safe Prometheus text v0.0.4 emitter.

    Carries a fixed set of Q3 counters and gauges (Phase-1 surface).
    Constants `sts2_service_up` (literal 1) and `sts2_service_uptime_seconds`
    (computed from started_at_monotonic) are emitted to keep
    pipeline/tests/smoke_services.py green.
    """

    def __init__(self, service_name: str, started_at_monotonic: float) -> None:
        self._service = str(service_name)
        self._started_at = float(started_at_monotonic)
        self._lock = threading.Lock()
        self._counters: dict[str, int] = {name: 0 for name in _COUNTERS}
        self._gauges: dict[str, float] = {name: 0.0 for name in _GAUGES}

    def inc(self, name: str, n: int = 1) -> None:
        """Increment a counter by `n` (default 1). Unknown name raises KeyError."""
        with self._lock:
            if name not in self._counters:
                raise KeyError(f"unknown counter: {name}")
            self._counters[name] += int(n)

    def set(self, name: str, val: int | float) -> None:
        """Set a gauge to `val`. Unknown name raises KeyError."""
        with self._lock:
            if name not in self._gauges:
                raise KeyError(f"unknown gauge: {name}")
            self._gauges[name] = float(val)

    def format_metrics(self) -> bytes:
        """Serialize all metrics as Prometheus text; UTF-8 bytes."""
        with self._lock:
            counters = dict(self._counters)
            gauges = dict(self._gauges)
        uptime = time.monotonic() - self._started_at
        lines = [
            f'sts2_service_up{{service="{self._service}"}} 1',
            f'sts2_service_uptime_seconds{{service="{self._service}"}} {uptime:.3f}',
        ]
        for name in _COUNTERS:
            lines.append(
                f'{name}{{service="{self._service}"}} {counters[name]}'
            )
        for name in _GAUGES:
            value = gauges[name]
            # Render integer-valued gauges without trailing decimals to
            # keep the bytes test stable; otherwise three-decimal float.
            if float(value).is_integer():
                rendered = str(int(value))
            else:
                rendered = f"{value:.3f}"
            lines.append(
                f'{name}{{service="{self._service}"}} {rendered}'
            )
        body = "\n".join(lines) + "\n"
        return body.encode("utf-8")

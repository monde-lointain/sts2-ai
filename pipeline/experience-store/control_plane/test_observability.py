"""Unit tests for MetricsEmitter (S0.B.gamma)."""

from __future__ import annotations

import time

import pytest

from control_plane.observability import MetricsEmitter

SERVICE = "experience-store"


def make_emitter() -> MetricsEmitter:
    return MetricsEmitter(SERVICE, started_at_monotonic=time.monotonic())


def test_format_metrics_includes_all_required_lines():
    emitter = make_emitter()
    text = emitter.format_metrics().decode("utf-8")

    # Smoke-required lines (compat with service_host.py:31-32).
    assert f'sts2_service_up{{service="{SERVICE}"}} 1' in text
    assert f'sts2_service_uptime_seconds{{service="{SERVICE}"}}' in text

    # Q3 counters.
    assert f'sts2_q3_ingest_total{{service="{SERVICE}"}} 0' in text
    assert f'sts2_q3_sample_total{{service="{SERVICE}"}} 0' in text
    assert f'sts2_q3_retention_drops_total{{service="{SERVICE}"}} 0' in text

    # Q3 gauges (initial value 0).
    assert f'sts2_q3_hot_tier_bytes{{service="{SERVICE}"}} 0' in text
    assert f'sts2_q3_ingest_queue_depth{{service="{SERVICE}"}} 0' in text


def test_format_metrics_returns_bytes():
    emitter = make_emitter()
    out = emitter.format_metrics()
    assert isinstance(out, bytes)
    # UTF-8 decodable; last byte is newline.
    text = out.decode("utf-8")
    assert text.endswith("\n")


def test_inc_increments_counter():
    emitter = make_emitter()
    emitter.inc("sts2_q3_ingest_total", 3)
    emitter.inc("sts2_q3_ingest_total")  # default 1
    text = emitter.format_metrics().decode("utf-8")
    assert f'sts2_q3_ingest_total{{service="{SERVICE}"}} 4' in text


def test_inc_unknown_counter_raises():
    emitter = make_emitter()
    with pytest.raises(KeyError):
        emitter.inc("sts2_q3_does_not_exist")


def test_set_assigns_gauge():
    emitter = make_emitter()
    emitter.set("sts2_q3_hot_tier_bytes", 1234567)
    emitter.set("sts2_q3_ingest_queue_depth", 42)
    text = emitter.format_metrics().decode("utf-8")
    assert f'sts2_q3_hot_tier_bytes{{service="{SERVICE}"}} 1234567' in text
    assert f'sts2_q3_ingest_queue_depth{{service="{SERVICE}"}} 42' in text


def test_set_unknown_gauge_raises():
    emitter = make_emitter()
    with pytest.raises(KeyError):
        emitter.set("sts2_q3_does_not_exist", 1)


def test_uptime_seconds_increases_monotonically():
    started = time.monotonic()
    emitter = MetricsEmitter(SERVICE, started_at_monotonic=started)
    text_a = emitter.format_metrics().decode("utf-8")
    time.sleep(0.05)
    text_b = emitter.format_metrics().decode("utf-8")

    # Locate the uptime value in each scrape and assert strictly greater.
    def uptime_value(text: str) -> float:
        for line in text.splitlines():
            if line.startswith("sts2_service_uptime_seconds"):
                return float(line.rsplit(" ", 1)[1])
        raise AssertionError("uptime line missing")

    assert uptime_value(text_b) > uptime_value(text_a)


def test_thread_safety_under_concurrent_inc():
    import threading

    emitter = MetricsEmitter(SERVICE, started_at_monotonic=time.monotonic())
    iterations = 1000

    def worker() -> None:
        for _ in range(iterations):
            emitter.inc("sts2_q3_ingest_total")

    threads = [threading.Thread(target=worker) for _ in range(8)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    text = emitter.format_metrics().decode("utf-8")
    expected = iterations * 8
    assert f'sts2_q3_ingest_total{{service="{SERVICE}"}} {expected}' in text


def test_smoke_compatible_first_lines():
    """smoke_services.py greps for sts2_service_up; we keep its format stable."""
    emitter = make_emitter()
    text = emitter.format_metrics().decode("utf-8")
    expected = f'sts2_service_up{{service="{SERVICE}"}} 1'
    assert expected in text

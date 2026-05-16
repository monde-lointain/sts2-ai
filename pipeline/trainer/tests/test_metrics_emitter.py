"""Unit tests for `pipeline.trainer.metrics_emitter.MetricsEmitter`.

Covers the spec's Phase-1 mandatory tests (metrics-emitter.md §Unit):
1. Unknown counter/gauge name → KeyError.
2. Thread-safe counter increment under contention.
3. Prometheus output contains the two `sts2_service_*` lines (smoke contract).
4. `wandb_enabled=False` → no daemon thread spawned.
5. W&B drop-oldest back-pressure on queue full.
6. `shutdown(timeout)` returns within bound.

Plus several record_step / labelled-metric / fixed-name tests.
"""

from __future__ import annotations

import threading
import time

import pytest

from pipeline.trainer.metrics_emitter import MetricsEmitter

# ----------------------------------------------------- helpers / fixtures


def _make(wandb_enabled: bool = False) -> MetricsEmitter:
    return MetricsEmitter(
        service_name="trainer",
        started_at=time.monotonic(),
        wandb_enabled=wandb_enabled,
    )


# ----------------------------------------------------------- inc / set / KeyError


def test_inc_unknown_name_raises_keyerror() -> None:
    m = _make()
    with pytest.raises(KeyError):
        m.inc("not_registered")
    m.shutdown()


def test_set_unknown_name_raises_keyerror() -> None:
    m = _make()
    with pytest.raises(KeyError):
        m.set("not_registered", 1.0)
    m.shutdown()


def test_inc_labelled_counter_unknown_label_value_raises() -> None:
    m = _make()
    with pytest.raises(KeyError):
        m.inc(
            "sts2_q10_sample_request_total",
            labels={"result": "bogus"},
        )
    m.shutdown()


def test_inc_labelled_counter_missing_label_key_raises() -> None:
    m = _make()
    with pytest.raises(KeyError):
        m.inc("sts2_q10_sample_request_total")  # no labels
    m.shutdown()


def test_set_labelled_gauge_unknown_head_raises_keyerror() -> None:
    m = _make()
    with pytest.raises(KeyError):
        m.set("sts2_q10_loss_component", 1.0, labels={"head": "bogus"})
    m.shutdown()


def test_inc_no_label_counter_with_labels_raises() -> None:
    m = _make()
    with pytest.raises(KeyError):
        m.inc("sts2_q10_steps_total", labels={"foo": "bar"})
    m.shutdown()


# --------------------------------------------------------- thread safety


def test_thread_safe_increment_under_contention() -> None:
    """100 threads x 1000 increments → exactly 100_000."""
    m = _make()
    threads_n = 100
    per_thread = 1000

    def worker() -> None:
        for _ in range(per_thread):
            m.inc("sts2_q10_steps_total")

    threads = [threading.Thread(target=worker) for _ in range(threads_n)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    body = m.format_metrics().decode("utf-8")
    expected = threads_n * per_thread
    expected_line = f'sts2_q10_steps_total{{service="trainer"}} {expected}'
    assert expected_line in body, body
    m.shutdown()


# ---------------------------------------------------------- format_metrics


def test_format_metrics_contains_service_up_and_uptime() -> None:
    m = _make()
    body = m.format_metrics().decode("utf-8")
    assert 'sts2_service_up{service="trainer"} 1' in body
    # uptime is a positive float .3f.
    assert 'sts2_service_uptime_seconds{service="trainer"}' in body
    # Validate the uptime field parses as float.
    for line in body.splitlines():
        if line.startswith("sts2_service_uptime_seconds"):
            value = line.split("} ")[-1]
            assert float(value) >= 0.0
            break
    else:  # pragma: no cover - sanity
        pytest.fail("uptime line not emitted")
    m.shutdown()


def test_format_metrics_emits_all_registered_counters_at_zero() -> None:
    m = _make()
    body = m.format_metrics().decode("utf-8")
    # Spot-check key counters present at 0.
    for name in (
        "sts2_q10_steps_total",
        "sts2_q10_nan_loss_total",
        "sts2_q10_wandb_dropped_total",
    ):
        assert f'{name}{{service="trainer"}} 0' in body, name
    # Labelled counter: all three result values emitted.
    for result in ("ok", "schema_drain", "error"):
        line = f'sts2_q10_sample_request_total{{result="{result}",service="trainer"}} 0'
        assert line in body, line
    m.shutdown()


def test_format_metrics_emits_loss_component_for_all_heads_at_zero() -> None:
    m = _make()
    body = m.format_metrics().decode("utf-8")
    for head in (
        "policy",
        "combat_sample",
        "combat_summary",
        "hp_frac_aux",
        "kl_vs_prior",
    ):
        # float gauge rendered with .6f
        line = f'sts2_q10_loss_component{{head="{head}",service="trainer"}} 0.000000'
        assert line in body, line
    m.shutdown()


def test_set_loss_total_renders_as_float() -> None:
    m = _make()
    m.set("sts2_q10_loss_total", 3.14)
    body = m.format_metrics().decode("utf-8")
    assert 'sts2_q10_loss_total{service="trainer"} 3.140000' in body
    m.shutdown()


def test_format_metrics_body_ends_with_newline() -> None:
    m = _make()
    body = m.format_metrics()
    assert body.endswith(b"\n")
    m.shutdown()


# ------------------------------------------------------------- record_step


class _FakeStepStats:
    def __init__(self, grad_pre: float, grad_post: float, lr: float) -> None:
        self.grad_norm_pre_clip = grad_pre
        self.grad_norm_post_clip = grad_post
        self.lr = lr


def test_record_step_increments_steps_and_sets_loss_total() -> None:
    m = _make()
    m.record_step(
        step=42,
        loss_components={"policy": 1.0, "combat_sample": 2.0},
    )
    body = m.format_metrics().decode("utf-8")
    assert 'sts2_q10_steps_total{service="trainer"} 1' in body
    assert 'sts2_q10_loss_total{service="trainer"} 3.000000' in body
    assert 'sts2_q10_loss_component{head="policy",service="trainer"} 1.000000' in body
    assert 'sts2_q10_loss_component{head="combat_sample",service="trainer"} 2.000000' in body
    m.shutdown()


def test_record_step_with_step_stats_sets_grad_norm_and_lr() -> None:
    m = _make()
    m.record_step(
        step=1,
        loss_components={"policy": 0.5},
        step_stats=_FakeStepStats(grad_pre=2.0, grad_post=1.5, lr=1e-3),
    )
    body = m.format_metrics().decode("utf-8")
    assert 'sts2_q10_grad_norm{service="trainer"} 1.500000' in body
    assert 'sts2_q10_lr{service="trainer"} 0.001000' in body
    m.shutdown()


def test_record_step_unknown_head_raises_keyerror() -> None:
    m = _make()
    with pytest.raises(KeyError):
        m.record_step(step=0, loss_components={"bogus_head": 1.0})
    m.shutdown()


# ---------------------------------------------------------- W&B disabled


def test_wandb_disabled_spawns_no_daemon_thread() -> None:
    """With wandb_enabled=False, no daemon thread should be added."""
    before = {t.ident for t in threading.enumerate()}
    m = MetricsEmitter(
        service_name="trainer",
        started_at=time.monotonic(),
        wandb_enabled=False,
    )
    after = {t.ident for t in threading.enumerate()}
    new = after - before
    assert new == set(), f"unexpected new threads: {new}"
    m.shutdown()


def test_wandb_enabled_spawns_one_daemon_thread() -> None:
    before = {t.ident for t in threading.enumerate()}
    m = MetricsEmitter(
        service_name="trainer",
        started_at=time.monotonic(),
        wandb_enabled=True,
    )
    after = {t.ident for t in threading.enumerate()}
    new = after - before
    assert len(new) == 1, f"expected 1 new thread, got {new}"
    # Drain thread should be a daemon.
    for t in threading.enumerate():
        if t.ident in new:
            assert t.daemon is True
            assert t.name == "q10-wandb-drain"
    m.shutdown(timeout=5.0)


# -------------------------------------------------- W&B drop-oldest policy


def test_wandb_drop_oldest_increments_dropped_counter() -> None:
    """Fill the W&B queue to capacity, then push one more; expect:
    - the dropped counter increments by 1
    - queue size remains at capacity
    """
    m = MetricsEmitter(
        service_name="trainer",
        started_at=time.monotonic(),
        wandb_enabled=True,
    )
    # Block the drain thread by signalling stop and giving it a beat to
    # exit so the queue doesn't drain mid-fill.
    m._stop_event.set()
    if m._wandb_thread is not None:
        m._wandb_thread.join(timeout=2.0)

    assert m._wandb_queue is not None
    capacity = m._wandb_queue.maxsize
    assert capacity == 1024
    # Fill to capacity directly (bypass _enqueue_wandb's overflow handler
    # so we can assert the handler increments the counter on the 1025th).
    for i in range(capacity):
        m._wandb_queue.put_nowait({"i": i})
    assert m._wandb_queue.qsize() == capacity

    # Now exercise the overflow path.
    m._enqueue_wandb({"i": capacity})
    body = m.format_metrics().decode("utf-8")
    assert 'sts2_q10_wandb_dropped_total{service="trainer"} 1' in body, body
    assert m._wandb_queue.qsize() == capacity


# -------------------------------------------------------- shutdown bounded


def test_shutdown_returns_within_timeout_when_queue_is_full() -> None:
    """Even with 1024 entries queued, shutdown(timeout=2) must return."""
    m = MetricsEmitter(
        service_name="trainer",
        started_at=time.monotonic(),
        wandb_enabled=True,
    )
    assert m._wandb_queue is not None
    # Fill queue to capacity.
    for i in range(m._wandb_queue.maxsize):
        try:
            m._wandb_queue.put_nowait({"i": i})
        except Exception:
            break

    t0 = time.monotonic()
    m.shutdown(timeout=2.0)
    elapsed = time.monotonic() - t0
    assert elapsed < 3.0, f"shutdown blocked {elapsed:.2f}s"
    # Drain thread should be gone.
    assert m._wandb_thread is None or not m._wandb_thread.is_alive()


def test_shutdown_noop_when_wandb_disabled() -> None:
    m = _make()
    # Should not raise nor block.
    t0 = time.monotonic()
    m.shutdown(timeout=5.0)
    elapsed = time.monotonic() - t0
    assert elapsed < 0.5

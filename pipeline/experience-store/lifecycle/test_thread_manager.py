"""Unit tests for LifecycleThreadManager (R3b.2).

Decouples daemon-thread mechanics from tick logic so each can be tested
independently of the other.
"""

from __future__ import annotations

import threading
import time

import pytest

from lifecycle.thread_manager import LifecycleThreadManager


def test_thread_manager_calls_tick_until_stop():
    calls: list[float] = []
    mgr = LifecycleThreadManager(
        tick_fn=lambda: calls.append(time.monotonic()),
        interval_fn=lambda: 0.01,
    )
    stop = threading.Event()
    mgr.start(stop)
    time.sleep(0.05)
    stop.set()
    mgr.join(timeout=1.0)
    assert len(calls) >= 3
    assert not mgr.is_alive()


def test_thread_manager_double_start_raises():
    mgr = LifecycleThreadManager(tick_fn=lambda: None, interval_fn=lambda: 1.0)
    stop = threading.Event()
    mgr.start(stop)
    try:
        with pytest.raises(RuntimeError):
            mgr.start(stop)
    finally:
        stop.set()
        mgr.join(timeout=1.0)


def test_thread_manager_on_error_invoked():
    seen: list[Exception] = []

    def boom() -> None:
        raise RuntimeError("kaboom")

    mgr = LifecycleThreadManager(
        tick_fn=boom,
        interval_fn=lambda: 0.01,
        on_error=seen.append,
    )
    stop = threading.Event()
    mgr.start(stop)
    time.sleep(0.05)
    stop.set()
    mgr.join(timeout=1.0)
    assert seen and isinstance(seen[0], RuntimeError)

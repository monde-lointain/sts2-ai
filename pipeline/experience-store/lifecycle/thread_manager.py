"""Background daemon thread for lifecycle ticks.

Decoupled from ``Lifecycle`` so the tick logic can be unit-tested without
spinning a thread. Owns the daemon-thread lifecycle (start/stop/join) and
the inter-tick wait against the shutdown signal. Tick exceptions are
forwarded to ``on_error`` so the caller (Lifecycle) can audit them
without the loop tearing down.
"""

from __future__ import annotations

import threading
from collections.abc import Callable


class LifecycleThreadManager:
    """Owns the lifecycle background thread; calls ``tick_fn`` every interval.

    The first tick fires immediately so smoke tests observe progress within
    the first interval (spec line 162-163: /metrics must include
    ``last_tick_ts_ns`` "after the first tick"). Subsequent ticks wait
    ``interval_fn()`` seconds (re-read each iter so policy changes take
    effect on the next wait).
    """

    def __init__(
        self,
        tick_fn: Callable[[], None],
        interval_fn: Callable[[], float],
        on_error: Callable[[Exception], None] | None = None,
    ) -> None:
        self._tick_fn = tick_fn
        self._interval_fn = interval_fn
        self._on_error = on_error
        self._thread: threading.Thread | None = None
        self._stop_event: threading.Event | None = None

    def start(self, stop_event: threading.Event) -> None:
        """Spawn the daemon thread. Idempotent: a second call while alive raises."""
        if self._thread is not None and self._thread.is_alive():
            raise RuntimeError("Lifecycle background thread already running")
        self._stop_event = stop_event
        self._thread = threading.Thread(
            target=self._run_loop,
            name="lifecycle-tick",
            daemon=True,
        )
        self._thread.start()

    def join(self, timeout: float | None = None) -> None:
        if self._thread is not None:
            self._thread.join(timeout)

    def is_alive(self) -> bool:
        return self._thread is not None and self._thread.is_alive()

    def _run_loop(self) -> None:
        assert self._stop_event is not None
        first = True
        while not self._stop_event.is_set():
            if first:
                first = False
            else:
                interval = self._interval_fn()
                if self._stop_event.wait(timeout=interval):
                    break
            try:
                self._tick_fn()
            except Exception as exc:
                if self._on_error is not None:
                    try:
                        self._on_error(exc)
                    except Exception:
                        # If even the error sink is broken, swallow rather
                        # than tear down the daemon.
                        pass

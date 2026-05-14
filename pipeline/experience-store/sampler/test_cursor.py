"""Tests for the CursorCache LRU + idle-eviction semantics (S0.C.beta)."""

from __future__ import annotations

import threading

import pytest

from sampler.cursor import CursorCache, CursorState


class _FakeClock:
    """Deterministic monotonic ns clock for idle-eviction tests."""

    def __init__(self, start_ns: int = 0) -> None:
        self.ns = int(start_ns)

    def __call__(self) -> int:
        return self.ns

    def advance_seconds(self, s: float) -> None:
        self.ns += int(s * 1_000_000_000)


def _state(mode: str = "uniform") -> CursorState:
    return CursorState(mode=mode, filters={}, position_ts_ns=0, served_count=0)


# ---------- basic insert/get/update/delete ----------


def test_create_then_get_round_trip() -> None:
    cache = CursorCache()
    cid = cache.create(_state())
    assert isinstance(cid, str) and len(cid) == 32
    got = cache.get(cid)
    assert got is not None
    assert got.mode == "uniform"


def test_get_missing_returns_none() -> None:
    cache = CursorCache()
    assert cache.get("does-not-exist") is None


def test_update_advances_state() -> None:
    cache = CursorCache()
    cid = cache.create(_state())
    advanced = CursorState(
        mode="uniform", filters={}, position_ts_ns=999, served_count=512
    )
    cache.update(cid, advanced)
    got = cache.get(cid)
    assert got is not None
    assert got.position_ts_ns == 999
    assert got.served_count == 512


def test_update_unknown_is_noop() -> None:
    cache = CursorCache()
    advanced = CursorState(mode="uniform", filters={})
    cache.update("nonexistent-cursor-id", advanced)
    # No exception, no insert
    assert len(cache) == 0


def test_delete_present() -> None:
    cache = CursorCache()
    cid = cache.create(_state())
    assert cache.delete(cid) is True
    assert cache.get(cid) is None
    assert cache.delete(cid) is False


def test_count_equals_len() -> None:
    cache = CursorCache(capacity=8)
    for _ in range(3):
        cache.create(_state())
    assert cache.count() == 3
    assert len(cache) == 3


# ---------- LRU bounded capacity ----------


def test_lru_eviction_at_capacity() -> None:
    cache = CursorCache(capacity=3)
    cids = [cache.create(_state()) for _ in range(3)]
    assert len(cache) == 3
    # Insert a 4th -> oldest (cids[0]) evicted.
    cids.append(cache.create(_state()))
    assert len(cache) == 3
    assert cache.get(cids[0]) is None
    for cid in cids[1:]:
        assert cache.get(cid) is not None


def test_lru_recently_used_survives() -> None:
    cache = CursorCache(capacity=3)
    a = cache.create(_state())
    b = cache.create(_state())
    c = cache.create(_state())
    # Touch `a` -> becomes most-recent.
    cache.get(a)
    # Insert a 4th -> `b` (oldest now) evicted, `a` survives.
    cache.create(_state())
    assert cache.get(a) is not None
    assert cache.get(b) is None
    assert cache.get(c) is not None


def test_capacity_must_be_positive() -> None:
    with pytest.raises(ValueError):
        CursorCache(capacity=0)


def test_idle_timeout_must_be_positive() -> None:
    with pytest.raises(ValueError):
        CursorCache(idle_timeout_seconds=0)


# ---------- idle eviction ----------


def test_idle_eviction_on_access() -> None:
    clock = _FakeClock(start_ns=1_000_000_000_000)
    cache = CursorCache(idle_timeout_seconds=5, now_ns=clock)
    cid = cache.create(_state())
    # Advance past idle timeout
    clock.advance_seconds(6)
    # Any access triggers eviction
    assert cache.get(cid) is None
    assert len(cache) == 0


def test_idle_eviction_does_not_drop_fresh_entries() -> None:
    clock = _FakeClock(start_ns=1_000_000_000_000)
    cache = CursorCache(idle_timeout_seconds=10, now_ns=clock)
    old = cache.create(_state())
    clock.advance_seconds(7)
    fresh = cache.create(_state())
    # Old will hit timeout at t=10s; fresh at t=17s.
    clock.advance_seconds(4)  # now t=11s after `old`, t=4s after `fresh`
    # Any access triggers eviction sweep
    assert cache.get(fresh) is not None
    assert cache.get(old) is None


def test_touch_resets_idle_clock() -> None:
    clock = _FakeClock(start_ns=1_000_000_000_000)
    cache = CursorCache(idle_timeout_seconds=10, now_ns=clock)
    cid = cache.create(_state())
    clock.advance_seconds(8)
    # Touch the cursor; idle clock resets
    cache.get(cid)
    clock.advance_seconds(8)
    # Total wall-time = 16s but only 8s idle since touch
    assert cache.get(cid) is not None


# ---------- thread safety ----------


def test_create_concurrent_does_not_drop() -> None:
    cache = CursorCache(capacity=1024)
    n_threads = 16
    per_thread = 50
    cids: list[str] = []
    lock = threading.Lock()

    def worker() -> None:
        local: list[str] = []
        for _ in range(per_thread):
            local.append(cache.create(_state()))
        with lock:
            cids.extend(local)

    threads = [threading.Thread(target=worker) for _ in range(n_threads)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    assert len(cids) == n_threads * per_thread
    # All ids should be unique and present
    assert len(set(cids)) == len(cids)
    for cid in cids:
        assert cache.get(cid) is not None


def test_snapshot_shape() -> None:
    s = CursorState(
        mode="uniform",
        filters={"model_version": ["v2"]},
        position_ts_ns=123,
        step_offset=2,
        served_count=64,
        last_touch_ts_ns=999,
    )
    snap = s.snapshot()
    assert snap == {
        "mode": "uniform",
        "filters": {"model_version": ["v2"]},
        "position_ts_ns": 123,
        "step_offset": 2,
        "served_count": 64,
        "last_touch_ts_ns": 999,
    }

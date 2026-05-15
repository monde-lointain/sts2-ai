"""Unit tests for pipeline.trainer.data_ingest.

Covers items 1-6 of data-ingest.md §Testing Strategy plus the two
sampling-mode validation tests (7-8). All Q3 interaction is mocked via
``unittest.mock.patch`` on ``urllib.request.urlopen``; ``time.sleep`` is
patched to a stub so 503-retry delays don't slow the suite.
"""
from __future__ import annotations

import io
import json
import threading
import time
from typing import Optional
from unittest import mock

import pytest

from pipeline.common.framing import encode_frames
from pipeline.common.trajectory_proto import TrajectoryStep
from pipeline.trainer.data_ingest import DataIngest, Q3UnavailableError
from pipeline.trainer.run_config import (
    CheckpointConfig,
    LossWeights,
    NetworkConfig,
    OptimConfig,
    RunConfig,
    WandbConfig,
)


# ---------------------------------------------------------------------------
# Fixtures / helpers
# ---------------------------------------------------------------------------
def _make_config(
    *,
    sampling_mode: str = "uniform",
    batch_size: int = 4,
    prefetch_queue_size: int = 2,
    q3_url: str = "http://127.0.0.1:0",
) -> RunConfig:
    """Build a minimal frozen RunConfig for DataIngest tests."""
    return RunConfig(
        service="trainer",
        port=0,
        data_dir="data/trainer",
        q3_url=q3_url,
        q5_url="http://127.0.0.1:0",
        parent_artifact_id=None,
        seed=0,
        refuse_on_dirty=False,
        sampling_mode=sampling_mode,
        prefetch_queue_size=prefetch_queue_size,
        batch_size=batch_size,
        network=NetworkConfig(
            expected_token_count=None,
            d_model=8,
            n_layers=1,
            n_heads=1,
            ffn_dim=8,
            max_seq_len=4,
        ),
        optim=OptimConfig(
            lr=1e-3,
            weight_decay=0.0,
            warmup_steps=1,
            total_steps=1,
            grad_clip=1.0,
        ),
        loss_weights=LossWeights(
            policy=1.0,
            combat_sample=1.0,
            combat_summary=1.0,
            hp_frac_aux=1.0,
            kl_beta=0.0,
        ),
        checkpoint=CheckpointConfig(every_n_steps=1, every_m_minutes=1),
        wandb=WandbConfig(enabled=False),
        run_id="01TEST00000000000000000000",
    )


def _trajectory_step(action_id: int = 0) -> TrajectoryStep:
    """Build a minimal TrajectoryStep instance."""
    step = TrajectoryStep()
    step.action_taken = action_id
    step.reward = 0.0
    step.terminal = False
    return step


def _frame_response(
    steps: list[TrajectoryStep], trailer_status: str = "ok"
) -> bytes:
    """Build a length-delimited protobuf body + JSON trailer frame."""
    parts: list[bytes] = []
    for step in steps:
        parts.append(step.SerializeToString())
    parts.append(
        json.dumps({"status": trailer_status}, separators=(",", ":")).encode("utf-8")
    )
    return encode_frames(parts)


class _FakeResponse:
    """Minimal urllib.request.urlopen response mock supporting context manager."""

    def __init__(
        self,
        body: bytes,
        status: int = 200,
        headers: Optional[dict[str, str]] = None,
    ) -> None:
        self._body = body
        self._status = status
        self.headers = headers or {}

    def __enter__(self) -> "_FakeResponse":
        return self

    def __exit__(self, *exc_info: object) -> None:
        return None

    def read(self) -> bytes:
        return self._body

    def getcode(self) -> int:
        return self._status


def _http_error_503(retry_after_sec: int = 1) -> Exception:
    """Build an HTTPError instance with a JSON 503 schema_drain body."""
    import urllib.error

    body = json.dumps(
        {"reason": "schema_drain", "retry_after_sec": retry_after_sec}
    ).encode("utf-8")
    return urllib.error.HTTPError(
        url="http://localhost/sample",
        code=503,
        msg="Service Unavailable",
        hdrs=None,  # type: ignore[arg-type]
        fp=io.BytesIO(body),
    )


# ---------------------------------------------------------------------------
# Test 1 — Varint framing decode roundtrip
# ---------------------------------------------------------------------------
def test_varint_framing_decode_roundtrip() -> None:
    """N protobuf messages with varint-prefixed lengths decode back to N
    identical messages. Exercises the pipeline.common.framing primitives
    that data_ingest relies on (encode_frames / iter_frames)."""
    from pipeline.common.framing import iter_frames

    steps = [_trajectory_step(action_id=i) for i in range(5)]
    payload = encode_frames([s.SerializeToString() for s in steps])
    decoded_frames = list(iter_frames(payload))
    assert len(decoded_frames) == len(steps)
    for original, raw in zip(steps, decoded_frames):
        round_trip = TrajectoryStep()
        round_trip.ParseFromString(raw)
        assert round_trip.action_taken == original.action_taken


# ---------------------------------------------------------------------------
# Test 2 — Trailer frame terminates the batch
# ---------------------------------------------------------------------------
def test_trailer_exhausted_yields_none_cursor() -> None:
    """Stream ends with ``{"status":"exhausted"}`` -> Batch.cursor_token is None."""
    cfg = _make_config(prefetch_queue_size=4)
    di = DataIngest(cfg)
    steps = [_trajectory_step(i) for i in range(3)]
    body = _frame_response(steps, trailer_status="exhausted")
    fake = _FakeResponse(body, status=200, headers={"X-Sts2-Q3-Trailer": "exhausted"})
    stop_event = threading.Event()
    with mock.patch(
        "pipeline.trainer.data_ingest.urllib.request.urlopen",
        return_value=fake,
    ):
        di.start(stop_event)
        batch = di.get_batch(timeout=2.0)
        stop_event.set()
        di.join(timeout=2.0)
    assert batch is not None
    assert batch.cursor_token is None
    assert len(batch.steps) == 3


# ---------------------------------------------------------------------------
# Test 3 — 503 schema-drain retries once with advertised delay
# ---------------------------------------------------------------------------
def test_503_schema_drain_retries_then_succeeds() -> None:
    """First call raises 503 with retry_after_sec=7; second call returns
    200. time.sleep is called with 7. Subsequent get_batch returns the
    decoded batch from the second call."""
    cfg = _make_config(prefetch_queue_size=4)
    di = DataIngest(cfg)
    steps = [_trajectory_step(i) for i in range(2)]
    body = _frame_response(steps, trailer_status="ok")
    good = _FakeResponse(body, status=200, headers={"X-Sts2-Q3-Trailer": "ok"})
    err503 = _http_error_503(retry_after_sec=7)
    sleep_calls: list[float] = []

    def fake_sleep(seconds: float) -> None:
        sleep_calls.append(seconds)

    stop_event = threading.Event()
    with mock.patch(
        "pipeline.trainer.data_ingest.urllib.request.urlopen",
        side_effect=[err503, good],
    ), mock.patch(
        "pipeline.trainer.data_ingest.time.sleep",
        side_effect=fake_sleep,
    ):
        di.start(stop_event)
        batch = di.get_batch(timeout=2.0)
        stop_event.set()
        di.join(timeout=2.0)
    assert batch is not None
    assert len(batch.steps) == 2
    assert sleep_calls == [7.0]


# ---------------------------------------------------------------------------
# Test 4 — Two consecutive 503s raise Q3UnavailableError
# ---------------------------------------------------------------------------
def test_two_consecutive_503s_raise_unavailable() -> None:
    """Two consecutive 503s -> the prefetcher captures Q3UnavailableError;
    get_batch re-raises it to the consumer per Q10-ADR-004 fail-fast."""
    cfg = _make_config(prefetch_queue_size=4)
    di = DataIngest(cfg)
    err503_a = _http_error_503(retry_after_sec=1)
    err503_b = _http_error_503(retry_after_sec=1)

    stop_event = threading.Event()
    with mock.patch(
        "pipeline.trainer.data_ingest.urllib.request.urlopen",
        side_effect=[err503_a, err503_b],
    ), mock.patch(
        "pipeline.trainer.data_ingest.time.sleep",
        return_value=None,
    ):
        di.start(stop_event)
        with pytest.raises(Q3UnavailableError):
            # Loop while waiting for the prefetcher to capture the
            # fatal error — get_batch re-raises once captured.
            for _ in range(20):
                result = di.get_batch(timeout=0.5)
                if result is None:
                    continue
        stop_event.set()
        di.join(timeout=2.0)


# ---------------------------------------------------------------------------
# Test 5 — Prefetch queue back-pressures the prefetcher
# ---------------------------------------------------------------------------
def test_prefetch_queue_backpressures_producer() -> None:
    """Queue capacity 1, prefetcher serves the same body indefinitely.
    After the queue fills, urlopen call count stalls; once the consumer
    drains one item, the prefetcher makes one more call."""
    cfg = _make_config(prefetch_queue_size=1)
    di = DataIngest(cfg)
    body = _frame_response([_trajectory_step(0)], trailer_status="ok")
    fake = _FakeResponse(body, status=200, headers={"X-Sts2-Q3-Trailer": "ok"})
    urlopen_calls = 0
    call_lock = threading.Lock()

    def fake_urlopen(*args: object, **kwargs: object) -> _FakeResponse:
        nonlocal urlopen_calls
        with call_lock:
            urlopen_calls += 1
        return fake

    stop_event = threading.Event()
    with mock.patch(
        "pipeline.trainer.data_ingest.urllib.request.urlopen",
        side_effect=fake_urlopen,
    ):
        di.start(stop_event)
        # Let the prefetcher run a few cycles. With queue=1 and no
        # consumer, after producing batches it must block on put().
        deadline = time.monotonic() + 2.0
        while time.monotonic() < deadline:
            with call_lock:
                if urlopen_calls >= 1:
                    break
            time.sleep(0.02)
        # Now the queue should be full (1 item) and the prefetcher
        # blocked. Wait until call count is stable.
        time.sleep(0.2)
        with call_lock:
            calls_while_full = urlopen_calls
        # Cap is 1 produced batch + at most one extra in-flight (the
        # one whose put() is currently blocked). For maxsize=1, the
        # produced batch is in the queue; the next iteration computes
        # a 2nd batch and is blocked on put. So calls_while_full
        # should be <= 2 (typically 2). Critically, it must NOT grow
        # unboundedly while no consumer drains.
        assert calls_while_full <= 2, (
            f"prefetcher did not back-pressure: {calls_while_full} calls"
        )
        # Drain one item; prefetcher should resume and make another call.
        batch = di.get_batch(timeout=2.0)
        assert batch is not None
        deadline = time.monotonic() + 2.0
        while time.monotonic() < deadline:
            with call_lock:
                if urlopen_calls > calls_while_full:
                    break
            time.sleep(0.02)
        with call_lock:
            final_calls = urlopen_calls
        assert final_calls > calls_while_full, (
            f"prefetcher did not resume after drain: "
            f"{final_calls} <= {calls_while_full}"
        )
        stop_event.set()
        di.join(timeout=2.0)


# ---------------------------------------------------------------------------
# Test 6 — Trajectory-ID accumulator + snapshot is a copy
# ---------------------------------------------------------------------------
def test_trajectory_id_accumulator_snapshot_is_copy() -> None:
    """Phase-1 stub: accumulator stays empty (see module docstring).
    Test asserts:
      (a) snapshot returns a tuple (not a list), so it cannot alias
          internal mutable state.
      (b) snapshot can be called many times without surprise — it is
          consistent with the (empty) Phase-1 contract.
    """
    cfg = _make_config(prefetch_queue_size=4)
    di = DataIngest(cfg)
    steps = [_trajectory_step(i) for i in range(2)]
    body = _frame_response(steps, trailer_status="ok")
    fake = _FakeResponse(body, status=200, headers={"X-Sts2-Q3-Trailer": "ok"})
    stop_event = threading.Event()
    with mock.patch(
        "pipeline.trainer.data_ingest.urllib.request.urlopen",
        return_value=fake,
    ):
        di.start(stop_event)
        # Drain at least one batch.
        b1 = di.get_batch(timeout=2.0)
        assert b1 is not None
        snap_a = di.snapshot_consumed_ids()
        snap_b = di.snapshot_consumed_ids()
        stop_event.set()
        di.join(timeout=2.0)
    assert isinstance(snap_a, tuple), "snapshot must return a tuple, not a list"
    assert isinstance(snap_b, tuple)
    assert snap_a == snap_b
    # Phase-1 stub: accumulator is empty.
    assert snap_a == tuple()
    # Confirm Batch.trajectory_ids is also a frozen tuple.
    assert isinstance(b1.trajectory_ids, tuple)


# ---------------------------------------------------------------------------
# Test 7 — Prioritized mode raises NotImplementedError on start
# ---------------------------------------------------------------------------
def test_prioritized_mode_raises_not_implemented_on_start() -> None:
    """sampling_mode='prioritized' is accepted at construct (it's in the
    valid set) but raises NotImplementedError when start() is called,
    citing ADR-020."""
    cfg = _make_config(sampling_mode="prioritized")
    di = DataIngest(cfg)
    stop_event = threading.Event()
    with pytest.raises(NotImplementedError, match="ADR-020"):
        di.start(stop_event)


# ---------------------------------------------------------------------------
# Test 8 — Unknown mode raises ValueError at construct
# ---------------------------------------------------------------------------
def test_unknown_mode_raises_value_error() -> None:
    """Unknown sampling_mode -> ValueError at construct."""
    cfg = _make_config(sampling_mode="bogus")
    with pytest.raises(ValueError, match="sampling_mode"):
        DataIngest(cfg)


# ---------------------------------------------------------------------------
# Additional coverage — stop_event honored, exhaustion -> None
# ---------------------------------------------------------------------------
def test_stop_event_honored_within_one_second() -> None:
    """Setting stop_event before start: prefetcher returns immediately;
    get_batch returns None promptly."""
    cfg = _make_config(prefetch_queue_size=4)
    di = DataIngest(cfg)
    stop_event = threading.Event()
    stop_event.set()
    with mock.patch(
        "pipeline.trainer.data_ingest.urllib.request.urlopen",
        side_effect=AssertionError("urlopen should not be called"),
    ):
        di.start(stop_event)
        t0 = time.monotonic()
        result = di.get_batch(timeout=1.0)
        elapsed = time.monotonic() - t0
        di.join(timeout=2.0)
    assert result is None
    assert elapsed < 1.0, f"get_batch took {elapsed:.3f}s on pre-set stop"


def test_exhaustion_returns_none_after_drain() -> None:
    """After Q3 returns ``exhausted``, get_batch yields the final batch,
    then subsequent calls return None promptly."""
    cfg = _make_config(prefetch_queue_size=4)
    di = DataIngest(cfg)
    steps = [_trajectory_step(i) for i in range(2)]
    body = _frame_response(steps, trailer_status="exhausted")
    fake = _FakeResponse(body, status=200, headers={"X-Sts2-Q3-Trailer": "exhausted"})
    stop_event = threading.Event()
    with mock.patch(
        "pipeline.trainer.data_ingest.urllib.request.urlopen",
        return_value=fake,
    ):
        di.start(stop_event)
        first = di.get_batch(timeout=2.0)
        # Subsequent call must return None (terminal exhaustion).
        second = di.get_batch(timeout=1.0)
        stop_event.set()
        di.join(timeout=2.0)
    assert first is not None
    assert first.cursor_token is None
    assert second is None

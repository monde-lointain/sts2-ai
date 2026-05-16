"""SamplingEngine unit tests — engine in isolation, no HTTP, no cursor cache.

These tests stub HotStore so the engine's filter algebra + scan loop +
frame emission can be exercised without a real RocksDB instance.
"""

from __future__ import annotations

from collections.abc import Iterator

from proto import DecisionType, Trajectory

from sampler.cursor import CursorState
from sampler.engine import SamplingEngine
from sampler.framing import decode_varint

# ---------- helpers ----------


def _make_traj(
    trajectory_id: str,
    *,
    n_steps: int = 2,
    model_version: str = "v1",
    generator: str = "rollout-worker",
    sampling_mode: str = "exploration",
    schema_major: int = 1,
    schema_minor: int = 0,
    decision_types: list[int] | None = None,
) -> bytes:
    traj = Trajectory()
    traj.schema_version.major = schema_major
    traj.schema_version.minor = schema_minor
    traj.trajectory_id = trajectory_id
    traj.episode_id = "ep-" + trajectory_id[-4:] if len(trajectory_id) >= 4 else "ep-0"
    traj.seed = 0xABCDEF
    traj.model_version = model_version
    traj.generator = generator
    traj.sampling_mode = sampling_mode
    if decision_types is None:
        decision_types = [DecisionType.DECISION_TYPE_CARD_PICK] * n_steps
    for i in range(n_steps):
        step = traj.steps.add()
        step.rich_state = f"state-{trajectory_id}-{i}".encode()
        step.action_taken = 2
        step.reward = 0.0
        step.terminal = i == n_steps - 1
        step.decision_type = decision_types[i % len(decision_types)]
    return traj.SerializeToString()


class _StubHotStore:
    """Minimal HotStore stand-in: yields rows strictly after `after_ts_ns`."""

    def __init__(self, rows: list[tuple[int, bytes, bytes]]) -> None:
        self._rows = rows

    def scan(self, after_ts_ns: int, limit: int) -> Iterator[tuple[int, bytes, bytes]]:
        yielded = 0
        for ts, tid, blob in self._rows:
            if ts <= after_ts_ns:
                continue
            if yielded >= limit:
                break
            yield ts, tid, blob
            yielded += 1


def _decode_step_frames(frames: list[bytes]) -> int:
    """Sanity-check: each frame is varint(len)||payload. Return frame count."""
    count = 0
    for frame in frames:
        length, n = decode_varint(frame, 0)
        # frame is exactly varint + payload; no trailer in engine output
        assert n + length == len(frame), "engine frame must be one length-delimited unit"
        count += 1
    return count


# ---------- tests ----------


def test_engine_emits_in_ts_order():
    """Engine yields trajectory steps in HotStore.scan order, capped by batch_max."""
    rows = [
        (10, b"a", _make_traj("t-a", n_steps=2)),
        (20, b"b", _make_traj("t-b", n_steps=2)),
        (30, b"c", _make_traj("t-c", n_steps=2)),
    ]
    eng = SamplingEngine(hot_store=_StubHotStore(rows), schema_registry=None)
    state = CursorState(mode="uniform")

    frames_iter, trailer, new_state = eng.sample(state, batch_max=2, filters={})
    frames = list(frames_iter)

    assert _decode_step_frames(frames) == len(frames)
    assert len(frames) <= 2
    assert new_state.served_count == state.served_count + len(frames)
    assert new_state.position_ts_ns >= state.position_ts_ns
    assert trailer in ("ok", "exhausted")


def test_engine_returns_exhausted_on_empty_store():
    """No rows => empty frames + trailer "exhausted"."""
    eng = SamplingEngine(hot_store=_StubHotStore([]), schema_registry=None)
    state = CursorState(mode="uniform")

    frames_iter, trailer, new_state = eng.sample(state, batch_max=10, filters={})
    frames = list(frames_iter)

    assert frames == []
    assert trailer == "exhausted"
    assert new_state.served_count == 0


def test_engine_filters_by_model_version():
    """Trajectories whose model_version is not in filter list are skipped."""
    rows = [
        (10, b"a", _make_traj("t-a", model_version="v1", n_steps=1)),
        (20, b"b", _make_traj("t-b", model_version="v2", n_steps=1)),
        (30, b"c", _make_traj("t-c", model_version="v1", n_steps=1)),
    ]
    eng = SamplingEngine(hot_store=_StubHotStore(rows), schema_registry=None)
    state = CursorState(mode="uniform", filters={"model_version": ["v1"]})

    frames_iter, trailer, _new_state = eng.sample(
        state, batch_max=10, filters={"model_version": ["v1"]}
    )
    frames = list(frames_iter)

    # Two v1 trajectories, one step each => 2 frames.
    assert len(frames) == 2
    # All rows consumed without hitting batch cap => "exhausted".
    assert trailer == "exhausted"


def test_engine_build_decision_type_filter_static():
    """Helper survives the move and accepts both names and ints."""
    spec = ["COMBAT", int(DecisionType.DECISION_TYPE_CARD_PICK)]
    out = SamplingEngine._build_decision_type_filter(spec)
    assert out is not None
    assert int(DecisionType.DECISION_TYPE_COMBAT) in out
    assert int(DecisionType.DECISION_TYPE_CARD_PICK) in out

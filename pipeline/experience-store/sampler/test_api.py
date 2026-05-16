"""End-to-end Sampler tests (S0.C.beta).

Covers the verification checklist from the dispatch prompt:

- Uniform sample of K returns <= K (10 traj x 5 steps -> 50 steps total).
- Filter by model_version excludes others.
- Filter by decision_type.
- Filter by schema_version.
- cold_only=true returns empty + exhausted.
- Cursor resume: first call returns N1 rows + cursor_id; second call with
  cursor_id returns next N2 rows (no overlap).
- Unknown cursor -> 404.
- LRU eviction tested in test_cursor.py.
- Idle cursor eviction tested in test_cursor.py.
- metrics_lines returns expected counter lines.
- End-to-end framing: parse length-delimited response, verify K (or
  fewer) TrajectoryStep messages + correct trailer.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from hot_store import HotStore
from proto import (
    DecisionType,
    Trajectory,
    TrajectoryStep,
)
from schema_registry import SchemaRegistry

from sampler import Sampler
from sampler.framing import decode_varint

# ---------- helpers ----------


def _make_trajectory(
    trajectory_id: str,
    *,
    n_steps: int = 5,
    model_version: str = "v1",
    generator: str = "rollout-worker",
    sampling_mode: str = "exploration",
    schema_major: int = 1,
    schema_minor: int = 0,
    decision_types: list[int] | None = None,
) -> Trajectory:
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
        step.legal_action_ids.extend([1, 2, 3])
        step.search_policy.extend([0.5, 0.3, 0.2])
        step.action_taken = 2
        step.reward = 0.0
        step.terminal = i == n_steps - 1
        step.decision_type = decision_types[i % len(decision_types)]
    return traj


def _seed(
    store: HotStore,
    n_trajectories: int = 10,
    steps_per_traj: int = 5,
    model_version_pattern: list[str] | None = None,
    schema_version: tuple[int, int] | None = None,
    decision_types_per_traj: list[list[int]] | None = None,
) -> list[bytes]:
    """Seed `store` with `n_trajectories` and return list of trajectory_ids."""
    ids: list[bytes] = []
    for i in range(n_trajectories):
        mv = (
            model_version_pattern[i % len(model_version_pattern)] if model_version_pattern else "v1"
        )
        sv = schema_version or (1, 0)
        dts = decision_types_per_traj[i] if decision_types_per_traj else None
        traj = _make_trajectory(
            f"traj-{i:04d}",
            n_steps=steps_per_traj,
            model_version=mv,
            schema_major=sv[0],
            schema_minor=sv[1],
            decision_types=dts,
        )
        tid = store.append_new(traj.SerializeToString())
        ids.append(tid)
    return ids


def _decode_stream(body: bytes) -> tuple[list[TrajectoryStep], dict]:
    """Parse the length-delimited response into (steps, trailer_dict)."""
    steps: list[TrajectoryStep] = []
    offset = 0
    trailer: dict | None = None
    while offset < len(body):
        length, n = decode_varint(body, offset)
        offset += n
        payload = body[offset : offset + length]
        offset += length
        # Attempt to decode as JSON trailer first; if not, treat as step.
        try:
            maybe = json.loads(payload.decode("utf-8"))
            if isinstance(maybe, dict) and "status" in maybe:
                trailer = maybe
                continue
        except (UnicodeDecodeError, json.JSONDecodeError):
            pass
        step = TrajectoryStep()
        step.ParseFromString(payload)
        steps.append(step)
    assert trailer is not None, "stream missing trailer frame"
    return steps, trailer


@pytest.fixture
def store(tmp_path: Path):
    db_dir = tmp_path / "hot"
    s = HotStore(db_dir)
    try:
        yield s
    finally:
        s.close()


@pytest.fixture
def registry(tmp_path: Path):
    return SchemaRegistry(tmp_path / "registry")


@pytest.fixture
def sampler(store, registry):
    return Sampler(hot_store=store, schema_registry=registry)


# ---------- batch_size + trailer ----------


def test_uniform_batch_le_total(sampler, store) -> None:
    _seed(store, n_trajectories=10, steps_per_traj=5)
    body = json.dumps({"mode": "uniform", "batch_size": 20}).encode()
    status, _headers, response = sampler.handle_post_sample(body)
    assert status == 200
    steps, trailer = _decode_stream(response)
    assert len(steps) == 20
    assert trailer == {"status": "ok"}


def test_uniform_total_lt_batch_returns_all_with_exhausted(sampler, store) -> None:
    _seed(store, n_trajectories=10, steps_per_traj=5)
    body = json.dumps({"mode": "uniform", "batch_size": 200}).encode()
    status, _headers, response = sampler.handle_post_sample(body)
    assert status == 200
    steps, trailer = _decode_stream(response)
    assert len(steps) == 50
    assert trailer == {"status": "exhausted"}


def test_uniform_empty_store_returns_exhausted(sampler) -> None:
    body = json.dumps({"mode": "uniform", "batch_size": 16}).encode()
    status, _h, response = sampler.handle_post_sample(body)
    assert status == 200
    steps, trailer = _decode_stream(response)
    assert steps == []
    assert trailer == {"status": "exhausted"}


# ---------- filters ----------


def test_filter_by_model_version_excludes_others(sampler, store) -> None:
    _seed(
        store,
        n_trajectories=9,
        steps_per_traj=4,
        model_version_pattern=["v1", "v2", "v3"],
    )
    body = json.dumps(
        {
            "mode": "uniform",
            "batch_size": 100,
            "filters": {"model_version": ["v2"]},
        }
    ).encode()
    status, _h, response = sampler.handle_post_sample(body)
    assert status == 200
    steps, trailer = _decode_stream(response)
    # 3 trajectories x 4 steps = 12 should match v2
    assert len(steps) == 12
    assert trailer == {"status": "exhausted"}


def test_filter_by_decision_type(sampler, store) -> None:
    # 3 trajectories; each has 4 steps; varied decision types across steps
    dts = [
        [
            DecisionType.DECISION_TYPE_COMBAT,
            DecisionType.DECISION_TYPE_MAP,
            DecisionType.DECISION_TYPE_COMBAT,
            DecisionType.DECISION_TYPE_SHOP,
        ]
    ] * 3
    _seed(store, n_trajectories=3, steps_per_traj=4, decision_types_per_traj=dts)
    body = json.dumps(
        {
            "mode": "uniform",
            "batch_size": 100,
            "filters": {"decision_type": ["COMBAT"]},
        }
    ).encode()
    _status, _h, response = sampler.handle_post_sample(body)
    steps, trailer = _decode_stream(response)
    assert len(steps) == 6  # 2 combat steps * 3 trajectories
    for step in steps:
        assert step.decision_type == DecisionType.DECISION_TYPE_COMBAT
    assert trailer == {"status": "exhausted"}


def test_filter_by_decision_type_canonical_name(sampler, store) -> None:
    """Both short ("COMBAT") and canonical ("DECISION_TYPE_COMBAT") accepted."""
    dts = [[DecisionType.DECISION_TYPE_COMBAT, DecisionType.DECISION_TYPE_MAP]] * 2
    _seed(store, n_trajectories=2, steps_per_traj=2, decision_types_per_traj=dts)
    body = json.dumps(
        {
            "mode": "uniform",
            "batch_size": 16,
            "filters": {"decision_type": ["DECISION_TYPE_COMBAT"]},
        }
    ).encode()
    _status, _h, response = sampler.handle_post_sample(body)
    steps, _ = _decode_stream(response)
    assert len(steps) == 2


def test_filter_by_schema_version(sampler, store) -> None:
    _seed(store, n_trajectories=5, steps_per_traj=3, schema_version=(1, 0))
    body = json.dumps(
        {
            "mode": "uniform",
            "batch_size": 100,
            "filters": {"schema_version": {"major": 1, "minor": 0}},
        }
    ).encode()
    _status, _h, response = sampler.handle_post_sample(body)
    steps, trailer = _decode_stream(response)
    assert len(steps) == 15
    assert trailer == {"status": "exhausted"}


def test_filter_schema_unknown_returns_400(sampler, store) -> None:
    """Filter with version not in SchemaRegistry.accepted -> 400 from gate."""
    _seed(store, n_trajectories=5)
    body = json.dumps(
        {
            "mode": "uniform",
            "batch_size": 10,
            "filters": {"schema_version": {"major": 99, "minor": 99}},
        }
    ).encode()
    status, _headers, response = sampler.handle_post_sample(body)
    assert status == 400
    payload = json.loads(response.decode("utf-8"))
    assert payload["error"] == "malformed"
    assert "schema rejected" in payload["detail"]


def test_filter_cold_only_returns_empty_exhausted(sampler, store) -> None:
    _seed(store, n_trajectories=5, steps_per_traj=3)
    body = json.dumps(
        {"mode": "uniform", "batch_size": 100, "filters": {"cold_only": True}}
    ).encode()
    status, _h, response = sampler.handle_post_sample(body)
    assert status == 200
    steps, trailer = _decode_stream(response)
    assert steps == []
    assert trailer == {"status": "exhausted"}


def test_filter_after_ts_ns_skips_old(sampler, store) -> None:
    # Seed; capture earliest ts via scan; supply after_ts_ns >= that ts.
    _seed(store, n_trajectories=4, steps_per_traj=2)
    rows = list(store.scan(after_ts_ns=0, limit=100))
    assert len(rows) == 4
    # Skip first trajectory by using ts of trajectory 0 as `after_ts_ns`.
    skip_ts = rows[0][0]
    body = json.dumps(
        {"mode": "uniform", "batch_size": 100, "filters": {"after_ts_ns": skip_ts}}
    ).encode()
    _status, _h, response = sampler.handle_post_sample(body)
    steps, _trailer = _decode_stream(response)
    # 3 remaining trajectories * 2 steps = 6
    assert len(steps) == 6


# ---------- cursor resume ----------


def test_cursor_resume_no_overlap(sampler, store) -> None:
    _seed(store, n_trajectories=10, steps_per_traj=5)
    # First call: batch_size 10 (= 2 trajectories worth).
    body = json.dumps({"mode": "uniform", "batch_size": 10}).encode()
    status, headers, response = sampler.handle_post_sample(body)
    assert status == 200
    first_steps, _ = _decode_stream(response)
    cursor_id = headers["X-Sts2-Q3-Cursor-Id"]
    assert len(cursor_id) == 32
    assert len(first_steps) == 10

    # Resume with cursor.
    body2 = json.dumps({"mode": "uniform", "batch_size": 10, "cursor_id": cursor_id}).encode()
    status, _headers2, response2 = sampler.handle_post_sample(body2)
    assert status == 200
    second_steps, _ = _decode_stream(response2)
    assert len(second_steps) == 10

    first_states = [s.rich_state for s in first_steps]
    second_states = [s.rich_state for s in second_steps]
    overlap = set(first_states) & set(second_states)
    assert overlap == set(), f"overlap detected: {overlap}"


def test_cursor_resume_exhausts(sampler, store) -> None:
    _seed(store, n_trajectories=4, steps_per_traj=5)  # 20 steps total
    cursor_id = None
    all_steps = []
    for _ in range(5):  # 5 iterations of batch=8 covers it
        payload = {"mode": "uniform", "batch_size": 8}
        if cursor_id:
            payload["cursor_id"] = cursor_id
        status, headers, response = sampler.handle_post_sample(json.dumps(payload).encode())
        assert status == 200
        steps, trailer = _decode_stream(response)
        all_steps.extend(steps)
        cursor_id = headers["X-Sts2-Q3-Cursor-Id"]
        if trailer == {"status": "exhausted"}:
            break
    assert len(all_steps) == 20


def test_cursor_resume_mid_trajectory_no_overlap(sampler, store) -> None:
    """Stop mid-trajectory; resume must include the rest of that traj only."""
    _seed(store, n_trajectories=3, steps_per_traj=5)  # 15 steps total
    # batch_size=3 stops mid-first-trajectory (yields 3 of 5).
    body = json.dumps({"mode": "uniform", "batch_size": 3}).encode()
    status, headers, response = sampler.handle_post_sample(body)
    assert status == 200
    first_steps, _ = _decode_stream(response)
    cursor_id = headers["X-Sts2-Q3-Cursor-Id"]
    assert len(first_steps) == 3

    # Resume; cumulative should reach 15 without overlap.
    all_first = [s.rich_state for s in first_steps]
    seen = list(all_first)
    while True:
        payload = {"mode": "uniform", "batch_size": 3, "cursor_id": cursor_id}
        status, headers, response = sampler.handle_post_sample(json.dumps(payload).encode())
        assert status == 200
        page_steps, trailer = _decode_stream(response)
        seen.extend(s.rich_state for s in page_steps)
        cursor_id = headers["X-Sts2-Q3-Cursor-Id"]
        if trailer == {"status": "exhausted"}:
            break
    assert len(seen) == 15
    assert len(set(seen)) == 15


def test_cursor_unknown_returns_404(sampler, store) -> None:
    _seed(store, n_trajectories=2)
    body = json.dumps(
        {
            "mode": "uniform",
            "batch_size": 4,
            "cursor_id": "deadbeef" * 4,  # 32 hex chars but never created
        }
    ).encode()
    status, _h, response = sampler.handle_post_sample(body)
    assert status == 404
    payload = json.loads(response.decode("utf-8"))
    assert payload["error"] == "cursor_not_found"
    assert payload["cursor_id"] == "deadbeef" * 4


# ---------- GET endpoints ----------


def test_get_sample_cursor_returns_state(sampler, store) -> None:
    _seed(store, n_trajectories=3, steps_per_traj=2)
    body = json.dumps({"mode": "uniform", "batch_size": 4}).encode()
    _, headers, _ = sampler.handle_post_sample(body)
    cursor_id = headers["X-Sts2-Q3-Cursor-Id"]

    status, headers, response = sampler.handle_get_sample_cursor(cursor_id)
    assert status == 200
    payload = json.loads(response.decode("utf-8"))
    assert payload["cursor_id"] == cursor_id
    assert payload["mode"] == "uniform"
    assert payload["served_count"] == 4


def test_get_sample_cursor_missing_returns_404(sampler) -> None:
    status, _h, response = sampler.handle_get_sample_cursor("nope")
    assert status == 404
    payload = json.loads(response.decode("utf-8"))
    assert payload["error"] == "cursor_not_found"


def test_get_sample_recent_uses_now_window(tmp_path: Path) -> None:
    """sample/recent uses now - 300s; verify by injecting a clock."""
    store = HotStore(tmp_path / "hot")
    try:
        # Seed some trajectories at real time.
        registry = SchemaRegistry(tmp_path / "reg")
        # Set clock far in the future so the recent-window cutoff
        # (now - 300s) is well past all seeded ts.
        clock_holder = {"ns": 0}

        def clock() -> int:
            return clock_holder["ns"]

        _seed(store, n_trajectories=3, steps_per_traj=2)
        # Advance the injected clock past the seeded data + the window.
        rows = list(store.scan(0, 100))
        latest_seed_ts = rows[-1][0]
        clock_holder["ns"] = latest_seed_ts + 10_000_000_000  # +10s past last seed

        s = Sampler(
            hot_store=store,
            schema_registry=registry,
            recent_window_seconds=1,  # tiny window
            now_ns=clock,
        )
        status, _h, response = s.handle_get_sample_recent(batch_size=64)
        assert status == 200
        steps, trailer = _decode_stream(response)
        # All seeded data is older than now-1s -> recent window starts after them.
        assert steps == []
        assert trailer == {"status": "exhausted"}
    finally:
        store.close()


def test_get_sample_recent_with_wide_window_returns_all(tmp_path: Path) -> None:
    store = HotStore(tmp_path / "hot")
    try:
        registry = SchemaRegistry(tmp_path / "reg")
        _seed(store, n_trajectories=2, steps_per_traj=3)
        s = Sampler(
            hot_store=store,
            schema_registry=registry,
            recent_window_seconds=3600,  # 1 hour back
        )
        status, _h, response = s.handle_get_sample_recent(batch_size=64)
        assert status == 200
        steps, _trailer = _decode_stream(response)
        assert len(steps) == 6
    finally:
        store.close()


# ---------- error paths ----------


def test_malformed_empty_body_returns_400(sampler) -> None:
    status, _h, response = sampler.handle_post_sample(b"")
    assert status == 400
    payload = json.loads(response.decode("utf-8"))
    assert payload["error"] == "malformed"


def test_malformed_non_json_returns_400(sampler) -> None:
    status, _h, response = sampler.handle_post_sample(b"not-json")
    assert status == 400
    payload = json.loads(response.decode("utf-8"))
    assert payload["error"] == "malformed"


def test_malformed_missing_mode_returns_400(sampler) -> None:
    body = json.dumps({"batch_size": 4}).encode()
    status, _h, _response = sampler.handle_post_sample(body)
    assert status == 400


def test_malformed_negative_batch_size_returns_400(sampler) -> None:
    body = json.dumps({"mode": "uniform", "batch_size": -1}).encode()
    status, _h, _response = sampler.handle_post_sample(body)
    assert status == 400


def test_unsupported_mode_prioritized_returns_400(sampler) -> None:
    body = json.dumps({"mode": "prioritized", "batch_size": 4}).encode()
    status, _h, response = sampler.handle_post_sample(body)
    assert status == 400
    payload = json.loads(response.decode("utf-8"))
    assert "uniform only" in payload["detail"]


def test_unsupported_mode_stratified_returns_400(sampler) -> None:
    body = json.dumps({"mode": "stratified", "batch_size": 4}).encode()
    status, _h, _response = sampler.handle_post_sample(body)
    assert status == 400


def test_malformed_filter_type_returns_400(sampler) -> None:
    body = json.dumps(
        {"mode": "uniform", "batch_size": 4, "filters": {"model_version": "v1"}}
    ).encode()
    status, _h, _response = sampler.handle_post_sample(body)
    assert status == 400


# ---------- metrics ----------


def test_metrics_lines_contains_expected_counters(sampler, store) -> None:
    _seed(store, n_trajectories=2, steps_per_traj=3)
    body = json.dumps({"mode": "uniform", "batch_size": 4}).encode()
    sampler.handle_post_sample(body)
    lines = sampler.metrics_lines("experience-store")
    text = b"\n".join(lines).decode("utf-8")
    assert 'sts2_q3_sample_request_total{mode="uniform",result="ok"' in text
    assert 'sts2_q3_sample_rows_returned_total{mode="uniform",tier="hot"' in text
    assert "sts2_q3_sample_cursor_count" in text
    assert "sts2_q3_sample_schema_503_total" in text


def test_metrics_503_emits_zero_in_phase1a(sampler) -> None:
    """schema_503 counter is emitted but stays at 0 in Phase-1A."""
    lines = sampler.metrics_lines("experience-store")
    text = b"\n".join(lines).decode("utf-8")
    assert 'sts2_q3_sample_schema_503_total{service="experience-store"} 0' in text


def test_metrics_cursor_count_reflects_lru(sampler, store) -> None:
    _seed(store, n_trajectories=3, steps_per_traj=2)
    body = json.dumps({"mode": "uniform", "batch_size": 2}).encode()
    sampler.handle_post_sample(body)
    sampler.handle_post_sample(body)
    lines = sampler.metrics_lines("experience-store")
    text = b"\n".join(lines).decode("utf-8")
    assert 'sts2_q3_sample_cursor_count{service="experience-store"} 2' in text


def test_metrics_rows_returned_counter(sampler, store) -> None:
    _seed(store, n_trajectories=4, steps_per_traj=3)  # 12 steps total
    body = json.dumps({"mode": "uniform", "batch_size": 12}).encode()
    sampler.handle_post_sample(body)
    lines = sampler.metrics_lines("experience-store")
    text = b"\n".join(lines).decode("utf-8")
    # exactly 12 steps were returned
    assert (
        'sts2_q3_sample_rows_returned_total{mode="uniform",tier="hot",service="experience-store"} 12'
        in text
    )


# ---------- end-to-end framing wire format ----------


def test_e2e_phase1_d1_trajectories_round_trip(sampler, store) -> None:
    """Populate HotStore with D1-shaped trajectories (degenerate combat),
    POST /sample, parse the wire-format response, verify each frame
    is a TrajectoryStep with degenerate combat sample populated.
    """
    # Build 5 trajectories; each has 4 steps, all COMBAT, with the
    # Phase-1 degenerate-single sample shape per Q3-ADR-005.
    for i in range(5):
        traj = Trajectory()
        traj.schema_version.major = 1
        traj.schema_version.minor = 0
        traj.trajectory_id = f"d1-{i}"
        traj.episode_id = f"ep-{i}"
        traj.model_version = "v1"
        traj.generator = "rollout"
        traj.sampling_mode = "exploration"
        for j in range(4):
            step = traj.steps.add()
            step.decision_type = DecisionType.DECISION_TYPE_COMBAT
            step.rich_state = f"state-{i}-{j}".encode()
            step.action_taken = j
            # Degenerate combat: one sample, weight=1.0, hp_delta == summary.expected_hp_delta
            step.combat_outcome_summary.expected_hp_delta = -3.5 - j
            sample = step.combat_outcome_samples.add()
            sample.probability_weight = 1.0
            sample.hp_delta = -3.5 - j
        store.append_new(traj.SerializeToString())

    body = json.dumps({"mode": "uniform", "batch_size": 20}).encode()
    status, _h, response = sampler.handle_post_sample(body)
    assert status == 200
    steps, _trailer = _decode_stream(response)
    assert len(steps) == 20
    for step in steps:
        assert step.decision_type == DecisionType.DECISION_TYPE_COMBAT
        assert len(step.combat_outcome_samples) == 1
        assert step.combat_outcome_samples[0].probability_weight == 1.0


def test_response_headers_carry_cursor_id(sampler, store) -> None:
    _seed(store, n_trajectories=2, steps_per_traj=2)
    body = json.dumps({"mode": "uniform", "batch_size": 2}).encode()
    status, headers, _ = sampler.handle_post_sample(body)
    assert status == 200
    assert "X-Sts2-Q3-Cursor-Id" in headers
    assert "X-Sts2-Q3-Trailer" in headers
    assert headers["X-Sts2-Q3-Trailer"] in ("ok", "exhausted")
    assert headers["Content-Type"] == "application/octet-stream"

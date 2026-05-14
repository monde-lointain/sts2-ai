"""IngestAPI unit + integration tests (S0.C.alpha).

Covers the test matrix listed in the dispatch prompt's "Verification"
block, plus the spec's "Testing Strategy" §Unit items.
"""

from __future__ import annotations

import json
import queue
import threading
from pathlib import Path
from typing import Any

import pytest

from control_plane.provenance import ProvenanceLog
from hot_store import HotStore
from ingest_api import IngestAPI
from ingest_api.framing import encode_frames
from proto import DecisionType, ObservabilityRegime, Trajectory
from schema_registry import SchemaRegistry


# --------------------------------- helpers ---------------------------------


def _make_trajectory(
    *,
    schema_major: int = 1,
    schema_minor: int = 0,
    model_version: str = "model-v1",
    sampling_mode: str = "uniform",
    generator: str = "test-gen",
    n_combat_steps: int = 1,
    sample_count: int = 1,
    decision_type: int = DecisionType.DECISION_TYPE_COMBAT,
    observability_regime: int = ObservabilityRegime.OBSERVABILITY_REGIME_POLICY_VISIBLE,
) -> Trajectory:
    """Build a D1-shaped Trajectory matching Q3-ADR-005 (degenerate sample)."""
    t = Trajectory()
    t.schema_version.major = schema_major
    t.schema_version.minor = schema_minor
    t.model_version = model_version
    t.sampling_mode = sampling_mode
    t.generator = generator
    for _ in range(n_combat_steps):
        step = t.steps.add()
        step.decision_type = decision_type
        step.observability_regime = observability_regime
        if decision_type == DecisionType.DECISION_TYPE_COMBAT:
            for _ in range(sample_count):
                s = step.combat_outcome_samples.add()
                s.probability_weight = 1.0
    return t


def _decode_body(body: bytes) -> dict[str, Any]:
    return json.loads(body.decode("utf-8"))


@pytest.fixture
def deps(tmp_path: Path):
    """Real HotStore + SchemaRegistry + ProvenanceLog on tmp_path."""
    hot = HotStore(tmp_path / "rocks")
    reg = SchemaRegistry(tmp_path)
    prov = ProvenanceLog(tmp_path)
    try:
        yield hot, reg, prov
    finally:
        hot.close()


@pytest.fixture
def api(deps):
    hot, reg, prov = deps
    return IngestAPI(hot, reg, prov)


# --------------------------- happy path: 202 -----------------------------


def test_post_trajectory_happy_path_returns_202(api: IngestAPI) -> None:
    body = _make_trajectory().SerializeToString()
    status, headers, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 202
    payload = _decode_body(resp_body)
    assert "trajectory_id" in payload
    assert len(payload["trajectory_id"]) == 32  # 16 bytes hex
    assert int(payload["ingest_ts_ns"]) > 0
    assert headers["Content-Type"] == "application/json"


def test_post_trajectory_writes_to_hot_store(deps, api: IngestAPI) -> None:
    hot, _reg, _prov = deps
    body = _make_trajectory().SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 202
    payload = _decode_body(resp_body)
    tid_hex = payload["trajectory_id"]
    tid_bytes = bytes.fromhex(tid_hex)
    stored = hot.read(tid_bytes)
    assert stored is not None
    assert stored == body


def test_post_trajectory_writes_provenance(deps, api: IngestAPI) -> None:
    _hot, _reg, prov = deps
    body = _make_trajectory(model_version="mv", sampling_mode="sm", generator="g").SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 202
    tid_hex = _decode_body(resp_body)["trajectory_id"]
    row = prov.lookup(tid_hex)
    assert row is not None
    assert row["model_version"] == "mv"
    assert row["sampling_mode"] == "sm"
    assert row["generator"] == "g"
    assert row["schema_major"] == 1
    assert row["schema_minor"] == 0


def test_post_trajectory_increments_accepted_counter(api: IngestAPI) -> None:
    body = _make_trajectory().SerializeToString()
    assert api.metrics_lines("svc")  # baseline emit OK
    for _ in range(3):
        status, _h, _b = api.handle_post_trajectories(
            body, "application/x-protobuf"
        )
        assert status == 202
    lines = api.metrics_lines("svc")
    accepted_line = next(
        l for l in lines if l.startswith(b"sts2_q3_ingest_accepted_total")
    )
    assert accepted_line.endswith(b"} 3")


def test_post_trajectory_accepts_content_type_with_charset(api: IngestAPI) -> None:
    body = _make_trajectory().SerializeToString()
    status, _h, _b = api.handle_post_trajectories(
        body, "application/x-protobuf; charset=binary"
    )
    assert status == 202


# --------------------------- content-type → 415 --------------------------


def test_wrong_content_type_returns_415(api: IngestAPI) -> None:
    body = _make_trajectory().SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(body, "application/json")
    assert status == 415
    payload = _decode_body(resp_body)
    assert payload["error"] == "content_type"
    assert payload["expected"] == "application/x-protobuf"


def test_empty_content_type_returns_415(api: IngestAPI) -> None:
    body = _make_trajectory().SerializeToString()
    status, _h, _b = api.handle_post_trajectories(body, "")
    assert status == 415


# ----------------------------- body too large → 413 -----------------------


def test_body_over_max_returns_413(deps) -> None:
    hot, reg, prov = deps
    api = IngestAPI(hot, reg, prov, max_body_bytes=128)
    body = b"\x00" * 200
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 413
    payload = _decode_body(resp_body)
    assert payload["error"] == "too_large"
    assert payload["max_bytes"] == 128


# -------------------------- schema_unknown → 400 -------------------------


def test_unknown_schema_returns_400(api: IngestAPI) -> None:
    body = _make_trajectory(schema_major=0, schema_minor=1).SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 400
    payload = _decode_body(resp_body)
    assert payload["error"] == "schema_unknown"
    # Diagnostic feedback: known-accepted versions returned to writer.
    assert payload["accepted"] == [{"major": 1, "minor": 0}]


def test_future_major_schema_returns_400(api: IngestAPI) -> None:
    body = _make_trajectory(schema_major=2, schema_minor=0).SerializeToString()
    status, _h, _b = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 400


# ------------------------- malformed parse → 400 -------------------------


def test_garbage_body_returns_400_malformed(api: IngestAPI) -> None:
    # Wire tag 0x0a expects a length-delimited field; 0xff is invalid varint.
    status, _h, resp_body = api.handle_post_trajectories(
        b"\x0a\xff\xff", "application/x-protobuf"
    )
    assert status == 400
    payload = _decode_body(resp_body)
    assert payload["error"] == "malformed"
    assert "parse" in payload["detail"]


# --------------------- trajectory-invariant violations → 400 -------------


def test_missing_model_version_returns_400(api: IngestAPI) -> None:
    body = _make_trajectory(model_version="").SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 400
    payload = _decode_body(resp_body)
    assert payload["error"] == "malformed"
    assert "model_version" in payload["detail"]


def test_missing_sampling_mode_returns_400(api: IngestAPI) -> None:
    body = _make_trajectory(sampling_mode="").SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 400
    assert "sampling_mode" in _decode_body(resp_body)["detail"]


def test_missing_generator_returns_400(api: IngestAPI) -> None:
    body = _make_trajectory(generator="").SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 400
    assert "generator" in _decode_body(resp_body)["detail"]


def test_decision_type_unspecified_returns_400(api: IngestAPI) -> None:
    body = _make_trajectory(
        decision_type=DecisionType.DECISION_TYPE_UNSPECIFIED,
    ).SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 400
    assert "decision_type" in _decode_body(resp_body)["detail"]


def test_observability_regime_unspecified_returns_400(api: IngestAPI) -> None:
    body = _make_trajectory(
        observability_regime=ObservabilityRegime.OBSERVABILITY_REGIME_UNSPECIFIED,
    ).SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 400
    assert "observability_regime" in _decode_body(resp_body)["detail"]


def test_combat_step_empty_samples_returns_400(api: IngestAPI) -> None:
    body = _make_trajectory(sample_count=0).SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 400
    detail = _decode_body(resp_body)["detail"]
    assert "combat_outcome_samples" in detail


def test_non_combat_step_does_not_require_samples(api: IngestAPI) -> None:
    # CARD_PICK step has no combat samples but should still 202.
    body = _make_trajectory(
        decision_type=DecisionType.DECISION_TYPE_CARD_PICK,
        sample_count=0,
    ).SerializeToString()
    status, _h, _b = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 202


# ------------------------------ queue → 503 -----------------------------


def test_queue_full_returns_503(deps) -> None:
    hot, reg, prov = deps
    # Tiny queue + manually fill so the next POST sees full.
    api = IngestAPI(hot, reg, prov, queue_capacity=1)
    # The handler enqueues a sentinel after accept; pre-fill the queue
    # to capacity directly so the next POST sees `full()` on entry.
    api._queue.put_nowait(("seed", 0))  # type: ignore[arg-type]

    body = _make_trajectory().SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 503
    payload = _decode_body(resp_body)
    assert payload["error"] == "queue_full"
    assert int(payload["retry_after_sec"]) > 0


def test_queue_full_increments_rejected_counter(deps) -> None:
    hot, reg, prov = deps
    api = IngestAPI(hot, reg, prov, queue_capacity=1)
    api._queue.put_nowait(("seed", 0))  # type: ignore[arg-type]

    body = _make_trajectory().SerializeToString()
    api.handle_post_trajectories(body, "application/x-protobuf")
    lines = api.metrics_lines("svc")
    found = any(
        b'sts2_q3_ingest_rejected_total{reason="queue_full"' in l for l in lines
    )
    assert found


# ---------------------------- GET /ingest/status -------------------------


def test_get_ingest_status_shape(api: IngestAPI) -> None:
    status, headers, resp_body = api.handle_get_ingest_status()
    assert status == 200
    assert headers["Content-Type"] == "application/json"
    payload = _decode_body(resp_body)
    assert set(payload.keys()) == {
        "queue_depth",
        "queue_capacity",
        "accepted_total",
        "rejected_total",
        "schema_drain_state",
    }
    assert payload["queue_depth"] == 0
    assert payload["queue_capacity"] == 4096
    assert payload["accepted_total"] == 0
    assert payload["rejected_total"] == 0
    assert payload["schema_drain_state"] == "open"


def test_get_ingest_status_reflects_writes(api: IngestAPI) -> None:
    body = _make_trajectory().SerializeToString()
    for _ in range(5):
        api.handle_post_trajectories(body, "application/x-protobuf")
    _status, _h, resp_body = api.handle_get_ingest_status()
    payload = _decode_body(resp_body)
    assert payload["accepted_total"] == 5
    assert payload["queue_depth"] == 5  # consumer not running in this test


# ----------------------- POST /trajectories:batch ------------------------


def test_batch_endpoint_three_valid_frames(api: IngestAPI) -> None:
    payloads = [_make_trajectory().SerializeToString() for _ in range(3)]
    body = encode_frames(payloads)
    status, _h, resp_body = api.handle_post_trajectories_batch(
        body, "application/x-protobuf"
    )
    assert status == 207
    payload = _decode_body(resp_body)
    frames = payload["frames"]
    assert len(frames) == 3
    for f in frames:
        assert f["status"] == 202
        assert "trajectory_id" in f
        assert "ingest_ts_ns" in f


def test_batch_endpoint_mixed_valid_and_invalid(api: IngestAPI) -> None:
    good = _make_trajectory().SerializeToString()
    bad_traj = _make_trajectory(model_version="").SerializeToString()  # invariant fail
    bad_unknown_schema = _make_trajectory(schema_major=0, schema_minor=1).SerializeToString()
    body = encode_frames([good, bad_traj, good, bad_unknown_schema])

    status, _h, resp_body = api.handle_post_trajectories_batch(
        body, "application/x-protobuf"
    )
    assert status == 207
    frames = _decode_body(resp_body)["frames"]
    assert [f["status"] for f in frames] == [202, 400, 202, 400]
    assert frames[1]["error"] == "malformed"
    assert frames[3]["error"] == "schema_unknown"


def test_batch_endpoint_framing_error_returns_400(api: IngestAPI) -> None:
    # Declares varint length 99 but only ~3 bytes follow.
    body = b"\x63" + b"abc"
    status, _h, resp_body = api.handle_post_trajectories_batch(
        body, "application/x-protobuf"
    )
    assert status == 400
    payload = _decode_body(resp_body)
    assert payload["error"] == "malformed"
    assert "framing" in payload["detail"]


def test_batch_endpoint_wrong_content_type_returns_415(api: IngestAPI) -> None:
    body = encode_frames([_make_trajectory().SerializeToString()])
    status, _h, _b = api.handle_post_trajectories_batch(body, "application/json")
    assert status == 415


def test_batch_endpoint_oversized_body_returns_413(deps) -> None:
    hot, reg, prov = deps
    api = IngestAPI(hot, reg, prov, max_body_bytes=64)
    body = encode_frames([_make_trajectory().SerializeToString()])  # ~32 bytes once framed
    # Pad body to overflow.
    if len(body) <= 64:
        body = body + b"\x00" * (65 - len(body))
    status, _h, _b = api.handle_post_trajectories_batch(
        body, "application/x-protobuf"
    )
    assert status == 413


# ----------------------------- end-to-end --------------------------------


def test_end_to_end_100_posts_all_land_in_hot_store(deps, api: IngestAPI) -> None:
    hot, _reg, prov = deps
    ids: list[str] = []
    for _ in range(100):
        body = _make_trajectory().SerializeToString()
        status, _h, resp_body = api.handle_post_trajectories(
            body, "application/x-protobuf"
        )
        assert status == 202
        ids.append(_decode_body(resp_body)["trajectory_id"])

    assert hot.count() == 100
    # Provenance log has 100 distinct rows.
    rows = prov.query_recent(200)
    assert len(rows) == 100
    seen = {r["trajectory_id"] for r in rows}
    assert seen == set(ids)

    # Range-scan: ingest_ts_ns is monotonic.
    scanned: list[int] = []
    for ts_ns, _tid, _v in hot.scan(after_ts_ns=0, limit=1000):
        scanned.append(ts_ns)
    assert len(scanned) == 100
    assert scanned == sorted(scanned)


# ------------------------------ consumer ---------------------------------


def test_consumer_drains_queue(deps, api: IngestAPI) -> None:
    body = _make_trajectory().SerializeToString()
    for _ in range(10):
        status, _h, _b = api.handle_post_trajectories(
            body, "application/x-protobuf"
        )
        assert status == 202
    assert api.queue_depth() == 10

    stop = threading.Event()
    t = threading.Thread(target=api.consumer_loop, args=(stop,), daemon=True)
    t.start()
    # Wait for drain — consumer pulls one at a time.
    import time

    deadline = time.monotonic() + 5.0
    while api.queue_depth() > 0 and time.monotonic() < deadline:
        time.sleep(0.05)
    stop.set()
    t.join(timeout=2.0)
    assert api.queue_depth() == 0


def test_consumer_loop_stops_on_event(api: IngestAPI) -> None:
    stop = threading.Event()
    t = threading.Thread(target=api.consumer_loop, args=(stop,), daemon=True)
    t.start()
    stop.set()
    t.join(timeout=2.0)
    assert not t.is_alive()


# ------------------------------ metrics ----------------------------------


def test_metrics_lines_emits_required_set(api: IngestAPI) -> None:
    # Trigger a mix of accepts + rejects so multiple lines emit.
    body = _make_trajectory().SerializeToString()
    api.handle_post_trajectories(body, "application/x-protobuf")  # 202
    api.handle_post_trajectories(body, "application/json")  # 415 -> content_type
    api.handle_post_trajectories(
        _make_trajectory(model_version="").SerializeToString(),
        "application/x-protobuf",
    )  # 400 -> malformed
    api.handle_post_trajectories(
        _make_trajectory(schema_major=2).SerializeToString(),
        "application/x-protobuf",
    )  # 400 -> schema_unknown

    lines_text = b"\n".join(api.metrics_lines("experience-store")).decode("utf-8")
    assert 'sts2_q3_ingest_accepted_total{service="experience-store"} 1' in lines_text
    assert 'sts2_q3_ingest_rejected_total{reason="content_type"' in lines_text
    assert 'sts2_q3_ingest_rejected_total{reason="malformed"' in lines_text
    assert 'sts2_q3_ingest_rejected_total{reason="schema_unknown"' in lines_text
    assert 'sts2_q3_ingest_queue_depth{service="experience-store"}' in lines_text
    assert 'sts2_q3_ingest_bytes_total{service="experience-store"}' in lines_text


def test_metrics_lines_omits_zero_rejection_reasons(api: IngestAPI) -> None:
    # Fresh API: no rejections yet → no rejected_total lines emitted.
    lines = api.metrics_lines("svc")
    assert not any(b"sts2_q3_ingest_rejected_total" in l for l in lines)


def test_metrics_lines_bytes_total_tracks_accepted_bodies(api: IngestAPI) -> None:
    body = _make_trajectory().SerializeToString()
    api.handle_post_trajectories(body, "application/x-protobuf")
    api.handle_post_trajectories(body, "application/x-protobuf")
    lines_text = b"\n".join(api.metrics_lines("svc")).decode("utf-8")
    # Two accepts of equal body size → 2 * len(body).
    import re

    m = re.search(r'sts2_q3_ingest_bytes_total\{service="svc"\} (\d+)', lines_text)
    assert m
    assert int(m.group(1)) == 2 * len(body)


# ---------- provenance-append failure surfaces as 500 (spec line 117) ----


def test_provenance_append_failure_returns_500(deps, monkeypatch) -> None:
    hot, reg, prov = deps
    api = IngestAPI(hot, reg, prov)

    def boom(*_args, **_kwargs):
        raise RuntimeError("simulated provenance outage")

    monkeypatch.setattr(prov, "append", boom)
    body = _make_trajectory().SerializeToString()
    status, _h, resp_body = api.handle_post_trajectories(
        body, "application/x-protobuf"
    )
    assert status == 500
    payload = _decode_body(resp_body)
    assert payload["error"] == "provenance_unavailable"


# ---------- construction guards ----------


def test_zero_queue_capacity_raises(deps) -> None:
    hot, reg, prov = deps
    with pytest.raises(ValueError):
        IngestAPI(hot, reg, prov, queue_capacity=0)


def test_zero_max_body_raises(deps) -> None:
    hot, reg, prov = deps
    with pytest.raises(ValueError):
        IngestAPI(hot, reg, prov, max_body_bytes=0)

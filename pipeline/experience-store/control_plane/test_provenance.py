"""Unit tests for ProvenanceLog (S0.B.gamma)."""

from __future__ import annotations

import json

import pytest

from control_plane.provenance import PROVENANCE_FILE, ProvenanceLog


def test_record_creates_file_and_appends_one_line_per_call(tmp_path):
    log = ProvenanceLog(tmp_path)
    assert (tmp_path / PROVENANCE_FILE).exists()

    log.record(
        ingest_ts_ns=1,
        trajectory_id="t0",
        model_version="v3",
        sampling_mode="uniform",
        generator="rollout_worker",
    )
    log.record(
        ingest_ts_ns=2,
        trajectory_id="t1",
        model_version="v3",
        sampling_mode="uniform",
        generator="rollout_worker",
    )

    lines = (tmp_path / PROVENANCE_FILE).read_text(encoding="utf-8").splitlines()
    assert len(lines) == 2
    assert json.loads(lines[0])["trajectory_id"] == "t0"
    assert json.loads(lines[1])["trajectory_id"] == "t1"


def test_query_recent_returns_last_n_in_append_order(tmp_path):
    log = ProvenanceLog(tmp_path)
    for i in range(10):
        log.record(
            ingest_ts_ns=i,
            trajectory_id=f"traj-{i}",
            model_version=f"v{i % 3}",
            sampling_mode="uniform" if i % 2 == 0 else "prioritized",
            generator="rollout_worker" if i < 5 else "curriculum",
        )

    last_five = log.query_recent(5)
    assert [r["trajectory_id"] for r in last_five] == [
        "traj-5",
        "traj-6",
        "traj-7",
        "traj-8",
        "traj-9",
    ]
    # Provenance values round-trip.
    assert last_five[0]["generator"] == "curriculum"
    assert last_five[0]["sampling_mode"] == "prioritized"
    assert last_five[-1]["model_version"] == "v0"


def test_query_recent_on_empty_log_returns_empty(tmp_path):
    log = ProvenanceLog(tmp_path)
    assert log.query_recent(5) == []
    assert log.query_recent(0) == []


def test_query_recent_zero_or_negative_returns_empty(tmp_path):
    log = ProvenanceLog(tmp_path)
    log.record(1, "t", "v1", "uniform", "gen")
    assert log.query_recent(0) == []
    assert log.query_recent(-1) == []


def test_query_recent_more_than_available_returns_all(tmp_path):
    log = ProvenanceLog(tmp_path)
    log.record(1, "t0", "v1", "uniform", "gen")
    log.record(2, "t1", "v1", "uniform", "gen")
    out = log.query_recent(100)
    assert len(out) == 2
    assert [r["trajectory_id"] for r in out] == ["t0", "t1"]


def test_empty_model_version_raises(tmp_path):
    log = ProvenanceLog(tmp_path)
    with pytest.raises(ValueError, match="model_version"):
        log.record(1, "t", "", "uniform", "gen")


def test_empty_sampling_mode_raises(tmp_path):
    log = ProvenanceLog(tmp_path)
    with pytest.raises(ValueError, match="sampling_mode"):
        log.record(1, "t", "v1", "", "gen")


def test_empty_generator_raises(tmp_path):
    log = ProvenanceLog(tmp_path)
    with pytest.raises(ValueError, match="generator"):
        log.record(1, "t", "v1", "uniform", "")


def test_rejected_record_does_not_pollute_log(tmp_path):
    log = ProvenanceLog(tmp_path)
    log.record(1, "ok", "v1", "uniform", "gen")
    with pytest.raises(ValueError):
        log.record(2, "bad", "", "uniform", "gen")
    out = log.query_recent(10)
    assert len(out) == 1
    assert out[0]["trajectory_id"] == "ok"


def test_record_persists_across_instances(tmp_path):
    log_a = ProvenanceLog(tmp_path)
    log_a.record(1, "t0", "v1", "uniform", "gen")
    log_b = ProvenanceLog(tmp_path)
    log_b.record(2, "t1", "v1", "uniform", "gen")

    out = log_b.query_recent(10)
    assert [r["trajectory_id"] for r in out] == ["t0", "t1"]

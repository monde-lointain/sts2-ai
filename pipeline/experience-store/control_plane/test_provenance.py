"""Unit tests for ProvenanceLog (S0.B.gamma + S0.B.gamma' spec alignment)."""

from __future__ import annotations

import json

import pytest

from control_plane.provenance import PROVENANCE_FILE, ProvenanceLog


def test_append_creates_file_and_appends_one_line_per_call(tmp_path):
    log = ProvenanceLog(tmp_path)
    assert (tmp_path / PROVENANCE_FILE).exists()

    log.append(
        trajectory_id="t0",
        model_version="v3",
        sampling_mode="uniform",
        generator="rollout_worker",
        ingest_ts_ns=1,
        schema_version=(1, 0),
    )
    log.append(
        trajectory_id="t1",
        model_version="v3",
        sampling_mode="uniform",
        generator="rollout_worker",
        ingest_ts_ns=2,
        schema_version=(1, 0),
    )

    lines = (tmp_path / PROVENANCE_FILE).read_text(encoding="utf-8").splitlines()
    assert len(lines) == 2
    assert json.loads(lines[0])["trajectory_id"] == "t0"
    assert json.loads(lines[1])["trajectory_id"] == "t1"


def test_append_serializes_schema_version_as_flat_fields(tmp_path):
    """Spec lines 38-42 mandate schema_major + schema_minor as flat fields."""
    log = ProvenanceLog(tmp_path)
    log.append(
        trajectory_id="tx",
        model_version="v3",
        sampling_mode="uniform",
        generator="gen",
        ingest_ts_ns=42,
        schema_version=(1, 0),
    )
    row = json.loads(
        (tmp_path / PROVENANCE_FILE).read_text(encoding="utf-8").splitlines()[0]
    )
    assert row["schema_major"] == 1
    assert row["schema_minor"] == 0
    # No nested object form.
    assert "schema_version" not in row


def test_append_schema_version_propagates_minor(tmp_path):
    log = ProvenanceLog(tmp_path)
    log.append(
        trajectory_id="tx",
        model_version="v3",
        sampling_mode="uniform",
        generator="gen",
        ingest_ts_ns=42,
        schema_version=(2, 5),
    )
    row = json.loads(
        (tmp_path / PROVENANCE_FILE).read_text(encoding="utf-8").splitlines()[0]
    )
    assert row["schema_major"] == 2
    assert row["schema_minor"] == 5


def test_query_recent_returns_last_n_in_append_order(tmp_path):
    log = ProvenanceLog(tmp_path)
    for i in range(10):
        log.append(
            trajectory_id=f"traj-{i}",
            model_version=f"v{i % 3}",
            sampling_mode="uniform" if i % 2 == 0 else "prioritized",
            generator="rollout_worker" if i < 5 else "curriculum",
            ingest_ts_ns=i,
            schema_version=(1, 0),
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
    log.append(
        trajectory_id="t",
        model_version="v1",
        sampling_mode="uniform",
        generator="gen",
        ingest_ts_ns=1,
        schema_version=(1, 0),
    )
    assert log.query_recent(0) == []
    assert log.query_recent(-1) == []


def test_query_recent_more_than_available_returns_all(tmp_path):
    log = ProvenanceLog(tmp_path)
    log.append(
        trajectory_id="t0",
        model_version="v1",
        sampling_mode="uniform",
        generator="gen",
        ingest_ts_ns=1,
        schema_version=(1, 0),
    )
    log.append(
        trajectory_id="t1",
        model_version="v1",
        sampling_mode="uniform",
        generator="gen",
        ingest_ts_ns=2,
        schema_version=(1, 0),
    )
    out = log.query_recent(100)
    assert len(out) == 2
    assert [r["trajectory_id"] for r in out] == ["t0", "t1"]


def test_empty_model_version_raises(tmp_path):
    log = ProvenanceLog(tmp_path)
    with pytest.raises(ValueError, match="model_version"):
        log.append(
            trajectory_id="t",
            model_version="",
            sampling_mode="uniform",
            generator="gen",
            ingest_ts_ns=1,
            schema_version=(1, 0),
        )


def test_empty_sampling_mode_raises(tmp_path):
    log = ProvenanceLog(tmp_path)
    with pytest.raises(ValueError, match="sampling_mode"):
        log.append(
            trajectory_id="t",
            model_version="v1",
            sampling_mode="",
            generator="gen",
            ingest_ts_ns=1,
            schema_version=(1, 0),
        )


def test_empty_generator_raises(tmp_path):
    log = ProvenanceLog(tmp_path)
    with pytest.raises(ValueError, match="generator"):
        log.append(
            trajectory_id="t",
            model_version="v1",
            sampling_mode="uniform",
            generator="",
            ingest_ts_ns=1,
            schema_version=(1, 0),
        )


def test_rejected_record_does_not_pollute_log(tmp_path):
    log = ProvenanceLog(tmp_path)
    log.append(
        trajectory_id="ok",
        model_version="v1",
        sampling_mode="uniform",
        generator="gen",
        ingest_ts_ns=1,
        schema_version=(1, 0),
    )
    with pytest.raises(ValueError):
        log.append(
            trajectory_id="bad",
            model_version="",
            sampling_mode="uniform",
            generator="gen",
            ingest_ts_ns=2,
            schema_version=(1, 0),
        )
    out = log.query_recent(10)
    assert len(out) == 1
    assert out[0]["trajectory_id"] == "ok"


def test_record_persists_across_instances(tmp_path):
    log_a = ProvenanceLog(tmp_path)
    log_a.append(
        trajectory_id="t0",
        model_version="v1",
        sampling_mode="uniform",
        generator="gen",
        ingest_ts_ns=1,
        schema_version=(1, 0),
    )
    log_b = ProvenanceLog(tmp_path)
    log_b.append(
        trajectory_id="t1",
        model_version="v1",
        sampling_mode="uniform",
        generator="gen",
        ingest_ts_ns=2,
        schema_version=(1, 0),
    )

    out = log_b.query_recent(10)
    assert [r["trajectory_id"] for r in out] == ["t0", "t1"]


def test_lookup_returns_dict_for_known_id(tmp_path):
    log = ProvenanceLog(tmp_path)
    log.append(
        trajectory_id="traj-known",
        model_version="v3",
        sampling_mode="uniform",
        generator="rollout_worker",
        ingest_ts_ns=42,
        schema_version=(1, 0),
    )
    log.append(
        trajectory_id="traj-other",
        model_version="v3",
        sampling_mode="uniform",
        generator="rollout_worker",
        ingest_ts_ns=43,
        schema_version=(1, 0),
    )

    row = log.lookup("traj-known")
    assert row is not None
    assert row["trajectory_id"] == "traj-known"
    assert row["model_version"] == "v3"
    assert row["schema_major"] == 1
    assert row["ingest_ts_ns"] == 42


def test_lookup_returns_none_for_unknown_id(tmp_path):
    log = ProvenanceLog(tmp_path)
    log.append(
        trajectory_id="traj-known",
        model_version="v3",
        sampling_mode="uniform",
        generator="rollout_worker",
        ingest_ts_ns=42,
        schema_version=(1, 0),
    )
    assert log.lookup("traj-missing") is None


def test_lookup_on_empty_log_returns_none(tmp_path):
    log = ProvenanceLog(tmp_path)
    assert log.lookup("anything") is None

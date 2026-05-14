"""Unit tests for AuditLog (S0.C.gamma)."""

from __future__ import annotations

import json

import pytest

from lifecycle.audit import AUDIT_FILE, AuditLog


def test_append_creates_file_and_appends_one_line_per_call(tmp_path):
    log = AuditLog(tmp_path)
    assert log.path.exists()
    log.append({"ts_ns": 1, "action": "drop", "rows": 5})
    log.append({"ts_ns": 2, "action": "drop", "rows": 6})
    lines = log.path.read_text(encoding="utf-8").splitlines()
    assert len(lines) == 2
    assert json.loads(lines[0])["ts_ns"] == 1
    assert json.loads(lines[1])["ts_ns"] == 2


def test_append_stores_full_record_shape(tmp_path):
    """Spec lines 44-49: action / until_ts_ns / rows / bytes / reason."""
    log = AuditLog(tmp_path)
    record = {
        "ts_ns": 100,
        "action": "drop",
        "until_ts_ns": 50,
        "rows": 10,
        "bytes": 1024,
        "reason": "overflow",
    }
    log.append(record)
    row = json.loads(log.path.read_text(encoding="utf-8").splitlines()[0])
    assert row == record


def test_rotation_at_threshold(tmp_path):
    """Spec line 49: rotate at 10 MiB. Verified with a small synthetic threshold."""
    log = AuditLog(tmp_path, rotate_at_bytes=512)
    # Each record is well under 512 bytes; force several to push past.
    for i in range(20):
        log.append({"ts_ns": i, "action": "drop", "filler": "x" * 64})
    rotated = log.rotated_files()
    assert len(rotated) >= 1
    # Live file size is below threshold after rotation.
    assert log.path.stat().st_size < 512


def test_rotation_retains_at_most_max_rotated(tmp_path):
    log = AuditLog(tmp_path, rotate_at_bytes=128, max_rotated_files=3)
    for i in range(40):
        log.append({"ts_ns": i, "filler": "x" * 64})
    rotated = log.rotated_files()
    assert len(rotated) <= 3


def test_rotation_synthetic_under_10_mib_threshold(tmp_path):
    """Default 10 MiB rotation: force many entries with large payloads."""
    log = AuditLog(tmp_path)  # default 10 MiB
    big_payload = "x" * 10_240  # 10 KiB filler per record
    # ~1100 records => ~11 MiB; rotation must fire at least once.
    for i in range(1100):
        log.append(
            {
                "ts_ns": i,
                "action": "drop",
                "rows": 1,
                "bytes": 1,
                "reason": "overflow",
                "filler": big_payload,
            }
        )
    rotated = log.rotated_files()
    assert len(rotated) >= 1
    assert log.path.stat().st_size < 10 * 1024 * 1024


def test_read_all_round_trip(tmp_path):
    log = AuditLog(tmp_path)
    log.append({"ts_ns": 1, "action": "drop"})
    log.append({"ts_ns": 2, "action": "policy_update"})
    rows = log.read_all()
    assert len(rows) == 2
    assert [r["ts_ns"] for r in rows] == [1, 2]


def test_constructor_rejects_zero_rotate_threshold(tmp_path):
    with pytest.raises(ValueError):
        AuditLog(tmp_path, rotate_at_bytes=0)


def test_constructor_rejects_zero_max_rotated(tmp_path):
    with pytest.raises(ValueError):
        AuditLog(tmp_path, max_rotated_files=0)


def test_reinit_on_existing_dir_preserves_audit(tmp_path):
    """Spec: reinit must not clobber existing audit log."""
    log = AuditLog(tmp_path)
    log.append({"ts_ns": 1, "action": "drop"})
    log2 = AuditLog(tmp_path)
    assert log2.read_all() == [{"ts_ns": 1, "action": "drop"}]


def test_audit_filename_is_canonical(tmp_path):
    log = AuditLog(tmp_path)
    assert log.path.name == AUDIT_FILE

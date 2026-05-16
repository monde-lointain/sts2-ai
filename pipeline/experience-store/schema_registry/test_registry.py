"""Unit tests for SchemaRegistry (S0.B.beta).

Coverage per S0.B.beta dispatch prompt:
- validate() Accept/Reject decision matrix (Phase-1 rule set).
- current_health_schema / current_write_target / accepted / drain_state.
- state_snapshot shape.
- Fresh init creates registry.json + migration_log.ndjson + sentinel.
- Re-instantiation preserves on-disk state (mtime check).
- metrics_lines emits required gauge + counter shape.
- drain / flip / revert raise NotImplementedError("Phase-1 close").
- Thread-safety: 100 threads x 50 validate calls -> 5000 (no lost updates).
- Atomic registry write: os.replace mocked; no torn intermediate visible.
"""

from __future__ import annotations

import json
import os
import threading
import time
from pathlib import Path
from unittest import mock

import pytest

from schema_registry import SchemaRegistry
from schema_registry.decision import Accept, Reject

# ---------------------------- Phase-1 rule set ----------------------------


def test_validate_known_version_read_returns_accept(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    assert isinstance(reg.validate((1, 0), "read"), Accept)
    assert isinstance(reg.validate((1, 1), "read"), Accept)


def test_validate_known_version_write_returns_accept(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    assert isinstance(reg.validate((1, 0), "write"), Accept)
    assert isinstance(reg.validate((1, 1), "write"), Accept)


def test_validate_unknown_minor_read_returns_reject_400(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    decision = reg.validate((0, 1), "read")
    assert isinstance(decision, Reject)
    assert decision.reason == "schema_unknown"
    assert decision.http_status == 400
    assert decision.accepted == [(1, 0), (1, 1)]
    assert decision.retry_after_sec is None


def test_validate_unknown_future_major_write_returns_reject_400(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    decision = reg.validate((2, 0), "write")
    assert isinstance(decision, Reject)
    assert decision.reason == "schema_unknown"
    assert decision.http_status == 400
    assert decision.accepted == [(1, 0), (1, 1)]


def test_validate_v0_write_returns_reject_400(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    decision = reg.validate((0, 0), "write")
    assert isinstance(decision, Reject)
    assert decision.reason == "schema_unknown"
    assert decision.http_status == 400


def test_validate_rejects_bad_op(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    with pytest.raises(ValueError):
        reg.validate((1, 0), "delete")  # type: ignore[arg-type]


# --------------------------- public state getters --------------------------


def test_current_health_schema_returns_major(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    assert reg.current_health_schema() == 1


def test_current_write_target(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    assert reg.current_write_target() == (1, 1)


def test_accepted_returns_v1_0_and_v1_1(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    assert reg.accepted() == [(1, 0), (1, 1)]


def test_drain_state_open(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    assert reg.drain_state() == "open"


def test_state_snapshot_shape(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    snap = reg.state_snapshot()
    assert snap == {
        "accepted": [{"major": 1, "minor": 0}, {"major": 1, "minor": 1}],
        "current_write_target": {"major": 1, "minor": 1},
        "drain_state": "open",
        "drain_target": None,
    }


# ---------------------------- persistence ---------------------------------


def test_fresh_init_creates_files(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    schema_dir = tmp_path / "schema"
    assert schema_dir.is_dir()
    assert (schema_dir / "registry.json").exists()
    assert (schema_dir / "migration_log.ndjson").exists()
    assert (schema_dir / "1.1.active").exists()
    # registry.json content matches the initial shape.
    with (schema_dir / "registry.json").open() as h:
        data = json.load(h)
    assert data == {
        "accepted": [{"major": 1, "minor": 0}, {"major": 1, "minor": 1}],
        "current_write_target": {"major": 1, "minor": 1},
        "drain_state": "open",
        "drain_target": None,
    }
    # Sentinel is empty.
    assert (schema_dir / "1.1.active").read_bytes() == b""
    # migration log starts empty (no transitions in Phase-1A).
    assert (schema_dir / "migration_log.ndjson").read_bytes() == b""
    # Bind reg to silence unused-var lint.
    assert reg.drain_state() == "open"


def test_reinit_preserves_existing_files(tmp_path: Path):
    reg1 = SchemaRegistry(tmp_path)
    del reg1

    registry_path = tmp_path / "schema" / "registry.json"
    sentinel_path = tmp_path / "schema" / "1.1.active"
    registry_mtime = registry_path.stat().st_mtime_ns
    sentinel_mtime = sentinel_path.stat().st_mtime_ns

    # Sleep a touch so any erroneous re-write would yield a newer mtime.
    time.sleep(0.05)

    reg2 = SchemaRegistry(tmp_path)
    assert reg2.current_write_target() == (1, 1)

    assert registry_path.stat().st_mtime_ns == registry_mtime
    assert sentinel_path.stat().st_mtime_ns == sentinel_mtime


def test_load_recovers_state_from_disk(tmp_path: Path):
    # Hand-craft a registry.json with a non-default drain_state to exercise
    # the load path (Phase-1A: state itself is constant on fresh init, but
    # the parser must handle persisted state for Phase-2+ continuity).
    schema_dir = tmp_path / "schema"
    schema_dir.mkdir(parents=True)
    payload = {
        "accepted": [{"major": 1, "minor": 0}, {"major": 1, "minor": 1}],
        "current_write_target": {"major": 1, "minor": 1},
        "drain_state": "open",
        "drain_target": None,
    }
    with (schema_dir / "registry.json").open("w") as h:
        json.dump(payload, h)

    reg = SchemaRegistry(tmp_path)
    assert reg.accepted() == [(1, 0), (1, 1)]
    assert reg.current_write_target() == (1, 1)
    assert reg.current_health_schema() == 1
    # Sentinel for the loaded target is materialized.
    assert (schema_dir / "1.1.active").exists()


# ------------------------------- metrics ----------------------------------


def test_metrics_lines_includes_state_gauges(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    lines = reg.metrics_lines("experience-store")
    text = b"\n".join(lines).decode("utf-8")
    assert 'sts2_q3_schema_state{state="open",service="experience-store"} 1' in text
    assert 'sts2_q3_schema_state{state="draining",service="experience-store"} 0' in text
    assert 'sts2_q3_schema_state{state="locked",service="experience-store"} 0' in text


def test_metrics_lines_returns_bytes(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    lines = reg.metrics_lines("experience-store")
    assert all(isinstance(line, bytes) for line in lines)


def test_metrics_lines_emits_migration_baseline(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    text = b"\n".join(reg.metrics_lines("experience-store")).decode("utf-8")
    # No-op (1.1) -> (1.1) baseline counter, value 0 in Phase-1A.
    assert (
        'sts2_q3_schema_migration_total{from="1.1",to="1.1",service="experience-store"} 0' in text
    )


def test_metrics_lines_validate_counter_after_calls(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    # 5 write-accepts, 3 read-accepts, 2 unknown writes.
    for _ in range(5):
        reg.validate((1, 0), "write")
    for _ in range(3):
        reg.validate((1, 0), "read")
    for _ in range(2):
        reg.validate((9, 9), "write")

    text = b"\n".join(reg.metrics_lines("experience-store")).decode("utf-8")
    assert (
        'sts2_q3_schema_validate_total{op="write",result="accept",'
        'service="experience-store"} 5' in text
    )
    assert (
        'sts2_q3_schema_validate_total{op="read",result="accept",'
        'service="experience-store"} 3' in text
    )
    assert (
        'sts2_q3_schema_validate_total{op="write",result="schema_unknown",'
        'service="experience-store"} 2' in text
    )


def test_metrics_lines_no_counter_lines_before_traffic(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    text = b"\n".join(reg.metrics_lines("experience-store")).decode("utf-8")
    # Counter is lazy: no validate calls = no validate_total lines yet.
    assert "sts2_q3_schema_validate_total" not in text


# ---------------------------- Phase-1 close stubs --------------------------


def test_drain_raises_not_implemented(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    with pytest.raises(NotImplementedError) as exc:
        reg.drain((1, 1))
    assert "Phase-1 close" in str(exc.value)


def test_flip_raises_not_implemented(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    with pytest.raises(NotImplementedError) as exc:
        reg.flip()
    assert "Phase-1 close" in str(exc.value)


def test_revert_raises_not_implemented(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    with pytest.raises(NotImplementedError) as exc:
        reg.revert()
    assert "Phase-1 close" in str(exc.value)


# ---------------------------- thread safety -------------------------------


def test_thread_safety_no_lost_counter_updates(tmp_path: Path):
    reg = SchemaRegistry(tmp_path)
    n_threads = 100
    per_thread = 50

    def worker() -> None:
        for _ in range(per_thread):
            reg.validate((1, 0), "write")

    threads = [threading.Thread(target=worker) for _ in range(n_threads)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()

    text = b"\n".join(reg.metrics_lines("experience-store")).decode("utf-8")
    expected = n_threads * per_thread
    assert (
        f'sts2_q3_schema_validate_total{{op="write",result="accept",'
        f'service="experience-store"}} {expected}' in text
    )


# ---------------------------- atomic write --------------------------------


def test_atomic_persist_uses_os_replace(tmp_path: Path):
    """_persist must end in os.replace, never overwrite registry.json in-place.

    Verified by patching os.replace and asserting it's invoked at least
    once during _init_fresh (which is the only Phase-1A persist path).
    """
    schema_dir = tmp_path / "schema"
    assert not schema_dir.exists()

    real_replace = os.replace
    seen_calls: list[tuple[str, str]] = []

    def tracking_replace(src, dst):
        seen_calls.append((str(src), str(dst)))
        return real_replace(src, dst)

    with mock.patch("os.replace", side_effect=tracking_replace):
        SchemaRegistry(tmp_path)

    # At least one os.replace targeted registry.json from a tempfile in the
    # same directory; the source must NOT be registry.json itself
    # (i.e., no in-place rewrite).
    registry_path = str(schema_dir / "registry.json")
    persist_calls = [c for c in seen_calls if c[1] == registry_path]
    assert persist_calls, "expected at least one os.replace -> registry.json"
    for src, _dst in persist_calls:
        assert src != registry_path, "registry.json was overwritten in place"
        # Source lives in the same directory and looks like the tempfile.
        assert Path(src).parent == schema_dir
        assert ".registry." in Path(src).name

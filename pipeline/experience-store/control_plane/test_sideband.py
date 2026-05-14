"""Unit tests for SidebandRouter (S0.B.gamma)."""

from __future__ import annotations

import json

from control_plane.sideband import SIDEBAND_DIR, SIDEBAND_FILE, SidebandRouter


def test_record_creates_dir_and_file(tmp_path):
    router = SidebandRouter(tmp_path)
    target = tmp_path / SIDEBAND_DIR / SIDEBAND_FILE
    assert target.exists()
    assert target.parent.is_dir()


def test_record_appends_one_line_per_payload(tmp_path):
    router = SidebandRouter(tmp_path)
    for i in range(5):
        router.record_oracle_agreement(
            {
                "trajectory_id": f"traj-{i}",
                "oracle_label": "win" if i % 2 == 0 else "loss",
                "network_label": "win",
                "ts_ns": 1_000_000 + i,
            }
        )

    target = tmp_path / SIDEBAND_DIR / SIDEBAND_FILE
    lines = target.read_text(encoding="utf-8").splitlines()
    assert len(lines) == 5
    assert router.count() == 5


def test_payload_roundtrips_through_json(tmp_path):
    router = SidebandRouter(tmp_path)
    payload = {
        "trajectory_id": "abc",
        "details": {"k1": [1, 2, 3], "k2": {"nested": True}},
        "weight": 0.42,
    }
    router.record_oracle_agreement(payload)

    target = tmp_path / SIDEBAND_DIR / SIDEBAND_FILE
    lines = target.read_text(encoding="utf-8").splitlines()
    parsed = json.loads(lines[0])
    assert parsed == payload


def test_count_empty_router(tmp_path):
    router = SidebandRouter(tmp_path)
    assert router.count() == 0


def test_persistence_across_instances(tmp_path):
    router_a = SidebandRouter(tmp_path)
    router_a.record_oracle_agreement({"k": 1})
    router_a.record_oracle_agreement({"k": 2})

    router_b = SidebandRouter(tmp_path)
    router_b.record_oracle_agreement({"k": 3})
    assert router_b.count() == 3

    target = tmp_path / SIDEBAND_DIR / SIDEBAND_FILE
    lines = target.read_text(encoding="utf-8").splitlines()
    assert [json.loads(line)["k"] for line in lines] == [1, 2, 3]


def test_unicode_payload(tmp_path):
    router = SidebandRouter(tmp_path)
    payload = {"label": "won—decisively", "emoji_free_note": "ok"}
    router.record_oracle_agreement(payload)

    target = tmp_path / SIDEBAND_DIR / SIDEBAND_FILE
    parsed = json.loads(target.read_text(encoding="utf-8").splitlines()[0])
    assert parsed == payload

"""Integration tests for the W4 service.py wiring.

Boots the service in a subprocess against a fresh `tmp_path` data dir and
asserts cross-submodule HTTP behavior. Five tests by design: health,
metrics, /schema snapshot, /ingest/status at boot, and 404 fall-through.
Heavier per-submodule coverage already lives in `*/test_*.py`.
"""

from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
SERVICE_PY = REPO_ROOT / "pipeline" / "experience-store" / "service.py"


def _find_free_port() -> int:
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


@pytest.fixture
def running_service(tmp_path):
    config = {
        "service": "experience-store",
        "port": _find_free_port(),
        "data_dir": str(tmp_path / "q3-data"),
        "hot_high_water_bytes": 50_000_000_000,
        "hot_overflow_bytes": 100_000_000_000,
        "ingest_queue_capacity": 32,
        "max_body_bytes": 65536,
        "tick_interval_seconds": 1,
        "cursor_cache_capacity": 16,
        "cursor_idle_timeout_seconds": 30,
    }
    config_path = tmp_path / "local.json"
    config_path.write_text(json.dumps(config), encoding="utf-8")

    env = os.environ.copy()
    env["PYTHONPATH"] = str(REPO_ROOT)
    proc = subprocess.Popen(
        [sys.executable, str(SERVICE_PY), "--config", str(config_path)],
        cwd=str(REPO_ROOT),
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        start_new_session=True,
    )

    base_url = f"http://127.0.0.1:{config['port']}"
    deadline = time.monotonic() + 8
    last_err: Exception | None = None
    ready = False
    while time.monotonic() < deadline:
        if proc.poll() is not None:
            # Process died before health-ready; surface stderr.
            stderr = proc.stderr.read().decode("utf-8", errors="replace") if proc.stderr else ""
            raise AssertionError(
                f"service exited rc={proc.returncode} before health-ready; stderr:\n{stderr}"
            )
        try:
            with urllib.request.urlopen(f"{base_url}/health", timeout=1) as resp:
                if resp.status == 200:
                    ready = True
                    break
        except (urllib.error.URLError, TimeoutError, ConnectionError) as exc:
            last_err = exc
        time.sleep(0.1)
    if not ready:
        proc.terminate()
        try:
            proc.wait(timeout=2)
        except subprocess.TimeoutExpired:
            proc.kill()
        raise AssertionError(f"service not ready within deadline: {last_err}")

    try:
        yield base_url, proc, config
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=3)


def test_health_returns_schema_one(running_service):
    base_url, _, _ = running_service
    with urllib.request.urlopen(f"{base_url}/health") as resp:
        body = json.loads(resp.read().decode("utf-8"))
    assert body == {"service": "experience-store", "status": "ok", "schema": 1}


def test_metrics_includes_required_lines(running_service):
    base_url, _, _ = running_service
    with urllib.request.urlopen(f"{base_url}/metrics") as resp:
        text = resp.read().decode("utf-8")
    assert 'sts2_service_up{service="experience-store"} 1' in text
    assert "sts2_service_uptime_seconds" in text
    # SchemaRegistry contributes; ingest/sampler/lifecycle each contribute.
    assert "sts2_q3_schema_state" in text
    assert "sts2_q3_ingest_accepted_total" in text
    assert "sts2_q3_lifecycle_pressure_state" in text


def test_schema_endpoint_returns_state(running_service):
    base_url, _, _ = running_service
    with urllib.request.urlopen(f"{base_url}/schema") as resp:
        body = json.loads(resp.read().decode("utf-8"))
    assert body["current_write_target"] == {"major": 1, "minor": 0}
    assert body["drain_state"] == "open"
    assert body["accepted"] == [{"major": 1, "minor": 0}]


def test_ingest_status_returns_zero_at_boot(running_service):
    base_url, _, _ = running_service
    with urllib.request.urlopen(f"{base_url}/ingest/status") as resp:
        body = json.loads(resp.read().decode("utf-8"))
    assert body["queue_depth"] == 0
    assert body["accepted_total"] == 0
    assert body["queue_capacity"] == 32
    assert body["schema_drain_state"] == "open"


def test_unknown_route_returns_404(running_service):
    base_url, _, _ = running_service
    try:
        urllib.request.urlopen(f"{base_url}/no-such-route")
    except urllib.error.HTTPError as exc:
        assert exc.code == 404
        body = json.loads(exc.read().decode("utf-8"))
        assert body["error"] == "not_found"
        assert body["path"] == "/no-such-route"
    else:
        pytest.fail("expected 404")

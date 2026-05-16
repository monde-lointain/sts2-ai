"""Phase-1A CULTISTS_NORMAL golden trajectory test (S0.E).

Reads the committed golden binary at `tests/data/cultists_smoke_episode.bin`,
asserts D1 shape (schema 1.1, 8 combat steps, degenerate samples per
Q3-ADR-005), boots the Q3 service, POSTs the bytes to /trajectories, then
samples them back via /sample and verifies the returned step frames carry
the expected reward / action_taken / terminal / decision_type fields.
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

# Make pipeline/experience-store/ importable for proto + framing siblings.
_Q3_ROOT = Path(__file__).resolve().parents[1]
if str(_Q3_ROOT) not in sys.path:
    sys.path.insert(0, str(_Q3_ROOT))

from proto import DecisionType, ObservabilityRegime, Trajectory, TrajectoryStep
from sampler.framing import decode_varint

REPO_ROOT = Path(__file__).resolve().parents[3]
SERVICE_PY = REPO_ROOT / "pipeline" / "experience-store" / "service.py"
GOLDEN_PATH = Path(__file__).resolve().parent / "data" / "cultists_smoke_episode.bin"

EXPECTED_STEPS = 8


def _find_free_port() -> int:
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def _post(url: str, body: bytes, content_type: str) -> tuple[int, bytes]:
    req = urllib.request.Request(
        url,
        data=body,
        method="POST",
        headers={"Content-Type": content_type, "Content-Length": str(len(body))},
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read()


def _parse_sample_response(body: bytes) -> tuple[list[bytes], str]:
    payloads: list[bytes] = []
    offset = 0
    trailer_status = ""
    while offset < len(body):
        frame_len, consumed = decode_varint(body, offset)
        offset += consumed
        payload = body[offset : offset + frame_len]
        offset += frame_len
        try:
            maybe = json.loads(payload.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            payloads.append(payload)
            continue
        if isinstance(maybe, dict) and "status" in maybe:
            trailer_status = str(maybe["status"])
            break
        payloads.append(payload)
    return payloads, trailer_status


@pytest.fixture
def running_service(tmp_path):
    config = {
        "service": "experience-store",
        "port": _find_free_port(),
        "data_dir": str(tmp_path / "q3-data"),
        "hot_high_water_bytes": 50_000_000_000,
        "hot_overflow_bytes": 100_000_000_000,
        "ingest_queue_capacity": 32,
        "max_body_bytes": 1_048_576,
        "tick_interval_seconds": 60,
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
    ready = False
    last_err: Exception | None = None
    while time.monotonic() < deadline:
        if proc.poll() is not None:
            stderr = proc.stderr.read().decode("utf-8", errors="replace") if proc.stderr else ""
            raise AssertionError(f"service exited rc={proc.returncode}; stderr:\n{stderr}")
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
        raise AssertionError(f"service not ready: {last_err}")

    try:
        yield base_url
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=3)


def test_golden_binary_committed_and_well_shaped():
    """The committed CULTISTS_NORMAL golden is on-disk, parses, and is D1-shaped."""
    assert GOLDEN_PATH.exists(), f"golden missing: {GOLDEN_PATH}"
    blob = GOLDEN_PATH.read_bytes()
    # 100KB safety cap per the dispatch prompt.
    assert len(blob) <= 100 * 1024, f"golden too large: {len(blob)} bytes"

    traj = Trajectory()
    traj.ParseFromString(blob)
    assert (traj.schema_version.major, traj.schema_version.minor) == (1, 1)
    assert len(traj.steps) == EXPECTED_STEPS
    assert traj.trajectory_id == "phase-1a-cultists-smoke"
    assert traj.episode_id == "smoke-episode-001"
    assert traj.model_version == "phase-1a-stub-v0"
    assert traj.sampling_mode == "synthetic"
    assert traj.generator == "q3-synthetic-writer"

    for i, step in enumerate(traj.steps):
        assert step.decision_type == DecisionType.DECISION_TYPE_COMBAT, f"step[{i}] non-combat"
        assert step.observability_regime == ObservabilityRegime.OBSERVABILITY_REGIME_POLICY_VISIBLE
        # Degenerate sample per Q3-ADR-005.
        assert len(step.combat_outcome_samples) == 1
        sample = step.combat_outcome_samples[0]
        assert sample.probability_weight == 1.0
        assert sample.survived is True
        assert sample.hp_delta == pytest.approx(-0.05)
        # Reward/terminal schedule.
        is_terminal = i == EXPECTED_STEPS - 1
        assert step.terminal is is_terminal
        assert step.reward == pytest.approx(1.0 if is_terminal else 0.0)
        assert step.action_taken == i % 4


def test_golden_round_trip_through_service(running_service):
    """Boot service, POST the golden, sample back, verify step content matches."""
    base_url = running_service
    blob = GOLDEN_PATH.read_bytes()

    # Reference the golden as a parsed object so we can compare per-step
    # fields against what /sample returns.
    golden = Trajectory()
    golden.ParseFromString(blob)

    status, resp = _post(f"{base_url}/trajectories", blob, "application/x-protobuf")
    assert status == 202, f"/trajectories rejected: status={status} body={resp!r}"

    sample_req = json.dumps({"mode": "uniform", "batch_size": 100, "filters": {}}).encode("utf-8")
    status, body = _post(f"{base_url}/sample", sample_req, "application/json")
    assert status == 200, f"/sample returned {status}: {body!r}"

    step_payloads, trailer = _parse_sample_response(body)
    assert trailer in ("ok", "exhausted"), f"unexpected trailer: {trailer!r}"
    assert len(step_payloads) >= EXPECTED_STEPS, (
        f"expected >= {EXPECTED_STEPS} step frames; got {len(step_payloads)}"
    )

    returned: list[TrajectoryStep] = []
    for payload in step_payloads:
        step = TrajectoryStep()
        step.ParseFromString(payload)
        returned.append(step)

    # Map by (action_taken, terminal, reward) — the per-step keys that are
    # populated. Uniform mode preserves trajectory order, so the first
    # `EXPECTED_STEPS` returned must equal the golden's steps in order.
    for i, (golden_step, observed) in enumerate(
        zip(golden.steps, returned[:EXPECTED_STEPS], strict=False)
    ):
        assert observed.decision_type == golden_step.decision_type, f"step[{i}].decision_type drift"
        assert observed.terminal == golden_step.terminal, f"step[{i}].terminal drift"
        assert observed.reward == pytest.approx(golden_step.reward), (
            f"step[{i}].reward drift: got {observed.reward}"
        )
        assert observed.action_taken == golden_step.action_taken, f"step[{i}].action_taken drift"
        assert len(observed.combat_outcome_samples) == 1
        assert observed.combat_outcome_samples[0].probability_weight == 1.0

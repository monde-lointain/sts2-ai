"""R2 determinism round-trip — append N → sample → bytes-identical (S0.E).

Per the dispatch prompt's R2 gate: boot Q3 service on an ephemeral port,
POST N=100 fixed-seed trajectories, then POST /sample to read them back
as length-delimited TrajectoryStep frames; assert every returned step
matches some original trajectory's step (bytes-identical via protobuf
deserialization). Re-run /sample once more to assert idempotent response
content (uniform mode is deterministic FIFO; same hot_store + same
request body → same payload).

The Sampler frames responses as `varint(len)||step || ... || trailer`
(see `pipeline/experience-store/sampler/framing.py`). The trailer's
payload is JSON (`{"status":"ok"|"exhausted"}`).
"""

from __future__ import annotations

import hashlib
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

# Ensure pipeline/experience-store/ is importable for proto + framing.
_Q3_ROOT = Path(__file__).resolve().parents[1]
if str(_Q3_ROOT) not in sys.path:
    sys.path.insert(0, str(_Q3_ROOT))

from proto import Trajectory  # noqa: E402
from sampler.framing import decode_varint  # noqa: E402
from tests.synthetic_writer import build_trajectory  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[3]
SERVICE_PY = REPO_ROOT / "pipeline" / "experience-store" / "service.py"

N_TRAJECTORIES = 100
STEPS_PER_TRAJECTORY = 3  # small to keep total payload modest
SAMPLE_BATCH = N_TRAJECTORIES * STEPS_PER_TRAJECTORY  # cover everything


def _find_free_port() -> int:
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def _post(url: str, body: bytes, content_type: str) -> tuple[int, dict, bytes]:
    req = urllib.request.Request(
        url,
        data=body,
        method="POST",
        headers={"Content-Type": content_type, "Content-Length": str(len(body))},
    )
    try:
        with urllib.request.urlopen(req, timeout=5) as resp:
            return resp.status, dict(resp.headers.items()), resp.read()
    except urllib.error.HTTPError as exc:
        return exc.code, dict(exc.headers.items()) if exc.headers else {}, exc.read()


def _parse_sample_response(body: bytes) -> tuple[list[bytes], str]:
    """Return (step_payloads, trailer_status). Trailer payload is JSON."""
    payloads: list[bytes] = []
    offset = 0
    trailer_status = ""
    while offset < len(body):
        frame_len, consumed = decode_varint(body, offset)
        offset += consumed
        if offset + frame_len > len(body):
            raise AssertionError(
                f"truncated frame: need {frame_len} bytes at offset {offset}"
            )
        payload = body[offset:offset + frame_len]
        offset += frame_len
        # Trailer frame's payload is JSON; if it parses with a 'status' key,
        # treat it as the terminator.
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
        "ingest_queue_capacity": N_TRAJECTORIES * 2,
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


def _seeded_trajectory(i: int, base_seed: int = 0xC0FFEE) -> Trajectory:
    """Build a trajectory whose trajectory_id is deterministic from (seed, i)."""
    derived_seed = (base_seed + i) & 0xFFFFFFFFFFFFFFFF
    # Stable trajectory_id from hash so re-running this test produces the same
    # id string and we can correlate by it across runs.
    digest = hashlib.sha256(f"r2-roundtrip-{i:04d}".encode("utf-8")).hexdigest()[:32]
    return build_trajectory(
        n_steps=STEPS_PER_TRAJECTORY,
        seed=derived_seed,
        trajectory_id=f"r2-{digest}",
        episode_id=f"r2-episode-{i:04d}",
        model_version="r2-roundtrip-v1",
        sampling_mode="r2-roundtrip",
        generator="r2-determinism-test",
    )


def test_r2_determinism_round_trip(running_service):
    """Append 100 trajectories, sample them back, verify byte-equal steps.

    Per R2: every step bytes returned by /sample must match exactly the
    bytes of some `steps[j]` in the original trajectory set (protobuf
    serialization is canonical for these fields given fixed input).
    """
    base_url = running_service

    # 1. Build all originals up front so we can compare step bytes later.
    originals: list[Trajectory] = [_seeded_trajectory(i) for i in range(N_TRAJECTORIES)]
    original_step_bytes: set[bytes] = set()
    for traj in originals:
        for step in traj.steps:
            original_step_bytes.add(step.SerializeToString())
    # Sanity: per-step bytes uniqueness — our seeded rich_state makes each
    # step unique even within a trajectory, so the set size equals total
    # step count.
    assert len(original_step_bytes) == N_TRAJECTORIES * STEPS_PER_TRAJECTORY, (
        "synthetic builder should yield byte-unique steps per (i, step_idx)"
    )

    # 2. POST each trajectory; collect ingest_ts_ns to confirm strictly-
    #    monotonic assignment (HotStore invariant).
    last_ts = 0
    for i, traj in enumerate(originals):
        body = traj.SerializeToString()
        status, _h, resp = _post(
            f"{base_url}/trajectories", body, "application/x-protobuf"
        )
        assert status == 202, f"trajectory[{i}] rejected: {resp!r}"
        payload = json.loads(resp.decode("utf-8"))
        ts = int(payload["ingest_ts_ns"])
        assert ts > last_ts, (
            f"ingest_ts_ns must be strictly monotonic; got {ts} after {last_ts}"
        )
        last_ts = ts

    # 3. Sample-back call #1.
    sample_req = json.dumps(
        {"mode": "uniform", "batch_size": SAMPLE_BATCH, "filters": {}}
    ).encode("utf-8")
    status, _h, sample_body_1 = _post(
        f"{base_url}/sample", sample_req, "application/json"
    )
    assert status == 200, f"/sample returned {status}: {sample_body_1!r}"

    step_payloads_1, trailer_1 = _parse_sample_response(sample_body_1)
    assert trailer_1 in ("ok", "exhausted"), f"unexpected trailer: {trailer_1!r}"
    assert len(step_payloads_1) == N_TRAJECTORIES * STEPS_PER_TRAJECTORY, (
        f"expected {N_TRAJECTORIES * STEPS_PER_TRAJECTORY} step frames; "
        f"got {len(step_payloads_1)}"
    )

    # 4. Every returned step's bytes must match some original step bytes.
    returned_set = set(step_payloads_1)
    missing = original_step_bytes - returned_set
    extra = returned_set - original_step_bytes
    assert not missing, f"{len(missing)} original step(s) not returned"
    assert not extra, f"{len(extra)} returned step(s) had no original match"

    # 5. Determinism: same hot_store + same /sample body → same response.
    #    Reuse the same path with a fresh request (no cursor_id provided,
    #    so the cursor is re-seeded from position 0).
    status, _h, sample_body_2 = _post(
        f"{base_url}/sample", sample_req, "application/json"
    )
    assert status == 200
    step_payloads_2, trailer_2 = _parse_sample_response(sample_body_2)
    assert trailer_1 == trailer_2, "trailer divergence between identical requests"
    assert step_payloads_1 == step_payloads_2, (
        "R2 violation: re-sample returned different step payload sequence"
    )

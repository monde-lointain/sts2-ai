#!/usr/bin/env python3
import json
import os
import signal
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SERVICES = [
    "experience-store",
    "model-registry",
    "inference-server",
    "rollout-workers",
    "trainer",
    "evaluation-harness",
    "observability",
]
# Services whose /health "schema" field deviates from the default 0; per
# spec, Q3 experience-store reports its current write-target major (=1 in
# Phase-1A) so smoke can verify SchemaRegistry wiring end-to-end.
SERVICE_HEALTH_SCHEMAS: dict[str, int] = {"experience-store": 1}


def request(url: str) -> tuple[int, bytes]:
    with urllib.request.urlopen(url, timeout=2) as response:
        return response.status, response.read()


def wait_for_health(port: int) -> dict:
    deadline = time.monotonic() + 5
    last_error = None
    while time.monotonic() < deadline:
        try:
            status, body = request(f"http://127.0.0.1:{port}/health")
            if status == 200:
                return json.loads(body.decode("utf-8"))
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
            last_error = exc
        time.sleep(0.1)
    raise AssertionError(f"service did not become healthy on port {port}: {last_error}")


def smoke_service(service: str) -> None:
    service_dir = REPO_ROOT / "pipeline" / service
    config_path = service_dir / "config" / "local.json"
    config = json.loads(config_path.read_text(encoding="utf-8"))
    env = os.environ.copy()
    env["PYTHONPATH"] = str(REPO_ROOT)
    process = subprocess.Popen(
        [sys.executable, str(service_dir / "service.py"), "--config", str(config_path)],
        cwd=REPO_ROOT,
        env=env,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        start_new_session=True,
    )
    try:
        health = wait_for_health(int(config["port"]))
        expected_schema = SERVICE_HEALTH_SCHEMAS.get(service, 0)
        if health != {"service": service, "status": "ok", "schema": expected_schema}:
            raise AssertionError(f"{service} bad health: {health}")
        data_dir = REPO_ROOT / config["data_dir"]
        if not data_dir.is_dir():
            raise AssertionError(f"{service} data dir missing: {data_dir}")
        status, metrics = request(f"http://127.0.0.1:{config['port']}/metrics")
        expected = f'sts2_service_up{{service="{service}"}} 1'
        if status != 200 or expected not in metrics.decode("utf-8"):
            raise AssertionError(f"{service} missing metric {expected}")
    finally:
        os.killpg(process.pid, signal.SIGTERM)
        try:
            process.communicate(timeout=3)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.communicate(timeout=3)


def main() -> int:
    for service in SERVICES:
        smoke_service(service)
        print(f"ok {service}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Q3 ExperienceStore service entrypoint (S0.D — W4 integration).

Wires the W2 substrate (HotStore, SchemaRegistry, ControlPlane) and W3
front-doors (IngestAPI, Sampler, Lifecycle) into a ThreadingHTTPServer.

Routing (per modules/* specs):
  GET  /health              — service status + schema major (Phase-1A: 1)
  GET  /metrics             — Prometheus text v0.0.4 aggregate
  GET  /schema              — SchemaRegistry state snapshot
  GET  /ingest/status       — writer-side queue depth + totals
  GET  /sample/recent       — convenience sampler GET (Q12)
  GET  /sample/cursor/<id>  — cursor debug
  GET  /lifecycle/status    — policy + last tick action
  GET  /provenance/<id>     — single trajectory provenance lookup
  GET  /retention/state     — hot bytes + queue depth snapshot
  POST /trajectories        — single-trajectory ingest
  POST /trajectories:batch  — batch ingest (length-delimited frames)
  POST /sample              — uniform sampling
  POST /lifecycle/policy    — operator policy update
  POST /lifecycle/force_tick— operator-triggered tick
  POST /sideband/oracle-agreement — Q2 oracle-agreement landing
"""

from __future__ import annotations

import argparse
import json
import re
import signal
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

# Hyphenated dir layout (`pipeline/experience-store/`) prevents the standard
# package import path; inject this dir so siblings resolve as top-level
# modules. Mirrors conftest.py used by the test suite.
sys.path.insert(0, str(Path(__file__).parent))

from control_plane import (
    MetricsEmitter,
    ProvenanceLog,
    RetentionController,
    SidebandRouter,
)
from hot_store import HotStore
from ingest_api.api import IngestAPI
from lifecycle.lifecycle import Lifecycle
from sampler.api import Sampler
from schema_registry import SchemaRegistry


def load_config(path: Path) -> dict:
    with path.open(encoding="utf-8") as handle:
        config = json.load(handle)
    required = {"service", "port", "data_dir"}
    missing = sorted(required - set(config))
    if missing:
        raise ValueError(f"{path}: missing {', '.join(missing)}")
    return config


CURSOR_PATH_RE = re.compile(r"^/sample/cursor/([0-9a-fA-F]{1,64})$")
PROVENANCE_PATH_RE = re.compile(r"^/provenance/(.+)$")


class QuantumRequestHandler(BaseHTTPRequestHandler):
    """HTTP handler dispatching to W2/W3 submodule handlers.

    All POST/GET handlers on the submodules return
    `(status, headers_dict, body_bytes)`; this handler is the thin shim
    that adapts that tuple to `BaseHTTPRequestHandler`.
    """

    server_version = "sts2-q3/1"

    def log_message(self, fmt: str, *args) -> None:  # quiet by default
        return

    def _read_body(self) -> bytes:
        length = int(self.headers.get("Content-Length") or 0)
        if length <= 0:
            return b""
        return self.rfile.read(length)

    def _send(self, status: int, headers: dict, body: bytes) -> None:
        self.send_response(status)
        sent_content_length = False
        for key, val in headers.items():
            if key.lower() == "content-length":
                sent_content_length = True
            self.send_header(key, str(val))
        if not sent_content_length:
            self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _send_json(self, status: int, payload: dict) -> None:
        body = json.dumps(payload).encode("utf-8")
        self._send(status, {"Content-Type": "application/json"}, body)

    def do_GET(self) -> None:
        srv: QuantumService = self.server  # type: ignore[assignment]
        path = self.path

        if path == "/health":
            schema_major = srv.schema_registry.current_health_schema()
            self._send_json(
                200,
                {
                    "service": srv.config["service"],
                    "status": "ok",
                    "schema": int(schema_major),
                },
            )
            return

        if path == "/metrics":
            body = srv.build_metrics_payload()
            self._send(200, {"Content-Type": "text/plain; version=0.0.4"}, body)
            return

        if path == "/ingest/status":
            status, headers, body = srv.ingest_api.handle_get_ingest_status()
            self._send(status, headers, body)
            return

        if path == "/sample/recent":
            status, headers, body = srv.sampler.handle_get_sample_recent()
            self._send(status, headers, body)
            return

        match_cursor = CURSOR_PATH_RE.match(path)
        if match_cursor:
            status, headers, body = srv.sampler.handle_get_sample_cursor(match_cursor.group(1))
            self._send(status, headers, body)
            return

        if path == "/lifecycle/status":
            status, headers, body = srv.lifecycle.handle_get_lifecycle_status()
            self._send(status, headers, body)
            return

        if path == "/schema":
            self._send_json(200, srv.schema_registry.state_snapshot())
            return

        if path == "/retention/state":
            self._send_json(
                200,
                {
                    "hot_bytes": srv.hot_store.range_size_bytes(),
                    "queue_depth": srv.ingest_api.queue_depth(),
                    "queue_capacity": srv.config.get("ingest_queue_capacity", 4096),
                },
            )
            return

        match_prov = PROVENANCE_PATH_RE.match(path)
        if match_prov:
            row = srv.provenance_log.lookup(match_prov.group(1))
            if row is None:
                self._send_json(
                    404,
                    {
                        "error": "provenance_not_found",
                        "trajectory_id": match_prov.group(1),
                    },
                )
            else:
                self._send_json(200, row)
            return

        self._send_json(404, {"error": "not_found", "path": path})

    def do_POST(self) -> None:
        srv: QuantumService = self.server  # type: ignore[assignment]
        path = self.path
        body = self._read_body()
        content_type = self.headers.get("Content-Type", "")

        if path == "/trajectories":
            status, headers, body_out = srv.ingest_api.handle_post_trajectories(body, content_type)
            self._send(status, headers, body_out)
            return

        if path == "/trajectories:batch":
            status, headers, body_out = srv.ingest_api.handle_post_trajectories_batch(
                body, content_type
            )
            self._send(status, headers, body_out)
            return

        if path == "/sample":
            status, headers, body_out = srv.sampler.handle_post_sample(body)
            self._send(status, headers, body_out)
            return

        if path == "/lifecycle/policy":
            status, headers, body_out = srv.lifecycle.handle_post_lifecycle_policy(body)
            self._send(status, headers, body_out)
            return

        if path == "/lifecycle/force_tick":
            status, headers, body_out = srv.lifecycle.handle_post_lifecycle_force_tick()
            self._send(status, headers, body_out)
            return

        if path == "/sideband/oracle-agreement":
            try:
                payload = json.loads(body.decode("utf-8")) if body else {}
            except (UnicodeDecodeError, json.JSONDecodeError):
                self._send_json(400, {"error": "malformed_json"})
                return
            srv.sideband_router.record_oracle_agreement(payload)
            self._send_json(202, {"status": "accepted"})
            return

        self._send_json(404, {"error": "not_found", "path": path})


class QuantumService(ThreadingHTTPServer):
    """ExperienceStore service. Owns the W2/W3 submodule instances.

    Construction order matters: SchemaRegistry/HotStore/ProvenanceLog/
    RetentionController/SidebandRouter must exist before IngestAPI,
    Sampler, and Lifecycle (which take them as dependencies).
    """

    allow_reuse_address = True

    def __init__(self, address, config: dict) -> None:
        super().__init__(address, QuantumRequestHandler)
        self.config = config
        self.started_at = time.monotonic()

        data_dir = Path(config["data_dir"])
        if not data_dir.is_absolute():
            # Mirror pipeline/common/service_host.py:60-64: resolve relative
            # data_dir against the repo root (parents[2] of this file:
            # pipeline/experience-store/service.py → repo root).
            repo_root = Path(__file__).resolve().parents[2]
            data_dir = repo_root / data_dir
        data_dir.mkdir(parents=True, exist_ok=True)
        self.data_dir = data_dir

        # W2 substrate.
        self.schema_registry = SchemaRegistry(data_dir)
        self.hot_store = HotStore(data_dir)
        self.provenance_log = ProvenanceLog(data_dir)
        self.retention_controller = RetentionController(config, data_dir)
        self.sideband_router = SidebandRouter(data_dir)
        self.metrics_emitter = MetricsEmitter(config["service"], self.started_at)

        # W3 front doors.
        self.ingest_api = IngestAPI(
            self.hot_store,
            self.schema_registry,
            self.provenance_log,
            queue_capacity=int(config.get("ingest_queue_capacity", 4096)),
            max_body_bytes=int(config.get("max_body_bytes", 64 * 1024 * 1024)),
        )
        self.sampler = Sampler(
            self.hot_store,
            self.schema_registry,
            cursor_cache_capacity=int(config.get("cursor_cache_capacity", 1024)),
            cursor_idle_timeout_seconds=int(config.get("cursor_idle_timeout_seconds", 300)),
        )
        self.lifecycle = Lifecycle(
            self.hot_store,
            self.retention_controller,
            self.ingest_api.queue_depth,
            int(config.get("ingest_queue_capacity", 4096)),
            data_dir,
        )

        self._stop_event = threading.Event()
        self._consumer_thread: threading.Thread | None = None

    def start_background_threads(self) -> None:
        self.lifecycle.start(self._stop_event)
        self._consumer_thread = threading.Thread(
            target=self.ingest_api.consumer_loop,
            args=(self._stop_event,),
            name="ingest-consumer",
            daemon=True,
        )
        self._consumer_thread.start()

    def shutdown_background(self) -> None:
        self._stop_event.set()
        if self._consumer_thread is not None:
            self._consumer_thread.join(timeout=5.0)

    def build_metrics_payload(self) -> bytes:
        """Aggregate Prometheus text across submodules.

        Per modules/control-plane.md the ControlPlane MetricsEmitter is the
        Phase-1A authoritative source for `sts2_service_up`,
        `sts2_service_uptime_seconds`, and Q3 counters/gauges. Each W3
        submodule additionally owns module-scoped metrics; we append those
        lines after the emitter's base payload.
        """
        base = self.metrics_emitter.format_metrics()
        extra_lines: list[bytes] = []
        emitters = (
            self.schema_registry,
            self.ingest_api,
            self.sampler,
            self.lifecycle,
        )
        for emitter in emitters:
            try:
                extra_lines.extend(emitter.metrics_lines(self.config["service"]))
            except Exception:
                continue
        if not extra_lines:
            return base
        tail = b"\n".join(extra_lines) + b"\n"
        return base + tail


def run(config_path: Path) -> int:
    config = load_config(config_path)
    server = QuantumService(("127.0.0.1", int(config["port"])), config)
    server.start_background_threads()

    def _on_signal(_sig, _frame):
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, _on_signal)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.shutdown_background()
        server.server_close()
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, required=True)
    args = parser.parse_args()
    return run(args.config)


if __name__ == "__main__":
    raise SystemExit(main())

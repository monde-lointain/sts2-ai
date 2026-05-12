from __future__ import annotations

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import argparse
import json
from pathlib import Path
import time


def load_config(path: Path) -> dict:
    with path.open(encoding="utf-8") as handle:
        config = json.load(handle)
    required = {"service", "port", "data_dir"}
    missing = sorted(required - set(config))
    if missing:
        raise ValueError(f"{path}: missing {', '.join(missing)}")
    return config


class Handler(BaseHTTPRequestHandler):
    server_version = "sts2-service/0"

    def do_GET(self) -> None:
        config = self.server.config
        if self.path == "/health":
            self._json({"service": config["service"], "status": "ok", "schema": 0})
            return
        if self.path == "/metrics":
            uptime = time.monotonic() - self.server.started_at
            body = (
                f'sts2_service_up{{service="{config["service"]}"}} 1\n'
                f'sts2_service_uptime_seconds{{service="{config["service"]}"}} {uptime:.3f}\n'
            )
            self._send(200, "text/plain; version=0.0.4", body.encode("utf-8"))
            return
        self._json({"error": "not found"}, status=404)

    def log_message(self, fmt: str, *args) -> None:
        return

    def _json(self, payload: dict, status: int = 200) -> None:
        self._send(status, "application/json", json.dumps(payload).encode("utf-8"))

    def _send(self, status: int, content_type: str, body: bytes) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


class ServiceServer(ThreadingHTTPServer):
    def __init__(self, address, handler, config: dict):
        super().__init__(address, handler)
        self.config = config
        self.started_at = time.monotonic()


def run(config_path: Path) -> int:
    config = load_config(config_path)
    data_dir = Path(config["data_dir"])
    if not data_dir.is_absolute():
        data_dir = config_path.parents[3] / data_dir
    data_dir.mkdir(parents=True, exist_ok=True)
    server = ServiceServer(("127.0.0.1", int(config["port"])), Handler, config)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        return 0
    finally:
        server.server_close()
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, required=True)
    args = parser.parse_args()
    return run(args.config)


if __name__ == "__main__":
    raise SystemExit(main())

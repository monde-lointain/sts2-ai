"""Unit tests for ``pipeline.trainer.service`` (S0.F).

Covers the four tests called out in the boot directive:

1. ``TrainerServer`` constructs without error.
2. ``TrainerHandler`` /health returns the smoke contract.
3. ``TrainerHandler`` /metrics returns valid Prometheus text containing
   ``sts2_service_up``.
4. ``shutdown_background`` completes within timeout.

Q3 is mocked at the prefetcher daemon's HTTP boundary so DataIngest can
start without a live Q3 service; the daemon is allowed to die naturally
on its first failed POST without taking the HTTP server with it.
"""
from __future__ import annotations

import json
import threading
import time
import urllib.error
import urllib.request
from pathlib import Path
from unittest import mock

import pytest

from pipeline.trainer.service import (
    TrainerHandler,
    TrainerServer,
    _resolve_data_dir,
)


_CFG_PATH = Path(__file__).resolve().parents[1] / "config" / "local.json"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def _load_config_dict() -> dict:
    return json.loads(_CFG_PATH.read_text(encoding="utf-8"))


def _pick_port() -> int:
    """Pick an ephemeral free port to avoid clashing with the service's 18110."""
    import socket as _s

    sock = _s.socket(_s.AF_INET, _s.SOCK_STREAM)
    sock.bind(("127.0.0.1", 0))
    port = sock.getsockname()[1]
    sock.close()
    return port


def _make_server(tmp_path: Path, port: int | None = None) -> TrainerServer:
    """Build a TrainerServer with the data_dir redirected into ``tmp_path``.

    Patches ``urllib.request.urlopen`` so the DataIngest prefetcher daemon
    cannot make outbound calls — it will die on first POST (intended;
    daemon thread death must not crash the HTTP server).
    """
    config = _load_config_dict()
    config["data_dir"] = str(tmp_path / "trainer")
    chosen_port = int(port) if port is not None else _pick_port()
    config["port"] = chosen_port
    # Stage config on disk so RunConfig.load() can re-read the nested keys.
    cfg_path = tmp_path / "local.json"
    cfg_path.write_text(json.dumps(config), encoding="utf-8")

    # Pre-create data dir (run() handles this normally).
    (tmp_path / "trainer").mkdir(parents=True, exist_ok=True)

    return TrainerServer(
        ("127.0.0.1", chosen_port),
        TrainerHandler,
        config,
        config_path=cfg_path,
    )


# ---------------------------------------------------------------------------
# Test 1: server constructs without error
# ---------------------------------------------------------------------------
def test_server_constructs(tmp_path: Path) -> None:
    server = _make_server(tmp_path)
    try:
        assert server.config["service"] == "trainer"
        assert server.run_config.service == "trainer"
        # All major submodule handles wired.
        assert server.content_registry is not None
        assert server.model is not None
        assert server.encoder is not None
        assert server.optim is not None
        assert server.loss_engine is not None
        assert server.ingest is not None
        assert server.metrics is not None
        assert server.publisher is not None
        assert server.driver is not None
        # Stop-event present and clear.
        assert isinstance(server._stop_event, threading.Event)
        assert not server._stop_event.is_set()
    finally:
        server.metrics.shutdown(timeout=2.0)
        server.server_close()


# ---------------------------------------------------------------------------
# Test 2: /health returns smoke contract
# ---------------------------------------------------------------------------
def test_health_returns_smoke_contract(tmp_path: Path) -> None:
    port = _pick_port()
    server = _make_server(tmp_path, port=port)
    server_thread = threading.Thread(
        target=server.serve_forever, name="test-server", daemon=True
    )
    server_thread.start()
    try:
        # Brief wait for the bind.
        time.sleep(0.05)
        with urllib.request.urlopen(
            f"http://127.0.0.1:{port}/health", timeout=2
        ) as resp:
            assert resp.status == 200
            payload = json.loads(resp.read().decode("utf-8"))
        assert payload == {"service": "trainer", "status": "ok", "schema": 0}
    finally:
        server.shutdown()
        server_thread.join(timeout=2.0)
        server.metrics.shutdown(timeout=2.0)
        server.server_close()


# ---------------------------------------------------------------------------
# Test 3: /metrics returns Prometheus text containing sts2_service_up
# ---------------------------------------------------------------------------
def test_metrics_returns_prometheus_with_service_up(tmp_path: Path) -> None:
    port = _pick_port()
    server = _make_server(tmp_path, port=port)
    server_thread = threading.Thread(
        target=server.serve_forever, name="test-server", daemon=True
    )
    server_thread.start()
    try:
        time.sleep(0.05)
        with urllib.request.urlopen(
            f"http://127.0.0.1:{port}/metrics", timeout=2
        ) as resp:
            assert resp.status == 200
            body = resp.read().decode("utf-8")
        # Smoke-contract gate (smoke_services.py checks this exact string).
        assert 'sts2_service_up{service="trainer"} 1' in body
        assert 'sts2_service_uptime_seconds{service="trainer"}' in body
        # MetricsEmitter must include the canonical Q10 gauges.
        assert "sts2_q10_run_dirty" in body
        assert "sts2_q10_model_param_count" in body
    finally:
        server.shutdown()
        server_thread.join(timeout=2.0)
        server.metrics.shutdown(timeout=2.0)
        server.server_close()


def test_404_returns_json(tmp_path: Path) -> None:
    port = _pick_port()
    server = _make_server(tmp_path, port=port)
    server_thread = threading.Thread(
        target=server.serve_forever, name="test-server", daemon=True
    )
    server_thread.start()
    try:
        time.sleep(0.05)
        try:
            urllib.request.urlopen(
                f"http://127.0.0.1:{port}/no-such-endpoint", timeout=2
            )
            raise AssertionError("expected 404")
        except urllib.error.HTTPError as exc:
            assert exc.code == 404
            body = json.loads(exc.read().decode("utf-8"))
            assert body["error"] == "not_found"
    finally:
        server.shutdown()
        server_thread.join(timeout=2.0)
        server.metrics.shutdown(timeout=2.0)
        server.server_close()


# ---------------------------------------------------------------------------
# Test 4: shutdown_background completes within timeout
# ---------------------------------------------------------------------------
@pytest.mark.filterwarnings("ignore::pytest.PytestUnhandledThreadExceptionWarning")
def test_shutdown_background_completes_in_time(tmp_path: Path) -> None:
    """With Q3 mocked unreachable, signal stop and assert shutdown < 15 s.

    The train_driver thread raises ``Q3UnavailableError`` per Q10-ADR-004
    when DataIngest captures a transport error; that exception surfaces
    through ``get_batch`` and the daemon exits via ``stop_event.set()`` in
    its ``except`` handler. shutdown_background then has nothing to join.
    """
    server = _make_server(tmp_path)

    # Patch urllib.request.urlopen in data_ingest so the prefetcher's
    # POST raises a URLError (treated as Q3UnavailableError); the daemon
    # then dies cleanly.
    with mock.patch(
        "pipeline.trainer.data_ingest.urllib.request.urlopen",
        side_effect=urllib.error.URLError("mock: q3 unreachable"),
    ):
        # Patch the driver loop's get_batch timeout to be tiny so the
        # driver's first iteration short-circuits and the daemon exits
        # cleanly on stop_event.
        with mock.patch(
            "pipeline.trainer.train_driver._GET_BATCH_TIMEOUT_SEC", 0.5
        ):
            try:
                server.start_background_threads()
                # Give the threads a moment to start; the prefetcher will
                # die on its first urlopen and store the exception.
                time.sleep(0.2)

                t0 = time.monotonic()
                server.shutdown_background(timeout=10.0)
                elapsed = time.monotonic() - t0
                # All daemons should rejoin well within the budget.
                assert elapsed < 15.0, (
                    f"shutdown took {elapsed:.2f}s; budget 15s"
                )
            finally:
                server.server_close()


# ---------------------------------------------------------------------------
# resolve_data_dir helper
# ---------------------------------------------------------------------------
def test_resolve_data_dir_relative_to_repo_root() -> None:
    """Relative ``data_dir`` resolves under the repo root."""
    config = {"data_dir": "data/trainer"}
    resolved = _resolve_data_dir(config)
    assert resolved.is_absolute()
    assert resolved.name == "trainer"
    assert resolved.parent.name == "data"


def test_resolve_data_dir_absolute_passes_through(tmp_path: Path) -> None:
    abs_dir = tmp_path / "absolute-trainer-data"
    config = {"data_dir": str(abs_dir)}
    resolved = _resolve_data_dir(config)
    assert resolved == abs_dir

#!/usr/bin/env python3
"""Q10 Trainer service entrypoint (S0.F — wiring wave).

Constructs every Q10 submodule, spawns daemon threads (data_ingest,
artifact_publisher, train_driver), and serves ``/health`` + ``/metrics``
over HTTP. Per Q10-ADR-001 (modular monolith + producer-consumer): one
process, port 18110, three daemon threads share a single
``threading.Event`` stop signal.

Routing (Phase-1 minimal surface — no operator endpoints today):
  GET  /health   — ``{"service":"trainer","status":"ok","schema":0}``
  GET  /metrics  — Prometheus text v0.0.4 from MetricsEmitter

Fail-soft posture (per Q10-ADR-004 only at run-time, not boot):
  - Q3 unreachable at boot: the prefetcher daemon dies on first POST; the
    HTTP server keeps serving /health + /metrics for observability.
  - ``parent_artifact_id`` set but artifact missing on disk: boot from
    scratch with a warn log.
"""

from __future__ import annotations

import os.path as _osp

# Pre-import sys.path repair: when launched directly as
# ``python pipeline/trainer/service.py`` (as smoke_services.py does),
# Python inserts this script's parent dir (``pipeline/trainer/``) at
# ``sys.path[0]``. That shadows stdlib ``types`` because of the local
# ``pipeline/trainer/types.py``. The very next stdlib import (pathlib →
# fnmatch → re → enum → types) then fails. Evict the script dir + put
# the repo root first BEFORE importing any other stdlib module.
#
# We use only ``sys`` and ``os.path`` (manual string ops) here — those
# do not transitively import ``types`` on the cold-import path.
import sys as _sys

_THIS_DIR = _osp.dirname(_osp.abspath(__file__))
_REPO_ROOT = _osp.dirname(_osp.dirname(_THIS_DIR))
_sys.path[:] = [p for p in _sys.path if _osp.abspath(p) != _THIS_DIR]
if _REPO_ROOT not in _sys.path:
    _sys.path.insert(0, _REPO_ROOT)

import argparse
import pathlib
import signal
import threading

import torch

from pipeline.common.service_host import Handler, ServiceServer, load_config
from pipeline.trainer.artifact_publisher import ArtifactPublisher
from pipeline.trainer.content_registry import ContentRegistry
from pipeline.trainer.data_ingest import DataIngest
from pipeline.trainer.loss_engine import LossEngine
from pipeline.trainer.metrics_emitter import MetricsEmitter
from pipeline.trainer.model import TrainerNet
from pipeline.trainer.optim import OptimController
from pipeline.trainer.run_config import RunConfig, RunProvenance, seed_everything
from pipeline.trainer.tensor_encoder import _MACRO_DIM, EncodedBatch, TensorEncoder
from pipeline.trainer.train_driver import TrainDriver

# Phase-1: registry path is fixed at `contracts/registry/phase1-silent.json`
# relative to the repo root (Q10-ADR-008 — frozen at bootstrap).
_REGISTRY_RELATIVE: pathlib.Path = pathlib.Path("contracts/registry/phase1-silent.json")


def _resolve_repo_root() -> pathlib.Path:
    """Repo root: `pipeline/trainer/service.py` -> parents[2]."""
    return pathlib.Path(__file__).resolve().parents[2]


def _resolve_data_dir(config: dict) -> pathlib.Path:
    """Resolve ``config['data_dir']`` against repo root if relative."""
    data_dir = pathlib.Path(config["data_dir"])
    if not data_dir.is_absolute():
        data_dir = _resolve_repo_root() / data_dir
    return data_dir


class TrainerHandler(Handler):
    """HTTP handler for /health + /metrics — Phase-1 minimal surface.

    POSTs are not supported (Q10 has no operator endpoints in Phase 1).
    """

    server_version = "sts2-q10/1"

    def do_GET(self) -> None:  # noqa: N802 — http.server interface
        srv: TrainerServer = self.server  # type: ignore[assignment]
        path = self.path

        if path == "/health":
            self._json({"service": srv.config["service"], "status": "ok", "schema": 0})
            return

        if path == "/metrics":
            body = srv.metrics.format_metrics()
            self._send(200, "text/plain; version=0.0.4", body)
            return

        self._json({"error": "not_found", "path": path}, status=404)

    def do_POST(self) -> None:  # noqa: N802 — http.server interface
        self._json({"error": "method_not_allowed", "path": self.path}, status=405)


class TrainerServer(ServiceServer):
    """Q10 Trainer service.

    Owns: RunConfig, RunProvenance, ContentRegistry, TrainerNet,
    TensorEncoder, OptimController, LossEngine, DataIngest, MetricsEmitter,
    ArtifactPublisher, TrainDriver. Three daemon threads share
    ``self._stop_event``.
    """

    allow_reuse_address = True

    def __init__(
        self,
        address: tuple[str, int],
        handler: type,
        config: dict,
        *,
        config_path: pathlib.Path,
    ) -> None:
        super().__init__(address, handler, config)
        self.config_path = config_path

        # 1) RunConfig — re-load to obtain the nested frozen dataclasses
        #    (the parent's ``load_config`` only validated top-level keys).
        self.run_config = RunConfig.load(config_path)
        seed_everything(self.run_config.seed)

        # 2) RunProvenance — captured once before mutable state starts.
        self.run_provenance = RunProvenance.capture(
            self.run_config,
            parent_artifact_id=self.run_config.parent_artifact_id,
        )

        # 3) Resolved data_dir (mkdir handled by run()).
        self.data_dir = _resolve_data_dir(config)

        # 4) ContentRegistry — frozen at bootstrap (Q10-ADR-008).
        registry_path = _resolve_repo_root() / _REGISTRY_RELATIVE
        self.content_registry = ContentRegistry.load(registry_path)

        # 5) TrainerNet + TensorEncoder.
        self.model = TrainerNet(self.run_config.network, self.content_registry)
        self.encoder = TensorEncoder(self.content_registry, self.run_config.network)

        # 6) OptimController + LossEngine.
        self.optim = OptimController(self.model, self.run_config.optim)
        self.loss_engine = LossEngine(self.run_config, self.model)

        # 7) DataIngest (Q3 sampling client).
        self.ingest = DataIngest(self.run_config)

        # 8) MetricsEmitter — uses self.started_at from the parent.
        self.metrics = MetricsEmitter(
            service_name="trainer",
            started_at=self.started_at,
            wandb_enabled=self.run_config.wandb.enabled,
        )

        # 9) ArtifactPublisher — needs a dummy-batch provider for ONNX.
        self.publisher = ArtifactPublisher(
            self.run_config,
            self.run_provenance,
            self.content_registry,
            model_for_onnx=self.model,
            dummy_batch_provider=self._dummy_batch_provider,
            consumed_ids_provider=self.ingest.snapshot_consumed_ids,
            data_dir=self.data_dir,
        )

        # 10) TrainDriver — last; consumes all of the above.
        self.driver = TrainDriver(
            self.run_config,
            self.run_provenance,
            self.model,
            self.encoder,
            self.ingest,
            self.loss_engine,
            self.optim,
            self.publisher,
            self.metrics,
        )

        # 11) Shared stop signal for all three daemon threads.
        self._stop_event = threading.Event()

        # 12) Bootstrap-time gauges.
        self.metrics.set(
            "sts2_q10_run_dirty",
            1 if self.run_provenance.run_dirty else 0,
        )
        self.metrics.set(
            "sts2_q10_model_param_count",
            sum(p.numel() for p in self.model.parameters()),
        )

    # ------------------------------------------------------------------
    # Public lifecycle
    # ------------------------------------------------------------------
    def start_background_threads(self) -> None:
        """Spawn the three Q10 daemon threads. Returns immediately."""
        self._stop_event.clear()
        self.ingest.start(self._stop_event)
        self.publisher.start(self._stop_event)
        self.driver.start(self._stop_event)

    def shutdown_background(self, timeout: float = 30.0) -> None:
        """Signal stop and bounded-join every daemon thread."""
        self._stop_event.set()
        # Driver gets the largest budget — it may be mid-step.
        self.driver.join(timeout=timeout)
        # Prefetcher + publisher are blocked on bounded queue polls; they
        # exit within the queue's poll interval.
        self.ingest.join(timeout=5.0)
        self.publisher.join(timeout=5.0)
        # W&B sidecar (no-op when disabled).
        self.metrics.shutdown(timeout=10.0)

    # ------------------------------------------------------------------
    # Internals
    # ------------------------------------------------------------------
    def _dummy_batch_provider(self) -> EncodedBatch:
        """Build a tiny EncodedBatch for ``torch.onnx.export`` dummy input.

        Shape is the smallest-viable that still exercises every network
        head: B=1, T=max_seq_len//4, A=8. Larger than 1 token because the
        TransformerEncoder applies positional embedding lookups.
        """
        b = 1
        t = max(1, int(self.run_config.network.max_seq_len) // 4)
        a = 8
        return EncodedBatch(
            tokens=torch.zeros((b, t), dtype=torch.long),
            padding_mask=torch.zeros((b, t), dtype=torch.bool),
            legal_action_mask=torch.ones((b, a), dtype=torch.bool),
            policy_target=torch.nn.functional.softmax(torch.randn((b, a)), dim=-1),
            combat_sample_targets=torch.zeros((b, 4)),
            combat_summary_targets=torch.zeros((b, 5)),
            hp_frac_target=torch.zeros((b,)),
            prior_logits=torch.zeros((b, a)),
            macro_context=torch.zeros((b, _MACRO_DIM)),
            metadata={
                "content_registry_sha": self.content_registry.content_hash,
            },
        )


def run(config_path: pathlib.Path) -> int:
    """Boot the trainer service and serve until SIGTERM/SIGINT."""
    config = load_config(config_path)
    data_dir = _resolve_data_dir(config)
    data_dir.mkdir(parents=True, exist_ok=True)

    server = TrainerServer(
        ("127.0.0.1", int(config["port"])),
        TrainerHandler,
        config,
        config_path=config_path,
    )
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
    parser.add_argument("--config", type=pathlib.Path, required=True)
    args = parser.parse_args()
    return run(args.config)


if __name__ == "__main__":
    raise SystemExit(main())

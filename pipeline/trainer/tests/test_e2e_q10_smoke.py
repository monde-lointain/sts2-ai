"""S0.G gate item #2 — end-to-end Q10 smoke test.

Drives the full Q10 in-process training pipeline against a mocked Q3, runs
one step, fires one publish, and verifies the artifact bundle on disk.

Why mock Q3 instead of running it
==================================
Q3 startup + 1k-row ingest + sampling is heavy for a pytest test. Mocking
``DataIngest.get_batch`` keeps the test fast and deterministic. The real
Q3↔Q10 service-up integration is exercised separately via
``pipeline/tests/smoke_services.py`` (gate item #1).

Coverage (per directive §S0.G goal B):
  1. Build all submodules with cadence-1 (every_n_steps=1).
  2. Mock ``DataIngest.get_batch`` to return a deterministic synthetic Batch.
  3. Run TrainDriver for 1 step.
  4. Wait for the publisher daemon to flush.
  5. Verify the bundle:
     - data_dir/runs/<run_id>/checkpoints/<step>/ contains:
       weights.pt, optimizer.pt, model.onnx (+ optional .data sidecar),
       content_registry.json, manifest.json
     - manifest.json validates via ProvenanceManifest.from_dict
     - manifest fields populated per Q10-ADR-006/009.
"""

from __future__ import annotations

import importlib.util
import json
import threading
import time
from pathlib import Path
from unittest import mock

import pytest
import torch

from pipeline.common.trajectory_proto import TrajectoryStep
from pipeline.trainer.artifact_publisher import ArtifactPublisher
from pipeline.trainer.content_registry import ContentRegistry
from pipeline.trainer.data_ingest import Batch, DataIngest
from pipeline.trainer.loss_engine import LossEngine
from pipeline.trainer.manifest import SCHEMA_VERSION, ProvenanceManifest
from pipeline.trainer.metrics_emitter import MetricsEmitter
from pipeline.trainer.model import TrainerNet
from pipeline.trainer.optim import OptimController
from pipeline.trainer.run_config import (
    CheckpointConfig,
    LossWeights,
    NetworkConfig,
    OptimConfig,
    RunConfig,
    RunProvenance,
    WandbConfig,
    seed_everything,
)
from pipeline.trainer.tensor_encoder import EncodedBatch, TensorEncoder
from pipeline.trainer.train_driver import TrainDriver

_REGISTRY_PATH = (
    Path(__file__).resolve().parents[3] / "contracts" / "registry" / "phase1-silent.json"
)

_HAS_ONNX = importlib.util.find_spec("onnx") is not None


# ---------------------------------------------------------------------------
# Fixtures / helpers
# ---------------------------------------------------------------------------
def _small_network_config() -> NetworkConfig:
    """Tiny transformer config — keeps forward+backward < 1s on CPU."""
    return NetworkConfig(
        expected_token_count=None,
        d_model=16,
        n_layers=2,
        n_heads=2,
        ffn_dim=32,
        max_seq_len=16,
        max_action_space=8,
    )


def _make_run_config(network: NetworkConfig) -> RunConfig:
    """Frozen RunConfig with cadence-1 checkpoint + tiny network.

    Built directly without git subprocess so the test is independent of the
    host worktree's dirty state.
    """
    return RunConfig(
        service="trainer",
        port=0,
        data_dir="data/trainer",
        q3_url="http://127.0.0.1:0",
        q5_url="http://127.0.0.1:0",
        parent_artifact_id=None,
        seed=0,
        refuse_on_dirty=False,
        sampling_mode="uniform",
        prefetch_queue_size=2,
        batch_size=2,
        network=network,
        optim=OptimConfig(
            lr=1e-3,
            weight_decay=0.0,
            warmup_steps=1,
            total_steps=10,
            grad_clip=1.0,
        ),
        loss_weights=LossWeights(
            policy=1.0,
            combat_sample=1.0,
            combat_summary=1.0,
            hp_frac_aux=0.05,
            kl_beta=0.01,
        ),
        # every_n_steps=1 → publish after the first step.
        checkpoint=CheckpointConfig(every_n_steps=1, every_m_minutes=60),
        wandb=WandbConfig(enabled=False),
        run_id="01E2ESMOKE0000000000000000",
    )


def _make_run_provenance(cfg: RunConfig) -> RunProvenance:
    """Build RunProvenance without invoking git (test-stable)."""
    return RunProvenance(
        code_sha="0" * 40,
        run_dirty=False,
        start_ts_ns=1_000_000_000,
        host="test-host",
        seed=cfg.seed,
        parent_artifact_id=None,
        run_id=cfg.run_id,
    )


def _make_step(action_id: int = 0) -> TrajectoryStep:
    """Deterministic synthetic TrajectoryStep with non-empty fields."""
    step = TrajectoryStep()
    step.rich_state = bytes(range(action_id, action_id + 8))
    step.legal_action_ids.extend([1, 2, 3, 4])
    step.search_policy.extend([0.4, 0.3, 0.2, 0.1])
    step.action_taken = action_id
    step.reward = 0.0
    step.terminal = False
    step.combat_outcome_summary.survival_probability = 0.5
    step.combat_outcome_summary.expected_hp_delta = -3.0
    step.combat_outcome_summary.expected_turns = 4.0
    step.combat_outcome_summary.timeout_probability = 0.1
    step.combat_outcome_summary.uncertainty = 0.2
    sample = step.combat_outcome_samples.add()
    sample.survived = True
    sample.hp_delta = -3.0
    sample.turns_taken = 4
    sample.timeout = False
    return step


def _make_batch() -> Batch:
    """A deterministic batch with 2 steps (matches cfg.batch_size)."""
    return Batch(
        steps=tuple(_make_step(i) for i in range(2)),
        cursor_token="cursor-0",
        trajectory_ids=(),
    )


def _dummy_batch_provider(registry: ContentRegistry, cfg: RunConfig):
    """Lambda-style provider returning an EncodedBatch for ONNX export dummy input.

    Mirrors ``service.TrainerServer._dummy_batch_provider`` shape.
    """

    def _provider() -> EncodedBatch:
        b = 1
        t = max(1, int(cfg.network.max_seq_len) // 4)
        a = 4
        return EncodedBatch(
            tokens=torch.zeros((b, t), dtype=torch.long),
            padding_mask=torch.zeros((b, t), dtype=torch.bool),
            legal_action_mask=torch.ones((b, a), dtype=torch.bool),
            policy_target=torch.nn.functional.softmax(torch.zeros((b, a)), dim=-1),
            combat_sample_targets=torch.zeros((b, 4)),
            combat_summary_targets=torch.zeros((b, 5)),
            hp_frac_target=torch.zeros((b,)),
            prior_logits=torch.zeros((b, a)),
            macro_context=torch.zeros((b, 11)),
            metadata={"content_registry_sha": registry.content_hash},
        )

    return _provider


@pytest.fixture(scope="module")
def registry() -> ContentRegistry:
    return ContentRegistry.load(_REGISTRY_PATH)


# ---------------------------------------------------------------------------
# End-to-end test
# ---------------------------------------------------------------------------
@pytest.mark.skipif(not _HAS_ONNX, reason="onnx not installed")
def test_e2e_one_step_publish(tmp_path: Path, registry: ContentRegistry) -> None:
    """Drive 1 step through the full Q10 pipeline; verify the published bundle.

    Mocks the network-layer (``DataIngest.get_batch``) only. All other
    Q10 submodules — encoder, model, loss, optim, publisher, train_driver,
    metrics — are the real implementations.
    """
    cfg = _make_run_config(_small_network_config())
    provenance = _make_run_provenance(cfg)
    seed_everything(cfg.seed)

    # Build the real submodule chain (same construction order as
    # ``pipeline/trainer/service.py``).
    model = TrainerNet(cfg.network, registry)
    encoder = TensorEncoder(registry, cfg.network)
    optim = OptimController(model, cfg.optim)
    loss_engine = LossEngine(cfg, model)

    metrics = MetricsEmitter(
        service_name="trainer",
        started_at=time.time(),
        wandb_enabled=False,
    )

    # DataIngest is real — but we mock ``get_batch`` so the test does not
    # need a Q3 service. The ingest also exposes ``snapshot_consumed_ids``
    # which the publisher consults for dataset_sha; the real implementation
    # returns an empty tuple in Phase-1, so we keep it as-is.
    ingest = DataIngest(cfg)

    publisher = ArtifactPublisher(
        cfg,
        provenance,
        registry,
        model_for_onnx=model,
        dummy_batch_provider=_dummy_batch_provider(registry, cfg),
        consumed_ids_provider=ingest.snapshot_consumed_ids,
        # Redirect publishes to tmp_path (no side effects outside).
        data_dir=tmp_path,
    )

    driver = TrainDriver(
        cfg,
        provenance,
        model,
        encoder,
        ingest,
        loss_engine,
        optim,
        publisher,
        metrics,
        device=torch.device("cpu"),
    )

    stop = threading.Event()
    # Patch ``DataIngest.get_batch`` to return one deterministic batch
    # and then None (clean exhaustion → loop exits without further fetches).
    batch_sequence = [_make_batch(), None]
    batch_iter = iter(batch_sequence)

    def _fake_get_batch(*_args, **_kwargs):
        try:
            return next(batch_iter)
        except StopIteration:
            return None

    try:
        with mock.patch.object(ingest, "get_batch", side_effect=_fake_get_batch):
            # Publisher starts BEFORE the driver so a publish request fired
            # mid-step is consumed promptly.
            publisher.start(stop)
            driver.start(stop)
            # Poll until publisher reports an OK or the test times out.
            deadline = time.monotonic() + 30.0
            while time.monotonic() < deadline:
                if publisher.publish_ok_count > 0:
                    break
                if publisher.publish_err_count > 0:
                    raise AssertionError(
                        f"publisher recorded error during smoke: "
                        f"publish_err_count={publisher.publish_err_count}"
                    )
                time.sleep(0.05)
            assert publisher.publish_ok_count >= 1, (
                f"no publish completed within timeout "
                f"(ok={publisher.publish_ok_count}, "
                f"err={publisher.publish_err_count}, "
                f"dropped={publisher.publish_dropped_count}, "
                f"current_step={driver.current_step()})"
            )
    finally:
        stop.set()
        driver.join(timeout=10.0)
        publisher.join(timeout=5.0)
        ingest.join(timeout=2.0)
        metrics.shutdown(timeout=5.0)

    # ------------------------------------------------------------------
    # Verify the published bundle
    # ------------------------------------------------------------------
    last_ref = publisher.last_published()
    assert last_ref is not None, "publisher.last_published() should be set"
    bundle_dir = last_ref.local_path
    assert bundle_dir is not None
    assert bundle_dir.is_dir(), f"bundle dir missing: {bundle_dir}"
    # Path shape: tmp_path/runs/<run_id>/checkpoints/<step>/.
    assert bundle_dir.parent.parent.parent == tmp_path / "runs"
    assert bundle_dir.parent.parent.name == cfg.run_id
    assert bundle_dir.parent.name == "checkpoints"
    assert int(bundle_dir.name) == last_ref.step

    # Required bundle files present and non-empty.
    weights_path = bundle_dir / "weights.pt"
    optim_path = bundle_dir / "optimizer.pt"
    onnx_path = bundle_dir / "model.onnx"
    registry_path = bundle_dir / "content_registry.json"
    manifest_path = bundle_dir / "manifest.json"

    assert weights_path.is_file() and weights_path.stat().st_size > 0
    assert optim_path.is_file() and optim_path.stat().st_size > 0
    assert onnx_path.is_file() and onnx_path.stat().st_size > 0
    assert registry_path.is_file() and registry_path.stat().st_size > 0
    assert manifest_path.is_file() and manifest_path.stat().st_size > 0

    # content_registry.json bytes match registry.bytes_blob (Q10-ADR-008).
    assert registry_path.read_bytes() == registry.bytes_blob

    # No staging-dir remnants from the ONNX export.
    staging_remnants = list(bundle_dir.glob(".onnx_staging.*"))
    assert staging_remnants == [], f"staging remnants: {staging_remnants}"

    # Manifest validates and has every required field populated.
    payload = json.loads(manifest_path.read_text())
    ProvenanceManifest.from_dict(payload)  # raises ValueError if invalid

    assert payload["schema_version"] == SCHEMA_VERSION
    assert payload["phase"] == 1
    assert payload["onnx_opset_version"] == 17
    assert payload["step"] >= 1
    assert payload["created_at_ns"] > 0
    assert payload["host"] == provenance.host
    assert payload["host"]  # non-empty
    assert payload["code_sha"] == provenance.code_sha
    assert payload["seed"] == cfg.seed
    assert payload["run_id"] == cfg.run_id
    assert payload["parent_artifact_id"] is None
    assert payload["content_registry_sha"] == registry.content_hash
    assert isinstance(payload["artifact_id"], str) and payload["artifact_id"]
    # dataset_sha matches sha256 of the empty trajectory-id list (Phase-1
    # stub — see data_ingest module docstring).
    from pipeline.trainer.artifact_publisher import compute_dataset_sha

    assert payload["dataset_sha"] == compute_dataset_sha(())
    assert payload["dataset_size"] == 0
    # hyperparameters includes the runtime loss_total (publisher recorded).
    assert "hyperparameters" in payload
    assert "loss_total" in payload["hyperparameters"]

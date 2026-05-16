"""S0.G gate item #3 — determinism roundtrip.

Two separate runs with the same ``(seed, cursor_token=None,
parent_artifact_id=None)`` and the same deterministic synthetic batch
must produce bit-identical:

  * loss scalar (``loss_result.total.item()``)
  * post-clip gradient norm (``step_stats.grad_norm_post_clip``)
  * model ``state_dict`` after one full forward + backward + optim.step

Why this matters
================
Q10-ADR-003 (provenance reproducibility) requires that identical
``(code_sha, seed, parent_artifact_id, content_registry_sha, hyperparameters)``
re-runs produce bit-identical artifacts. This test gates the seed +
construction-order discipline of every Q10 submodule from the bottom
up — encoder, model init, loss, optim — without bringing in network I/O
or the publisher thread.

The two-run comparison is the simplest sufficient probe; if the post-step
state_dict matches tensor-by-tensor, every upstream computation must
have been deterministic.
"""

from __future__ import annotations

from pathlib import Path

import pytest
import torch

from pipeline.common.trajectory_proto import TrajectoryStep
from pipeline.trainer.content_registry import ContentRegistry
from pipeline.trainer.loss_engine import LossEngine
from pipeline.trainer.model import TrainerNet
from pipeline.trainer.optim import OptimController
from pipeline.trainer.run_config import (
    CheckpointConfig,
    LossWeights,
    NetworkConfig,
    OptimConfig,
    RunConfig,
    WandbConfig,
    seed_everything,
)
from pipeline.trainer.tensor_encoder import TensorEncoder

# ---------------------------------------------------------------------------
# Fixtures / helpers
# ---------------------------------------------------------------------------
_REGISTRY_PATH = (
    Path(__file__).resolve().parents[3] / "contracts" / "registry" / "phase1-silent.json"
)
_SEED: int = 4242


def _small_network_config() -> NetworkConfig:
    """Tiny transformer config that keeps the test under a few seconds on CPU."""
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
    """Frozen RunConfig with deterministic seed + tiny network.

    No I/O paths or git subprocess — built directly so the test does not
    depend on the host working tree (mirrors ``test_data_ingest._make_config``).
    """
    return RunConfig(
        service="trainer",
        port=0,
        data_dir="data/trainer",
        q3_url="http://127.0.0.1:0",
        q5_url="http://127.0.0.1:0",
        parent_artifact_id=None,
        seed=_SEED,
        refuse_on_dirty=False,
        sampling_mode="uniform",
        prefetch_queue_size=2,
        batch_size=2,
        network=network,
        optim=OptimConfig(
            lr=1e-3,
            weight_decay=0.0,
            warmup_steps=2,
            total_steps=100,
            grad_clip=1.0,
        ),
        loss_weights=LossWeights(
            policy=1.0,
            combat_sample=1.0,
            combat_summary=1.0,
            hp_frac_aux=0.05,
            kl_beta=0.01,
        ),
        checkpoint=CheckpointConfig(every_n_steps=10, every_m_minutes=1),
        wandb=WandbConfig(enabled=False),
        run_id="01DETERMINISM0000000000000",
    )


def _make_step(action_id: int = 0) -> TrajectoryStep:
    """Deterministic synthetic TrajectoryStep.

    ``rich_state`` is a fixed byte sequence so token encoding is identical
    across runs. ``legal_action_ids`` + ``search_policy`` populate the
    policy target deterministically.
    """
    step = TrajectoryStep()
    # Fixed-length deterministic rich_state. Length < max_seq_len so the
    # encoder doesn't trip on truncation behavior.
    step.rich_state = bytes(range(action_id, action_id + 8))
    step.legal_action_ids.extend([1, 2, 3, 4])
    step.search_policy.extend([0.4, 0.3, 0.2, 0.1])
    step.action_taken = action_id
    step.reward = 0.0
    step.terminal = False
    # Combat outcome summary + degenerate single sample (Phase-1 / ADR-021).
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


def _make_batch_steps(n: int = 4) -> list[TrajectoryStep]:
    """A deterministic list of N trajectory steps."""
    return [_make_step(action_id=i) for i in range(n)]


def _run_one_step(cfg: RunConfig, registry: ContentRegistry, steps: list[TrajectoryStep]) -> dict:
    """Construct fresh submodules under a fixed seed and run one full step.

    Returns a dict carrying the scalars + state_dict needed for the
    post-step equality assertions. Each call seeds the global RNG before
    constructing the network so weight initialization is bit-identical.
    """
    seed_everything(cfg.seed)
    # Defensive: torch.manual_seed is also re-set in case seed_everything's
    # import of the optional torch shim skips it on some path.
    torch.manual_seed(cfg.seed)

    net = TrainerNet(cfg.network, registry)
    net.train()
    encoder = TensorEncoder(registry, cfg.network)
    loss_engine = LossEngine(cfg, net)
    optim = OptimController(net, cfg.optim)

    encoded = encoder.encode_batch(steps)
    model_output = net.forward(encoded)
    loss_result = loss_engine.compute(model_output, encoded)
    step_stats = optim.step(loss_result.total)

    # Snapshot the post-step state_dict to CPU (defensive — already CPU,
    # but ``.cpu()`` is a no-op then) for tensor-equality comparison.
    state_dict = {k: v.detach().cpu().clone() for k, v in net.state_dict().items()}
    return {
        "loss_total": float(loss_result.total.detach().item()),
        "grad_norm_post_clip": float(step_stats.grad_norm_post_clip),
        "lr": float(step_stats.lr),
        "state_dict": state_dict,
    }


@pytest.fixture(scope="module")
def registry() -> ContentRegistry:
    return ContentRegistry.load(_REGISTRY_PATH)


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------
def test_seeded_step_loss_and_grad_norm_bit_identical(
    registry: ContentRegistry,
) -> None:
    """Same seed + same batch → bit-identical loss scalar + post-clip grad norm."""
    cfg = _make_run_config(_small_network_config())
    steps = _make_batch_steps(n=4)

    run_a = _run_one_step(cfg, registry, steps)
    run_b = _run_one_step(cfg, registry, steps)

    # Exact equality (no atol). If determinism is broken these will differ.
    assert run_a["loss_total"] == run_b["loss_total"], (
        f"loss diverged: {run_a['loss_total']!r} vs {run_b['loss_total']!r}"
    )
    assert run_a["grad_norm_post_clip"] == run_b["grad_norm_post_clip"], (
        f"grad_norm_post_clip diverged: "
        f"{run_a['grad_norm_post_clip']!r} vs {run_b['grad_norm_post_clip']!r}"
    )
    assert run_a["lr"] == run_b["lr"], f"lr diverged: {run_a['lr']!r} vs {run_b['lr']!r}"


def test_seeded_state_dict_bit_identical(registry: ContentRegistry) -> None:
    """Same seed → bit-identical model ``state_dict`` after 1 training step.

    Iterates every (key, tensor) pair in the state_dict and asserts
    ``torch.equal`` (bit-identical). This is the strongest deterministic-
    construction probe we have: if every weight, every bias, and every
    transformer-layer normalization tensor match exactly, every upstream
    op was deterministic.
    """
    cfg = _make_run_config(_small_network_config())
    steps = _make_batch_steps(n=4)

    run_a = _run_one_step(cfg, registry, steps)
    run_b = _run_one_step(cfg, registry, steps)

    sd_a = run_a["state_dict"]
    sd_b = run_b["state_dict"]
    assert set(sd_a.keys()) == set(sd_b.keys()), "state_dict keys diverged across runs"
    for key in sd_a:
        ta, tb = sd_a[key], sd_b[key]
        assert ta.shape == tb.shape, f"{key}: shape diverged {ta.shape} vs {tb.shape}"
        assert ta.dtype == tb.dtype, f"{key}: dtype diverged {ta.dtype} vs {tb.dtype}"
        assert torch.equal(ta, tb), (
            f"{key}: tensors not bit-identical (max abs diff = {(ta - tb).abs().max().item()})"
        )

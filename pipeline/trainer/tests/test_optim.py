"""Tests for ``pipeline.trainer.optim`` (S0.D.α).

Covers the 5 unit tests + 1 integration test called out in
``pipeline/trainer/docs/specs/modules/optim.md`` §Testing Strategy.
"""

from __future__ import annotations

import copy
from pathlib import Path

import pytest
import torch
from torch import nn

from pipeline.trainer.optim import OptimController, StepStats, _cosine_warmup_factor
from pipeline.trainer.run_config import OptimConfig, RunConfig


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def _load_cfg() -> RunConfig:
    cfg_path = Path(__file__).resolve().parents[1] / "config" / "local.json"
    return RunConfig.load(cfg_path)


def _make_optim_cfg(
    *,
    lr: float = 1e-3,
    weight_decay: float = 0.0,
    warmup_steps: int = 100,
    total_steps: int = 1000,
    grad_clip: float = 1.0,
) -> OptimConfig:
    return OptimConfig(
        lr=lr,
        weight_decay=weight_decay,
        warmup_steps=warmup_steps,
        total_steps=total_steps,
        grad_clip=grad_clip,
    )


def _tiny_net(seed: int = 0) -> nn.Module:
    """Tiny deterministic linear model used across tests."""
    torch.manual_seed(seed)
    return nn.Linear(4, 2)


def _quadratic_loss(net: nn.Linear) -> torch.Tensor:
    """Sum-of-squares of all params → finite grads, simple convex landscape."""
    total = torch.zeros((), dtype=torch.float32)
    for p in net.parameters():
        total = total + p.pow(2).sum()
    return total


# ---------------------------------------------------------------------------
# Test 1: LR schedule reaches warmup peak then decays.
# ---------------------------------------------------------------------------
def test_lr_schedule_peaks_then_decays() -> None:
    base_lr = 1e-3
    warmup = 100
    total = 1000
    net = _tiny_net()
    cfg = _make_optim_cfg(lr=base_lr, warmup_steps=warmup, total_steps=total, grad_clip=1e9)
    opt = OptimController(net, cfg)

    lrs: list[float] = []
    for _ in range(total):
        # Use a tiny synthetic loss so we don't move the schedule prematurely.
        loss = _quadratic_loss(net)
        stats = opt.step(loss)
        lrs.append(stats.lr)

    # After warmup (step == warmup_steps), LR equals the peak (base_lr) up
    # to floating-point tolerance. ``step()`` is called *after* backward, so
    # lrs[i] is the LR for "step i+1".
    peak_lr = lrs[warmup - 1]
    assert peak_lr == pytest.approx(base_lr, rel=1e-6), peak_lr

    # By the end of the schedule, LR is at the floor (≤ base_lr * 1e-3).
    end_lr = lrs[-1]
    assert end_lr <= base_lr * 1e-3, end_lr
    assert end_lr >= 0.0, end_lr


def test_cosine_warmup_factor_pure_form() -> None:
    """The schedule lambda alone (no optimizer) gives the documented shape."""
    assert _cosine_warmup_factor(0, 100, 1000) == 0.0
    assert _cosine_warmup_factor(50, 100, 1000) == pytest.approx(0.5)
    assert _cosine_warmup_factor(100, 100, 1000) == pytest.approx(1.0)
    # Midpoint of cosine decay: progress=0.5 → 0.5*(1+cos(pi/2)) = 0.5
    mid = _cosine_warmup_factor(550, 100, 1000)
    assert mid == pytest.approx(0.5, rel=1e-6)
    assert _cosine_warmup_factor(1000, 100, 1000) == 0.0
    assert _cosine_warmup_factor(1500, 100, 1000) == 0.0


# ---------------------------------------------------------------------------
# Test 2: Gradient clipping caps at configured norm.
# ---------------------------------------------------------------------------
def test_grad_clip_caps_norm_and_increments_counter() -> None:
    torch.manual_seed(0)
    # Single linear param, no bias, easy to seed with a known grad magnitude.
    layer = nn.Linear(4, 4, bias=False)
    cfg = _make_optim_cfg(grad_clip=1.0)
    opt = OptimController(layer, cfg)

    # Construct a loss whose ‖∇‖ ≈ 10 deterministically: set weight to known
    # value and pick a target so that backward yields a >> 1 norm grad.
    # We do this by scaling the loss by 100.
    x = torch.randn(8, 4)
    y = torch.randn(8, 4)
    loss = ((layer(x) - y) ** 2).mean() * 100.0

    assert opt.clip_fired_total == 0
    stats = opt.step(loss)

    # The pre-clip norm should exceed grad_clip; post-clip ≤ grad_clip with
    # numerical tolerance (clip_grad_norm_ scales to exactly max_norm).
    assert stats.grad_norm_pre_clip > 1.0
    assert stats.grad_norm_post_clip <= 1.0 + 1e-5
    assert opt.clip_fired_total == 1


def test_grad_clip_skips_counter_when_norm_under_threshold() -> None:
    torch.manual_seed(0)
    layer = nn.Linear(4, 2, bias=False)
    cfg = _make_optim_cfg(grad_clip=1e6)  # giant threshold → never fires
    opt = OptimController(layer, cfg)

    x = torch.randn(8, 4)
    y = torch.randn(8, 2)
    loss = ((layer(x) - y) ** 2).mean()
    opt.step(loss)
    assert opt.clip_fired_total == 0


# ---------------------------------------------------------------------------
# Test 3: state_dict round-trip preserves momentum.
# ---------------------------------------------------------------------------
def test_state_dict_round_trip_preserves_step() -> None:
    torch.manual_seed(42)
    net_a = nn.Linear(3, 2)
    cfg = _make_optim_cfg(lr=1e-2, warmup_steps=5, total_steps=100, grad_clip=1e9)
    opt_a = OptimController(net_a, cfg)

    # Drive 10 steps on net_a with deterministic inputs.
    torch.manual_seed(7)
    inputs_targets = [(torch.randn(4, 3), torch.randn(4, 2)) for _ in range(11)]
    for x, y in inputs_targets[:10]:
        loss = ((net_a(x) - y) ** 2).mean()
        opt_a.step(loss)

    # Snapshot the model + optimizer state. ``optimizer.state_dict()``
    # returns references to live tensors (documented PyTorch behavior);
    # deepcopy is required so subsequent training on net_a doesn't mutate
    # the snapshot. In production, ``artifact_publisher`` serializes the
    # state_dict to disk, which performs the equivalent of deepcopy.
    sd_opt = copy.deepcopy(opt_a.state_dict())
    sd_net = {k: v.detach().clone() for k, v in net_a.state_dict().items()}

    # Continue net_a for one more step → reference params.
    x_ref, y_ref = inputs_targets[10]
    loss_ref = ((net_a(x_ref) - y_ref) ** 2).mean()
    opt_a.step(loss_ref)
    ref_params = {k: v.detach().clone() for k, v in net_a.state_dict().items()}

    # Build a fresh net_b with the snapshot weights, fresh OptimController,
    # load the optim state_dict, and run the same 11th step.
    net_b = nn.Linear(3, 2)
    net_b.load_state_dict(sd_net)
    opt_b = OptimController(net_b, cfg)
    opt_b.load_state_dict(sd_opt)
    loss_b = ((net_b(x_ref) - y_ref) ** 2).mean()
    opt_b.step(loss_b)

    # Bit-equal: each param tensor should match exactly.
    for k in ref_params:
        assert torch.equal(net_b.state_dict()[k], ref_params[k]), (
            f"param {k} diverged after state_dict round-trip"
        )


# ---------------------------------------------------------------------------
# Test 4: set_requires_grad freezes a parameter group.
# ---------------------------------------------------------------------------
def test_set_requires_grad_freezes_named_group() -> None:
    torch.manual_seed(0)
    # Two-layer model: first layer is the "policy_head" group we'll toggle.
    head = nn.Linear(4, 2)
    trunk = nn.Linear(2, 1)
    model = nn.Sequential(head, trunk)

    cfg = _make_optim_cfg(grad_clip=1e9)
    opt = OptimController(model, cfg)
    opt.register_param_group("policy_head", list(head.parameters()))

    # Toggle off → grads at head should be zero after backward + step().
    opt.set_requires_grad("policy_head", False)
    # All head params now have requires_grad=False.
    for p in head.parameters():
        assert p.requires_grad is False

    x = torch.randn(8, 4)
    y = torch.randn(8, 1)
    loss = ((model(x) - y) ** 2).mean()
    # We need to read .grad before step's zero_grad. Inspect via a copy of
    # the controller's flow: backward directly, check grad, then run step
    # (which clip/step/zero_grads). Simpler: call backward outside step
    # and then verify; but the public API is step(). So we use a hook:
    # bypass step's zero_grad by checking gradients during a manual run.
    loss.backward()
    for p in head.parameters():
        # frozen → no grad accumulated
        assert p.grad is None or torch.equal(p.grad, torch.zeros_like(p)), (
            "frozen policy_head accumulated a non-zero grad"
        )
    # Trunk should still receive grad.
    for p in trunk.parameters():
        assert p.grad is not None and p.grad.abs().sum() > 0

    # Toggle back on → grads flow again.
    # Clear any stale grads from the prior backward, since requires_grad
    # toggles do not erase existing .grad tensors.
    for p in model.parameters():
        p.grad = None
    opt.set_requires_grad("policy_head", True)
    for p in head.parameters():
        assert p.requires_grad is True

    loss2 = ((model(x) - y) ** 2).mean()
    loss2.backward()
    for p in head.parameters():
        assert p.grad is not None and p.grad.abs().sum() > 0


# ---------------------------------------------------------------------------
# Test 5: zero_grad(set_to_none=True) releases gradient buffers.
# ---------------------------------------------------------------------------
def test_zero_grad_releases_buffers_to_none() -> None:
    torch.manual_seed(0)
    net = _tiny_net()
    cfg = _make_optim_cfg(grad_clip=1e9)
    opt = OptimController(net, cfg)

    x = torch.randn(8, 4)
    y = torch.randn(8, 2)
    loss = ((net(x) - y) ** 2).mean()
    opt.step(loss)

    for p in net.parameters():
        assert p.grad is None, "step() must zero_grad(set_to_none=True)"


# ---------------------------------------------------------------------------
# Test 6 (integration): toy convergence on synthetic quadratic loss.
# ---------------------------------------------------------------------------
def test_toy_convergence_quadratic() -> None:
    """200 steps reduce a quadratic loss by ≥3 orders of magnitude.

    Spec §Integration test 1. AdamW with cosine-warmup wired correctly.
    A modest starting magnitude + a higher learning rate suffices on
    the simple bowl; the schedule's decay still leaves enough late-step
    LR for the polish required to push under the 1e-3 ratio.
    """
    torch.manual_seed(123)
    # Parameter is the variable; "target" is fixed; quadratic in param.
    param = nn.Parameter(torch.randn(8) * 2.0)
    target = torch.zeros(8)

    class _Wrap(nn.Module):
        def __init__(self, p: nn.Parameter) -> None:
            super().__init__()
            self.p = p

    net = _Wrap(param)
    cfg = _make_optim_cfg(lr=3e-1, warmup_steps=10, total_steps=200, grad_clip=1e9)
    opt = OptimController(net, cfg)

    initial_loss = float(((param - target) ** 2).sum().item())
    # Measure the loss AFTER the final step, not before.
    for _ in range(200):
        loss = ((param - target) ** 2).sum()
        opt.step(loss)
    final_loss = float(((param - target) ** 2).sum().item())

    assert final_loss <= initial_loss * 1e-3, (
        f"loss did not converge: initial={initial_loss}, final={final_loss}"
    )


# ---------------------------------------------------------------------------
# Bonus: momentum_norm is non-zero after first step.
# ---------------------------------------------------------------------------
def test_momentum_norm_populated_after_first_step() -> None:
    """``exp_avg`` is lazily created by AdamW; momentum_norm > 0 after step 1."""
    torch.manual_seed(0)
    net = _tiny_net()
    cfg = _make_optim_cfg(grad_clip=1e9)
    opt = OptimController(net, cfg)

    x = torch.randn(8, 4)
    y = torch.randn(8, 2)
    loss = ((net(x) - y) ** 2).mean()
    stats = opt.step(loss)
    assert stats.momentum_norm > 0.0
    assert stats.weight_norm > 0.0


# ---------------------------------------------------------------------------
# Bonus: smoke that local.json config wires through cleanly.
# ---------------------------------------------------------------------------
def test_constructs_from_run_config() -> None:
    cfg = _load_cfg()
    net = nn.Linear(10, 1)
    opt = OptimController(net, cfg.optim)
    x = torch.randn(4, 10)
    y = torch.randn(4, 1)
    loss = ((net(x) - y) ** 2).mean()
    stats = opt.step(loss)
    assert isinstance(stats, StepStats)
    assert stats.lr > 0.0


# ---------------------------------------------------------------------------
# Bonus: pcgrad_wrap flag round-trip (Phase-4 readiness)
# ---------------------------------------------------------------------------
def test_pcgrad_wrap_flag_is_no_op_phase1() -> None:
    torch.manual_seed(0)
    net = _tiny_net()
    cfg = _make_optim_cfg(grad_clip=1e9)
    opt = OptimController(net, cfg)
    assert opt.pcgrad_active is False
    opt.pcgrad_wrap(True)
    assert opt.pcgrad_active is True
    # Step still works identically.
    loss = _quadratic_loss(net)
    stats = opt.step(loss)
    assert isinstance(stats, StepStats)

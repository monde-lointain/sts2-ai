"""Optim submodule: AdamW + cosine-with-warmup LR + global-norm clipping.

Per ``pipeline/trainer/docs/specs/modules/optim.md``. Phase-1 wires a
single parameter group; Phase-2+ freeze/unfreeze toggles ``requires_grad``
on per-head groups via :meth:`OptimController.set_requires_grad`.
Phase-4 swaps the single-loss backward for a per-head PCGrad projection
via :meth:`OptimController.pcgrad_wrap` (Phase-1 default: off — no-op).

Owns no I/O. ``state_dict`` / ``load_state_dict`` shuttle optimizer +
scheduler state through ``artifact_publisher`` for atomic checkpointing.
"""
from __future__ import annotations

import math
from dataclasses import dataclass

import torch
from torch import nn

from pipeline.trainer.run_config import OptimConfig


# ---------------------------------------------------------------------------
# StepStats
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class StepStats:
    """Per-step optimizer telemetry; produced by :meth:`OptimController.step`.

    Attributes
    ----------
    grad_norm_pre_clip:
        Global ℓ₂ norm of parameter gradients **before**
        :func:`torch.nn.utils.clip_grad_norm_`. Useful for deciding
        whether the clip threshold is right-sized (paired with
        ``clip_fired_total``).
    grad_norm_post_clip:
        Global ℓ₂ norm **after** clipping. Bounded by
        ``config.grad_clip`` modulo floating-point tolerance.
    lr:
        Current optimizer learning rate (post-``scheduler.step()``).
    weight_norm:
        Global ℓ₂ norm of all parameters; ``√Σ ‖p‖²``.
    momentum_norm:
        Sum of ``‖exp_avg‖²`` across populated AdamW first-moment
        buffers. Zero until the first ``optimizer.step()`` has run (the
        ``exp_avg`` state is lazily created by AdamW). Reported as the
        un-square-rooted sum-of-squares for cheaper aggregation across
        groups; consumers that want ``‖m‖₂`` should take ``sqrt``.
    """

    grad_norm_pre_clip: float
    grad_norm_post_clip: float
    lr: float
    weight_norm: float
    momentum_norm: float


# ---------------------------------------------------------------------------
# Cosine-with-warmup factor
# ---------------------------------------------------------------------------
def _cosine_warmup_factor(
    step: int, warmup_steps: int, total_steps: int
) -> float:
    """Return the LR multiplier in [0, 1] for the LambdaLR schedule.

    - ``step ≤ warmup_steps``: linear ``step / warmup_steps`` (the
      ``step == 0`` case yields ``0``; first ``scheduler.step()`` moves
      it to ``1 / warmup_steps`` — standard LambdaLR convention).
    - ``warmup_steps < step ≤ total_steps``: cosine from 1 to 0 with
      progress in ``(0, 1]``.
    - ``step > total_steps``: clamped at 0 (floor).
    - Degenerate guard: ``warmup_steps <= 0`` skips warmup; ``total_steps
      <= warmup_steps`` collapses cosine to the floor immediately.
    """
    if warmup_steps > 0 and step <= warmup_steps:
        return float(step) / float(warmup_steps)
    if step >= total_steps:
        return 0.0
    decay_span = total_steps - max(warmup_steps, 0)
    if decay_span <= 0:
        return 0.0
    progress = (step - max(warmup_steps, 0)) / decay_span
    return 0.5 * (1.0 + math.cos(math.pi * progress))


# ---------------------------------------------------------------------------
# OptimController
# ---------------------------------------------------------------------------
class OptimController:
    """AdamW optimizer + cosine-warmup LR scheduler + grad clipping.

    Phase-1 single parameter group over ``net.parameters()``. Phase-2+
    registers named sub-groups (e.g. ``"policy_head"``,
    ``"combat_summary_head"``) and toggles ``requires_grad`` on their
    parameters via :meth:`set_requires_grad`. Phase-1 stores the
    requested per-group state on a map but does not yet route
    parameters to groups; the only effect of ``set_requires_grad`` in
    Phase-1 is on subsequently-registered groups (see
    :meth:`register_param_group`).

    Phase-4 PCGrad: :meth:`pcgrad_wrap` flips an internal flag;
    :meth:`step` is unchanged in Phase-1 (single loss). The PCGrad
    implementation will replace ``loss.backward()`` with a per-head
    backward + gradient projection using ``loss_engine``'s
    ``gradient_diagnostics``; the rest of :meth:`step` (clip / step /
    scheduler / zero_grad) is unchanged.

    Parameters
    ----------
    net:
        :class:`torch.nn.Module` whose ``parameters()`` enter AdamW.
    config:
        :class:`OptimConfig` — ``lr``, ``weight_decay``,
        ``warmup_steps``, ``total_steps``, ``grad_clip``.
    """

    def __init__(self, net: nn.Module, config: OptimConfig) -> None:
        self._net = net
        self._config = config
        self._params: list[torch.nn.Parameter] = list(net.parameters())
        self.optimizer = torch.optim.AdamW(
            self._params,
            lr=config.lr,
            weight_decay=config.weight_decay,
            betas=(0.9, 0.999),
            eps=1e-8,
        )
        warmup = max(0, config.warmup_steps)
        total = max(1, config.total_steps)
        self.scheduler = torch.optim.lr_scheduler.LambdaLR(
            self.optimizer,
            lr_lambda=lambda step: _cosine_warmup_factor(step, warmup, total),
        )
        self._grad_clip: float = float(config.grad_clip)
        self._clip_fired_total: int = 0
        self._pcgrad_active: bool = False
        # Phase-1: single-group registry. Phase-2+ populates with
        # named sub-groups whose ``requires_grad`` is toggled via
        # :meth:`set_requires_grad`. Phase-1 stores intent only.
        self._group_requires_grad: dict[str, bool] = {}
        self._group_params: dict[str, list[torch.nn.Parameter]] = {}

    # -------- step ------------------------------------------------------
    def step(self, loss: torch.Tensor) -> StepStats:
        """Backward, clip, step, schedule, zero_grad → :class:`StepStats`.

        Phase-1 (single loss): ``loss.backward()`` then clip + step.
        Phase-4 (PCGrad): replace the single backward with a per-head
        backward + projection driven by ``loss_engine``'s
        ``gradient_diagnostics``. The remainder of this routine
        (clip / optimizer.step / scheduler.step / zero_grad) is unchanged.
        """
        loss.backward()

        grad_norm_pre = _global_grad_norm(self._params)
        # ``clip_grad_norm_`` returns the pre-clip total norm; we also
        # compute it explicitly for clarity (cheap; both compute identical).
        torch.nn.utils.clip_grad_norm_(self._params, max_norm=self._grad_clip)
        if grad_norm_pre > self._grad_clip:
            self._clip_fired_total += 1
        grad_norm_post = _global_grad_norm(self._params)

        self.optimizer.step()
        self.scheduler.step()
        self.optimizer.zero_grad(set_to_none=True)

        weight_norm = _global_weight_norm(self._params)
        momentum_norm = self._momentum_norm()
        current_lr = float(self.optimizer.param_groups[0]["lr"])

        return StepStats(
            grad_norm_pre_clip=float(grad_norm_pre),
            grad_norm_post_clip=float(grad_norm_post),
            lr=current_lr,
            weight_norm=float(weight_norm),
            momentum_norm=float(momentum_norm),
        )

    # -------- checkpoint ------------------------------------------------
    def state_dict(self) -> dict:
        """Bundle optimizer + scheduler state for ``artifact_publisher``."""
        return {
            "optimizer": self.optimizer.state_dict(),
            "scheduler": self.scheduler.state_dict(),
        }

    def load_state_dict(self, sd: dict) -> None:
        """Restore optimizer + scheduler state from a prior :meth:`state_dict`."""
        self.optimizer.load_state_dict(sd["optimizer"])
        self.scheduler.load_state_dict(sd["scheduler"])

    # -------- phase-2 freeze-unfreeze hook ------------------------------
    def register_param_group(
        self, group_name: str, params: list[torch.nn.Parameter]
    ) -> None:
        """Phase-2+ helper: associate ``group_name`` with ``params``.

        Phase-1 callers do not need this (a single implicit group covers
        ``net.parameters()``). Phase-2 uses it to wire per-head
        freeze/unfreeze; ``set_requires_grad`` then flips
        ``requires_grad`` on the listed params.
        """
        self._group_params[group_name] = list(params)
        # default newly-registered group to enabled
        self._group_requires_grad.setdefault(group_name, True)

    def set_requires_grad(self, group_name: str, value: bool) -> None:
        """Phase-2+ hook: toggle a named parameter group's ``requires_grad``.

        Phase-1 default: store the requested state on a per-group map
        but no-op on parameters unless the group has been registered
        via :meth:`register_param_group`. When registered, this iterates
        the named params for that group and toggles ``requires_grad``.
        """
        self._group_requires_grad[group_name] = bool(value)
        for p in self._group_params.get(group_name, []):
            p.requires_grad = bool(value)

    # -------- phase-4 pcgrad hook ---------------------------------------
    def pcgrad_wrap(self, active: bool) -> None:
        """Phase-4 hook: enable per-head gradient projection.

        Phase-1 default: off; no behavior change. The flag is stored
        and exposed via :attr:`pcgrad_active` so future PCGrad wiring
        is a single-line swap in :meth:`step`.
        """
        self._pcgrad_active = bool(active)

    @property
    def pcgrad_active(self) -> bool:
        """Whether PCGrad is currently armed (Phase-4 readout)."""
        return self._pcgrad_active

    @property
    def clip_fired_total(self) -> int:
        """Running counter of steps whose pre-clip grad-norm exceeded ``grad_clip``."""
        return self._clip_fired_total

    # -------- internals -------------------------------------------------
    def _momentum_norm(self) -> float:
        """Sum of ``‖exp_avg‖²`` across all populated AdamW first-moment buffers.

        AdamW lazily creates ``exp_avg`` on the first ``step()``; on a
        fresh controller pre-step this is zero by design (the state
        ``dict`` is empty). After the first step, every parameter that
        received a gradient has a corresponding ``exp_avg`` tensor.
        """
        total = 0.0
        for p in self._params:
            state = self.optimizer.state.get(p, None)
            if state is None:
                continue
            exp_avg = state.get("exp_avg", None)
            if exp_avg is None:
                continue
            total += float(exp_avg.detach().pow(2).sum().item())
        return total


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def _global_grad_norm(params: list[torch.nn.Parameter]) -> float:
    """Compute ``√Σ ‖g‖²`` across all params with a populated ``.grad``."""
    sq_sum = 0.0
    for p in params:
        if p.grad is None:
            continue
        sq_sum += float(p.grad.detach().pow(2).sum().item())
    return math.sqrt(sq_sum)


def _global_weight_norm(params: list[torch.nn.Parameter]) -> float:
    """Compute ``√Σ ‖p‖²`` across all params (regardless of grad)."""
    sq_sum = 0.0
    for p in params:
        sq_sum += float(p.detach().pow(2).sum().item())
    return math.sqrt(sq_sum)


__all__ = ["OptimController", "StepStats"]

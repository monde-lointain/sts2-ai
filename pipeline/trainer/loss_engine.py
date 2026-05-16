"""Loss-engine submodule: multi-head loss assembly.

Per ``pipeline/trainer/docs/specs/modules/loss-engine.md``. Phase-1 heads:

- ``policy``         — masked cross-entropy of ``policy_logits`` against
                       ``policy_target`` (illegal actions → ``-inf`` pre-softmax).
- ``combat_sample``  — Phase-1 degenerate-single MSE per ADR-021.
- ``combat_summary`` — 5-component multi-loss (BCE-with-logits for two
                       probability fields, MSE for the remaining three).
- ``hp_frac_aux``    — scalar MSE; low weight (default 0.05).
- ``kl_vs_prior``    — masked KL of current softmax against
                       ``model.compute_prior_logits``-derived prior.

The engine is a pure function modulo a head registry: same
``ModelOutput`` + ``EncodedBatch`` → same ``LossResult`` modulo PyTorch
RNG (which the training loop keeps deterministic).

Gradient diagnostics
====================
Phase-1 default: ``LossResult.gradient_diagnostics`` is an empty dict —
no extra cost. Phase-4 PCGrad consumer flips ``enable_pcgrad_diag`` on
the engine; ``compute`` then populates per-head ``‖∇‖₂`` for the
parameter set tied to each head's loss output (here we report the
gradient norm of each head loss w.r.t. its required-grad input tensors).
"""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass, field

import torch
import torch.nn.functional as F

from pipeline.trainer.run_config import RunConfig
from pipeline.trainer.tensor_encoder import EncodedBatch
from pipeline.trainer.types import ModelOutput


# ---------------------------------------------------------------------------
# Result dataclass
# ---------------------------------------------------------------------------
@dataclass
class LossResult:
    """Bundle returned by :meth:`LossEngine.compute`.

    Attributes
    ----------
    total:
        Backward-able scalar tensor — ``Σ weight_i × loss_i``.
    components:
        Detached per-head scalar loss values for Q7 metrics.
    weights:
        Snapshot of the current head weights (Phase-2+ schedulers read
        these via the engine and write back into its registry).
    gradient_diagnostics:
        Per-head ``‖∇‖₂`` of the head loss with respect to its
        required-grad model-output tensors. Empty by default; populated
        only when ``LossEngine.enable_pcgrad_diag`` is True (Phase-4
        PCGrad seam).
    """

    total: torch.Tensor
    components: dict[str, float]
    weights: dict[str, float]
    gradient_diagnostics: dict[str, float] = field(default_factory=dict)


# ---------------------------------------------------------------------------
# Engine
# ---------------------------------------------------------------------------
HeadFn = Callable[[ModelOutput, EncodedBatch], torch.Tensor]


class LossEngine:
    """Head-loss registry + multi-head assembly.

    Constructed once per run with the live model (for KL-prior
    queries). Phase-1 heads register at construction; Phase-2+ may
    ``register_head`` additional entries.

    Parameters
    ----------
    config:
        :class:`RunConfig` — :pyattr:`loss_weights` supplies default
        head weights.
    model:
        Duck-typed object exposing ``compute_prior_logits(encoded_batch)
        -> torch.Tensor`` of shape ``(B, A)`` matching
        ``encoded_batch.policy_target``. The contract: returns the prior
        snapshot's raw (unmasked) logits — masking is the engine's job.
        Returning ``zeros_like(...)`` on the first call (before any
        ``snapshot_prior``) is acceptable; ``log_softmax`` of zeros over
        the legal slots yields a uniform prior and a finite KL.

    Attributes
    ----------
    weights:
        Mutable ``dict[str, float]`` keyed by head name. Phase-2 weight
        schedulers may rewrite values between :meth:`compute` calls;
        the engine reads it fresh per call.
    enable_pcgrad_diag:
        Default False. Set True to populate
        ``LossResult.gradient_diagnostics`` per call.
    """

    def __init__(self, config: RunConfig, model: object) -> None:
        self._config = config
        self._model = model
        self._fns: dict[str, HeadFn] = {}
        self.weights: dict[str, float] = {}
        self.enable_pcgrad_diag: bool = False
        self._register_phase1_heads()

    # -------- registry ---------------------------------------------------
    def register_head(self, name: str, fn: HeadFn, weight: float) -> None:
        """Add or replace a head's loss function and weight."""
        self._fns[name] = fn
        self.weights[name] = float(weight)

    # -------- compute ---------------------------------------------------
    def compute(self, model_output: ModelOutput, encoded_batch: EncodedBatch) -> LossResult:
        """Run every registered head, weight-sum into a backward-able scalar."""
        components: dict[str, float] = {}
        weights_snapshot: dict[str, float] = dict(self.weights)
        per_head_loss: dict[str, torch.Tensor] = {}

        total: torch.Tensor | None = None
        for name, fn in self._fns.items():
            loss = fn(model_output, encoded_batch)
            per_head_loss[name] = loss
            components[name] = float(loss.detach().item())
            weight = float(self.weights.get(name, 0.0))
            contribution = weight * loss
            total = contribution if total is None else total + contribution

        if total is None:
            # No registered heads — return a zero scalar that still
            # supports backward (a tensor connected to the graph isn't
            # available; return a constant zero).
            total = torch.zeros((), dtype=torch.float32)

        gradient_diagnostics: dict[str, float] = {}
        if self.enable_pcgrad_diag:
            gradient_diagnostics = self._compute_head_grad_norms(per_head_loss, model_output)

        return LossResult(
            total=total,
            components=components,
            weights=weights_snapshot,
            gradient_diagnostics=gradient_diagnostics,
        )

    # -------- internals --------------------------------------------------
    def _register_phase1_heads(self) -> None:
        lw = self._config.loss_weights
        self.register_head("policy", _policy_ce_loss, lw.policy)
        self.register_head("combat_sample", _combat_sample_loss, lw.combat_sample)
        self.register_head("combat_summary", _combat_summary_loss, lw.combat_summary)
        self.register_head("hp_frac_aux", _hp_frac_aux_loss, lw.hp_frac_aux)
        # KL closure captures the live model so we don't re-look it up
        # per call.
        model = self._model

        def kl_head(mo: ModelOutput, eb: EncodedBatch) -> torch.Tensor:
            return _kl_vs_prior_loss(mo, eb, model)

        self.register_head("kl_vs_prior", kl_head, lw.kl_beta)

    def _compute_head_grad_norms(
        self,
        per_head_loss: dict[str, torch.Tensor],
        model_output: ModelOutput,
    ) -> dict[str, float]:
        """Per-head ``‖∇‖₂`` w.r.t. require_grad model-output tensors.

        Phase-4 PCGrad consumer reads this to project conflicting head
        gradients. We compute ``autograd.grad(loss, grad_inputs,
        retain_graph=True, allow_unused=True)`` where ``grad_inputs``
        is the set of ``ModelOutput`` tensors that ``requires_grad``.
        ``None`` (unused) entries contribute zero to the norm.
        """
        grad_inputs: list[torch.Tensor] = [
            t
            for t in (
                model_output.policy_logits,
                model_output.sample_preds,
                model_output.summary_preds,
                model_output.hp_frac_aux,
            )
            if isinstance(t, torch.Tensor) and t.requires_grad
        ]
        if not grad_inputs:
            return dict.fromkeys(per_head_loss, 0.0)

        out: dict[str, float] = {}
        for name, loss in per_head_loss.items():
            grads = torch.autograd.grad(
                loss,
                grad_inputs,
                retain_graph=True,
                allow_unused=True,
            )
            sq_sum = 0.0
            for g in grads:
                if g is None:
                    continue
                sq_sum += float(g.detach().pow(2).sum().item())
            out[name] = float(sq_sum**0.5)
        return out


# ---------------------------------------------------------------------------
# Phase-1 head loss functions
# ---------------------------------------------------------------------------
def _policy_ce_loss(model_output: ModelOutput, encoded_batch: EncodedBatch) -> torch.Tensor:
    """Masked cross-entropy of ``policy_logits`` against ``policy_target``.

    Sets illegal-action logits to ``-inf`` so they receive zero
    softmax mass (and therefore zero gradient). Uses ``log_softmax``
    for numerical stability and computes the per-sample sum then
    batch mean: ``- mean_b Σ_a target_{b,a} log_softmax(logits)_{b,a}``.

    Numerical care: ``log_softmax`` at illegal indices is ``-inf``. The
    encoder guarantees ``policy_target`` is zero there, but ``0 *
    -inf = nan`` in IEEE float. We zero-out the contribution at illegal
    indices via ``torch.where`` *after* the log_softmax so that
    autograd flows zero gradient back to the masked logits.
    """
    logits = model_output.policy_logits
    mask = encoded_batch.legal_action_mask  # True = legal
    # Mask illegal entries to -inf BEFORE log_softmax.
    masked = logits.masked_fill(~mask, float("-inf"))
    log_probs = F.log_softmax(masked, dim=-1)
    target = encoded_batch.policy_target
    # Build the per-element contribution with explicit zeros at
    # illegal indices to avoid ``0 * -inf = nan`` poisoning the sum.
    contrib = target * log_probs
    contrib = torch.where(mask, contrib, torch.zeros_like(contrib))
    per_sample = -contrib.sum(dim=-1)
    return per_sample.mean()


def _combat_sample_loss(model_output: ModelOutput, encoded_batch: EncodedBatch) -> torch.Tensor:
    """Phase-1 degenerate-single MSE per ADR-021."""
    return F.mse_loss(model_output.sample_preds, encoded_batch.combat_sample_targets)


def _combat_summary_loss(model_output: ModelOutput, encoded_batch: EncodedBatch) -> torch.Tensor:
    """5-component summary loss.

    Field order matches tensor_encoder._SUMMARY_FIELD_COUNT layout:

      0 survival_probability  — BCE-with-logits (network emits logit)
      1 expected_hp_delta     — MSE
      2 expected_turns        — MSE
      3 timeout_probability   — BCE-with-logits
      4 uncertainty           — MSE

    Sum the 5 sub-losses (each ``mean()``-reduced) → scalar.
    """
    preds = model_output.summary_preds  # (B, 5) logits/raw
    tgt = encoded_batch.combat_summary_targets  # (B, 5)
    survival = F.binary_cross_entropy_with_logits(preds[:, 0], tgt[:, 0])
    hp_delta = F.mse_loss(preds[:, 1], tgt[:, 1])
    turns = F.mse_loss(preds[:, 2], tgt[:, 2])
    timeout = F.binary_cross_entropy_with_logits(preds[:, 3], tgt[:, 3])
    uncertainty = F.mse_loss(preds[:, 4], tgt[:, 4])
    return survival + hp_delta + turns + timeout + uncertainty


def _hp_frac_aux_loss(model_output: ModelOutput, encoded_batch: EncodedBatch) -> torch.Tensor:
    """Scalar MSE between predicted hp-frac and bootstrap target."""
    return F.mse_loss(model_output.hp_frac_aux, encoded_batch.hp_frac_target)


def _kl_vs_prior_loss(
    model_output: ModelOutput, encoded_batch: EncodedBatch, model: object
) -> torch.Tensor:
    """Masked KL of current softmax against prior softmax.

    - ``current = log_softmax(masked policy_logits)``
    - ``prior   = log_softmax(masked compute_prior_logits)``
    - Mask: illegal actions get ``-inf`` pre-softmax in both.
    - ``KL = mean_b Σ_a p_curr_{b,a} (log p_curr - log p_prior)``.

    The model's ``compute_prior_logits`` is expected to run under
    ``no_grad`` internally; even so, we ``detach`` the prior log-probs
    here as defense in depth.
    """
    mask = encoded_batch.legal_action_mask  # (B, A) True = legal
    cur_logits = model_output.policy_logits.masked_fill(~mask, float("-inf"))
    prior_raw = model.compute_prior_logits(encoded_batch)  # type: ignore[attr-defined]
    prior_logits = prior_raw.masked_fill(~mask, float("-inf"))

    cur = F.log_softmax(cur_logits, dim=-1)
    prior = F.log_softmax(prior_logits, dim=-1).detach()

    # At illegal indices both ``cur`` and ``prior`` are ``-inf``; their
    # difference and exponentials yield ``nan`` under autograd. Replace
    # with zeros via ``torch.where`` *before* arithmetic so values
    # (and gradients) flowing back at illegal positions are zero
    # exactly. ``where`` plugs in the zero branch as a constant — no
    # grad flows from the masked positions. At legal indices the
    # values are untouched.
    zero = torch.zeros_like(cur)
    cur_safe = torch.where(mask, cur, zero)
    prior_safe = torch.where(mask, prior, zero)
    # cur_p == exp(cur) at legal; 0 at illegal (matches exp(-inf) = 0).
    cur_p = torch.where(mask, cur_safe.exp(), zero)
    contrib = cur_p * (cur_safe - prior_safe)
    return contrib.sum(dim=-1).mean()


__all__ = ["LossEngine", "LossResult"]

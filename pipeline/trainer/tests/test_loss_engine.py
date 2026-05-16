"""Tests for ``pipeline.trainer.loss_engine`` (S0.C.β).

Covers the 6 unit tests called out in
``pipeline/trainer/docs/specs/modules/loss-engine.md`` §Testing Strategy.

A ``MockModel`` exposing only ``compute_prior_logits`` is used in lieu
of the real ``TrainerNet`` (S0.C.α is in flight in parallel; the loss
engine takes a duck-typed model). The mock returns a configurable
fixed tensor; tests that need ``prior == current`` substitute the
current logits.
"""

from __future__ import annotations

import math
from dataclasses import replace
from pathlib import Path

import torch
import torch.nn.functional as F

from pipeline.trainer.loss_engine import LossEngine, LossResult
from pipeline.trainer.run_config import LossWeights, RunConfig
from pipeline.trainer.tensor_encoder import EncodedBatch
from pipeline.trainer.types import ModelOutput


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------
class _MockModel:
    """Minimal stand-in for ``TrainerNet`` exposing only the contract the
    loss engine needs: ``compute_prior_logits(encoded_batch) -> Tensor``.

    The S0.C.α model is implemented in parallel; this duck-typed mock
    lets the loss engine tests run independently. After merge, if the
    real model's contract diverges, re-surface (see task spec).
    """

    def __init__(self, prior_logits: torch.Tensor | None = None) -> None:
        self._prior = prior_logits

    def set_prior(self, prior_logits: torch.Tensor) -> None:
        self._prior = prior_logits

    def compute_prior_logits(self, encoded_batch: EncodedBatch) -> torch.Tensor:
        if self._prior is not None:
            return self._prior
        # Default mirror: uniform prior matching the legal-action shape.
        return torch.zeros_like(encoded_batch.legal_action_mask, dtype=torch.float32)


def _load_cfg() -> RunConfig:
    cfg_path = Path(__file__).resolve().parents[1] / "config" / "local.json"
    return RunConfig.load(cfg_path)


def _make_eb(
    *,
    b: int,
    a: int,
    legal_mask: torch.Tensor | None = None,
    policy_target: torch.Tensor | None = None,
    combat_sample_targets: torch.Tensor | None = None,
    combat_summary_targets: torch.Tensor | None = None,
    hp_frac_target: torch.Tensor | None = None,
    prior_logits: torch.Tensor | None = None,
) -> EncodedBatch:
    """Build a minimal EncodedBatch for loss-engine tests."""
    if legal_mask is None:
        legal_mask = torch.ones(b, a, dtype=torch.bool)
    if policy_target is None:
        policy_target = F.softmax(torch.zeros(b, a), dim=-1)
    if combat_sample_targets is None:
        combat_sample_targets = torch.zeros(b, 4)
    if combat_summary_targets is None:
        combat_summary_targets = torch.zeros(b, 5)
    if hp_frac_target is None:
        hp_frac_target = torch.zeros(b)
    if prior_logits is None:
        prior_logits = torch.zeros(b, a)
    return EncodedBatch(
        tokens=torch.zeros(b, 8, dtype=torch.long),
        padding_mask=torch.zeros(b, 8, dtype=torch.bool),
        legal_action_mask=legal_mask,
        policy_target=policy_target,
        combat_sample_targets=combat_sample_targets,
        combat_summary_targets=combat_summary_targets,
        hp_frac_target=hp_frac_target,
        prior_logits=prior_logits,
        macro_context=torch.zeros(b, 11),
        metadata={"content_registry_sha": "test"},
    )


def _make_mo(
    *,
    b: int,
    a: int,
    policy_logits: torch.Tensor | None = None,
    sample_preds: torch.Tensor | None = None,
    summary_preds: torch.Tensor | None = None,
    hp_frac_aux: torch.Tensor | None = None,
) -> ModelOutput:
    if policy_logits is None:
        policy_logits = torch.randn(b, a, requires_grad=True)
    if sample_preds is None:
        sample_preds = torch.randn(b, 4, requires_grad=True)
    if summary_preds is None:
        summary_preds = torch.randn(b, 5, requires_grad=True)
    if hp_frac_aux is None:
        hp_frac_aux = torch.randn(b, requires_grad=True)
    return ModelOutput(
        policy_logits=policy_logits,
        sample_preds=sample_preds,
        summary_preds=summary_preds,
        hp_frac_aux=hp_frac_aux,
    )


# ---------------------------------------------------------------------------
# Test 1 — Policy CE ignores illegal actions
# ---------------------------------------------------------------------------
def test_policy_ce_ignores_illegal_actions() -> None:
    """Mask action 3 of 10 as illegal; gradient to logits[:, 3] is ~0."""
    torch.manual_seed(0)
    cfg = _load_cfg()
    model = _MockModel()
    # Use a config where only the policy head fires so any non-zero grad
    # at index 3 must come from the policy CE.
    cfg = replace(
        cfg,
        loss_weights=LossWeights(
            policy=1.0,
            combat_sample=0.0,
            combat_summary=0.0,
            hp_frac_aux=0.0,
            kl_beta=0.0,
        ),
    )
    engine = LossEngine(cfg, model)

    b, a = 4, 10
    legal_mask = torch.ones(b, a, dtype=torch.bool)
    legal_mask[:, 3] = False
    # Target supported only on legal actions (uniform over legal).
    raw_target = legal_mask.float()
    policy_target = raw_target / raw_target.sum(dim=-1, keepdim=True)

    eb = _make_eb(b=b, a=a, legal_mask=legal_mask, policy_target=policy_target)
    policy_logits = torch.randn(b, a, requires_grad=True)
    mo = _make_mo(b=b, a=a, policy_logits=policy_logits)
    result = engine.compute(mo, eb)
    result.total.backward()

    grad_col_3 = policy_logits.grad[:, 3]
    # masked_fill -> -inf -> softmax exact 0 -> grad exact 0 (no NaN).
    assert torch.all(grad_col_3 == 0.0), f"expected zero grad on illegal column, got {grad_col_3}"
    # And the legal columns should have non-trivial gradient signal.
    assert policy_logits.grad[:, 0].abs().sum() > 0


# ---------------------------------------------------------------------------
# Test 2 — Hand-computed reference on tiny batch
# ---------------------------------------------------------------------------
def test_hand_computed_reference_total() -> None:
    """2x3 batch with known logits/targets → total matches hand math."""
    torch.manual_seed(0)
    cfg = _load_cfg()
    # Pin loss weights to known values for the closed-form check.
    cfg = replace(
        cfg,
        loss_weights=LossWeights(
            policy=1.0,
            combat_sample=1.0,
            combat_summary=1.0,
            hp_frac_aux=0.05,
            kl_beta=0.01,
        ),
    )
    model = _MockModel()
    engine = LossEngine(cfg, model)

    b, a = 2, 3
    legal_mask = torch.ones(b, a, dtype=torch.bool)
    policy_logits = torch.tensor([[0.0, 0.0, 0.0], [1.0, 0.0, -1.0]], requires_grad=True)
    policy_target = torch.tensor([[1.0, 0.0, 0.0], [0.0, 1.0, 0.0]], dtype=torch.float32)
    sample_preds = torch.tensor([[0.0, 0.0, 0.0, 0.0], [0.5, 0.0, 0.0, 0.0]], requires_grad=True)
    combat_sample_targets = torch.zeros(b, 4)
    summary_preds = torch.zeros(b, 5, requires_grad=True)
    combat_summary_targets = torch.zeros(b, 5)
    hp_frac_aux = torch.tensor([0.0, 0.0], requires_grad=True)
    hp_frac_target = torch.zeros(b)
    # Prior == zeros (mock default), current logits as above — KL is
    # not zero (since current is not uniform), so we compute it
    # symbolically below.

    eb = _make_eb(
        b=b,
        a=a,
        legal_mask=legal_mask,
        policy_target=policy_target,
        combat_sample_targets=combat_sample_targets,
        combat_summary_targets=combat_summary_targets,
        hp_frac_target=hp_frac_target,
    )
    mo = _make_mo(
        b=b,
        a=a,
        policy_logits=policy_logits,
        sample_preds=sample_preds,
        summary_preds=summary_preds,
        hp_frac_aux=hp_frac_aux,
    )
    result = engine.compute(mo, eb)

    # Hand math.
    # Policy CE row 0: -log(softmax([0,0,0])[0]) = -log(1/3) = log 3.
    # Policy CE row 1: softmax([1,0,-1])[1] = e^0 / (e^1+e^0+e^-1)
    #                                       = 1 / (e + 1 + 1/e).
    z1 = math.exp(1.0) + 1.0 + math.exp(-1.0)
    p1_target = 1.0 / z1
    policy_loss = (math.log(3.0) + (-math.log(p1_target))) / 2.0

    # combat_sample: MSE over (2,4). One nonzero element 0.5 in pos
    # (1,0). MSE = mean of squared errors over all 8 elements.
    sample_loss = (0.5**2) / 8.0

    # combat_summary: preds=0, targets=0 → BCE-with-logits(0,0)
    # = -log(sigmoid(0)) * 0 + log(1+e^0) = log 2. MSE pieces = 0.
    bce_at_zero = math.log(1.0 + math.exp(0.0))  # log 2
    summary_loss = 2.0 * bce_at_zero + 0.0 + 0.0 + 0.0

    # hp_frac_aux: MSE between zeros → 0.
    hp_loss = 0.0

    # KL(current || prior=uniform). Per-row entropy contribution.
    # Row 0: current is uniform → KL = 0.
    # Row 1: log_softmax([1,0,-1]) entries:
    #   p = [e/z1, 1/z1, 1/(e*z1)]
    #   prior = uniform = 1/3 → log_prior = -log 3
    #   KL_row1 = Σ p * (log p - log_prior)
    #          = -H(p) - log(prior) since log_prior is constant.
    #          = Σ p * log p + log 3.
    p_row1 = [
        math.exp(1.0) / z1,
        1.0 / z1,
        math.exp(-1.0) / z1,
    ]
    sum_p_log_p = sum(p * math.log(p) for p in p_row1)
    kl_row1 = sum_p_log_p + math.log(3.0)
    kl_loss = (0.0 + kl_row1) / 2.0

    expected_total = (
        1.0 * policy_loss + 1.0 * sample_loss + 1.0 * summary_loss + 0.05 * hp_loss + 0.01 * kl_loss
    )

    assert math.isclose(
        float(result.total.detach().item()), expected_total, rel_tol=1e-5, abs_tol=1e-6
    ), (
        f"hand-derived total {expected_total} vs engine {float(result.total)}; "
        f"components={result.components}"
    )


# ---------------------------------------------------------------------------
# Test 3 — KL term zero when prior == current
# ---------------------------------------------------------------------------
def test_kl_zero_when_prior_equals_current() -> None:
    """Prior snapshot == current logits → KL ≈ 0."""
    torch.manual_seed(0)
    cfg = _load_cfg()
    cfg = replace(
        cfg,
        loss_weights=LossWeights(
            policy=0.0,
            combat_sample=0.0,
            combat_summary=0.0,
            hp_frac_aux=0.0,
            kl_beta=1.0,
        ),
    )
    b, a = 3, 4
    legal_mask = torch.ones(b, a, dtype=torch.bool)
    legal_mask[:, 0] = False  # at least one illegal — verify mask path.
    raw_target = legal_mask.float()
    policy_target = raw_target / raw_target.sum(dim=-1, keepdim=True)
    eb = _make_eb(b=b, a=a, legal_mask=legal_mask, policy_target=policy_target)

    policy_logits = torch.randn(b, a, requires_grad=True)
    # Snapshot: prior = detached copy of current.
    prior_snapshot = policy_logits.detach().clone()
    model = _MockModel(prior_logits=prior_snapshot)
    engine = LossEngine(cfg, model)

    mo = _make_mo(b=b, a=a, policy_logits=policy_logits)
    result = engine.compute(mo, eb)
    # KL is the only contributor (other weights = 0).
    assert math.isclose(float(result.components["kl_vs_prior"]), 0.0, abs_tol=1e-6), (
        f"expected KL ≈ 0, got {result.components['kl_vs_prior']}"
    )
    assert math.isclose(float(result.total.detach().item()), 0.0, abs_tol=1e-6)


# ---------------------------------------------------------------------------
# Test 4 — Phase-1 degenerate sample loss is well-formed
# ---------------------------------------------------------------------------
def test_combat_sample_degenerate_well_formed() -> None:
    """Single-sample preds + targets → MSE finite, gradient flows."""
    torch.manual_seed(0)
    cfg = _load_cfg()
    cfg = replace(
        cfg,
        loss_weights=LossWeights(
            policy=0.0,
            combat_sample=1.0,
            combat_summary=0.0,
            hp_frac_aux=0.0,
            kl_beta=0.0,
        ),
    )
    model = _MockModel()
    engine = LossEngine(cfg, model)

    b, a = 2, 3
    sample_preds = torch.tensor([[1.0, 0.5, 2.0, 0.0], [-1.0, 0.0, 1.0, 1.0]], requires_grad=True)
    combat_sample_targets = torch.tensor([[0.0, 1.0, 1.0, 0.0], [0.0, 0.0, 2.0, 1.0]])
    eb = _make_eb(b=b, a=a, combat_sample_targets=combat_sample_targets)
    mo = _make_mo(b=b, a=a, sample_preds=sample_preds)
    result = engine.compute(mo, eb)

    assert torch.isfinite(result.total)
    result.total.backward()
    assert sample_preds.grad is not None
    assert torch.isfinite(sample_preds.grad).all()
    # Hand MSE: ((1-0)^2+(0.5-1)^2+(2-1)^2+(0-0)^2 + (-1-0)^2+(0-0)^2+(1-2)^2+(1-1)^2)/8
    expected = (1.0 + 0.25 + 1.0 + 0.0 + 1.0 + 0.0 + 1.0 + 0.0) / 8.0
    assert math.isclose(float(result.components["combat_sample"]), expected, rel_tol=1e-6)


# ---------------------------------------------------------------------------
# Test 5 — Weight registry change updates total in next call
# ---------------------------------------------------------------------------
def test_weight_change_updates_next_call() -> None:
    """Setting weights['hp_frac_aux'] = 0 drops its contribution next call."""
    torch.manual_seed(0)
    cfg = _load_cfg()
    cfg = replace(
        cfg,
        loss_weights=LossWeights(
            policy=0.0,
            combat_sample=0.0,
            combat_summary=0.0,
            hp_frac_aux=1.0,
            kl_beta=0.0,
        ),
    )
    model = _MockModel()
    engine = LossEngine(cfg, model)

    b, a = 2, 3
    hp_frac_aux = torch.tensor([1.0, 2.0], requires_grad=True)
    hp_frac_target = torch.zeros(b)
    eb = _make_eb(b=b, a=a, hp_frac_target=hp_frac_target)
    mo = _make_mo(b=b, a=a, hp_frac_aux=hp_frac_aux)

    r1 = engine.compute(mo, eb)
    # MSE([1,2], [0,0]) = (1+4)/2 = 2.5
    assert math.isclose(float(r1.total.detach().item()), 2.5, rel_tol=1e-6)
    assert math.isclose(float(r1.components["hp_frac_aux"]), 2.5, rel_tol=1e-6)
    assert math.isclose(float(r1.weights["hp_frac_aux"]), 1.0)

    # Flip the weight.
    engine.weights["hp_frac_aux"] = 0.0
    r2 = engine.compute(mo, eb)
    assert math.isclose(float(r2.total.detach().item()), 0.0, abs_tol=1e-6)
    # Component still records the un-weighted loss value (detached).
    assert math.isclose(float(r2.components["hp_frac_aux"]), 2.5, rel_tol=1e-6)
    assert math.isclose(float(r2.weights["hp_frac_aux"]), 0.0)


# ---------------------------------------------------------------------------
# Test 6 — Gradient diagnostics on demand
# ---------------------------------------------------------------------------
def test_gradient_diagnostics_on_demand() -> None:
    """Default off → {}; enable_pcgrad_diag=True → populated per head."""
    torch.manual_seed(0)
    cfg = _load_cfg()
    model = _MockModel()
    engine = LossEngine(cfg, model)

    b, a = 2, 4
    eb = _make_eb(b=b, a=a)
    mo = _make_mo(b=b, a=a)

    # Default off.
    r_off = engine.compute(mo, eb)
    assert r_off.gradient_diagnostics == {}

    # Flag on.
    engine.enable_pcgrad_diag = True
    r_on = engine.compute(mo, eb)
    assert set(r_on.gradient_diagnostics.keys()) == {
        "policy",
        "combat_sample",
        "combat_summary",
        "hp_frac_aux",
        "kl_vs_prior",
    }
    # All finite, non-negative.
    for name, gn in r_on.gradient_diagnostics.items():
        assert math.isfinite(gn), f"{name}: non-finite grad norm {gn}"
        assert gn >= 0.0, f"{name}: negative grad norm {gn}"
    # At least one head should have non-trivial gradient signal — e.g.
    # combat_sample MSE on randn predictions.
    assert r_on.gradient_diagnostics["combat_sample"] > 0.0


# ---------------------------------------------------------------------------
# Bonus — verify the LossResult contract (returned types).
# ---------------------------------------------------------------------------
def test_loss_result_contract() -> None:
    """Shape sanity: LossResult fields have the documented types."""
    torch.manual_seed(0)
    cfg = _load_cfg()
    model = _MockModel()
    engine = LossEngine(cfg, model)
    b, a = 2, 3
    eb = _make_eb(b=b, a=a)
    mo = _make_mo(b=b, a=a)
    result = engine.compute(mo, eb)
    assert isinstance(result, LossResult)
    assert isinstance(result.total, torch.Tensor)
    assert result.total.requires_grad
    assert isinstance(result.components, dict)
    assert isinstance(result.weights, dict)
    assert set(result.components.keys()) == {
        "policy",
        "combat_sample",
        "combat_summary",
        "hp_frac_aux",
        "kl_vs_prior",
    }
    assert isinstance(result.gradient_diagnostics, dict)

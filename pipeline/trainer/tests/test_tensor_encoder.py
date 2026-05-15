"""Tests for ``pipeline.trainer.tensor_encoder`` (S0.B.γ).

Covers the 6 unit tests called out in
``pipeline/trainer/docs/specs/modules/tensor-encoder.md`` §Testing Strategy.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import pytest
import torch

from pipeline.common.trajectory_proto import (
    OBSERVABILITY_REGIME_POLICY_VISIBLE,
    OBSERVABILITY_REGIME_SOURCE_PERFECT,
    CombatOutcomeSample,
    CombatOutcomeSummary,
    TrajectoryStep,
)
from pipeline.trainer.content_registry import ContentRegistry
from pipeline.trainer.tensor_encoder import (
    EncodedBatch,
    TensorEncoder,
)


_REGISTRY_PATH = Path(__file__).resolve().parents[3] / "contracts" / "registry" / "phase1-silent.json"


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class _NetworkCfg:
    max_seq_len: int = 32
    max_action_space: int = 100


@pytest.fixture(scope="module")
def registry() -> ContentRegistry:
    return ContentRegistry.load(_REGISTRY_PATH)


def _make_step(
    *,
    rich_state: bytes = b"\x01\x02\x03",
    legal_action_ids: list[int] | None = None,
    search_policy: list[float] | None = None,
    hp_delta: float = -3.0,
    survived: bool = True,
    turns_taken: int = 4,
    timeout: bool = False,
    summary_hp_delta: float = -3.0,
    survival_probability: float = 0.9,
    expected_turns: float = 4.0,
    timeout_probability: float = 0.1,
    uncertainty: float = 0.05,
    observability_regime: int = OBSERVABILITY_REGIME_POLICY_VISIBLE,
) -> TrajectoryStep:
    legal = legal_action_ids if legal_action_ids is not None else [0, 1, 2]
    sp = search_policy if search_policy is not None else [0.5, 0.3, 0.2]
    return TrajectoryStep(
        rich_state=rich_state,
        legal_action_ids=legal,
        search_policy=sp,
        action_taken=0,
        reward=0.0,
        terminal=False,
        decision_type=1,
        combat_outcome_samples=[
            CombatOutcomeSample(
                hp_delta=hp_delta,
                probability_weight=1.0,
                survived=survived,
                turns_taken=turns_taken,
                timeout=timeout,
            )
        ],
        combat_outcome_summary=CombatOutcomeSummary(
            survival_probability=survival_probability,
            expected_hp_delta=summary_hp_delta,
            expected_turns=expected_turns,
            timeout_probability=timeout_probability,
            uncertainty=uncertainty,
        ),
        observability_regime=observability_regime,
    )


# ---------------------------------------------------------------------------
# Test 1 — Deterministic encode
# ---------------------------------------------------------------------------
def test_deterministic_encode(registry: ContentRegistry) -> None:
    enc1 = TensorEncoder(registry, _NetworkCfg())
    enc2 = TensorEncoder(registry, _NetworkCfg())
    steps = [_make_step(rich_state=b"\x01\x02\x03\x04") for _ in range(3)]
    a = enc1.encode_batch(steps)
    b = enc2.encode_batch(steps)
    # Same input → bit-equal tensors (CPU, float32 / long / bool).
    assert torch.equal(a.tokens, b.tokens)
    assert torch.equal(a.padding_mask, b.padding_mask)
    assert torch.equal(a.legal_action_mask, b.legal_action_mask)
    assert torch.equal(a.policy_target, b.policy_target)
    assert torch.equal(a.combat_sample_targets, b.combat_sample_targets)
    assert torch.equal(a.combat_summary_targets, b.combat_summary_targets)
    assert torch.equal(a.hp_frac_target, b.hp_frac_target)
    # Prior logits contain -inf — torch.equal treats NaN as inequal but -inf == -inf
    assert torch.equal(a.prior_logits, b.prior_logits)
    assert torch.equal(a.macro_context, b.macro_context)
    assert a.metadata == b.metadata


# ---------------------------------------------------------------------------
# Test 2 — Padding mask correctness
# ---------------------------------------------------------------------------
def test_padding_mask_correctness(registry: ContentRegistry) -> None:
    cfg = _NetworkCfg(max_seq_len=20)
    enc = TensorEncoder(registry, cfg)
    steps = [
        _make_step(rich_state=bytes(range(10))),  # len 10
        _make_step(rich_state=bytes(range(20))),  # len 20 (== T)
        _make_step(rich_state=bytes(range(15))),  # len 15
    ]
    batch = enc.encode_batch(steps)
    assert batch.tokens.shape == (3, 20)
    assert batch.padding_mask.shape == (3, 20)
    # Row 0: True at positions >= 10
    assert batch.padding_mask[0, :10].sum() == 0  # all False
    assert batch.padding_mask[0, 10:].sum() == 10  # 10 padded
    # Row 1: all False (filled exactly)
    assert batch.padding_mask[1].sum() == 0
    # Row 2: True at positions >= 15
    assert batch.padding_mask[2, :15].sum() == 0
    assert batch.padding_mask[2, 15:].sum() == 5


def test_padding_truncates_long_rich_state(registry: ContentRegistry) -> None:
    cfg = _NetworkCfg(max_seq_len=8)
    enc = TensorEncoder(registry, cfg)
    step = _make_step(rich_state=bytes(range(20)))  # 20 bytes > T=8
    batch = enc.encode_batch([step])
    assert batch.tokens.shape == (1, 8)
    assert batch.padding_mask.sum() == 0  # nothing padded; row is full


# ---------------------------------------------------------------------------
# Test 3 — Legal-action mask + policy_target invariants
# ---------------------------------------------------------------------------
def test_legal_action_mask_and_policy_sum(registry: ContentRegistry) -> None:
    enc = TensorEncoder(registry, _NetworkCfg(max_action_space=20))
    # 5 legal actions of (max action_space) 20 — verify shape + counts.
    step = _make_step(
        legal_action_ids=[0, 1, 2, 3, 4],
        search_policy=[0.1, 0.2, 0.3, 0.25, 0.15],
    )
    batch = enc.encode_batch([step])
    # Phase-1 action-space size = max legal across batch (5 here), capped.
    assert batch.legal_action_mask.shape == (1, 5)
    # Exactly 5 True
    assert int(batch.legal_action_mask.sum().item()) == 5
    # policy_target sums to ~1 over True positions, 0 elsewhere
    masked_sum = batch.policy_target[batch.legal_action_mask].sum().item()
    assert pytest.approx(masked_sum, abs=1e-6) == 1.0
    unmasked = batch.policy_target[~batch.legal_action_mask]
    assert unmasked.numel() == 0  # all positions are legal in this case


def test_legal_action_mask_with_mixed_batch(registry: ContentRegistry) -> None:
    """5 legal vs. 2 legal → action space = 5; policy 0 on unused tail."""
    enc = TensorEncoder(registry, _NetworkCfg(max_action_space=20))
    step_a = _make_step(
        legal_action_ids=[0, 1, 2, 3, 4],
        search_policy=[0.1, 0.2, 0.3, 0.25, 0.15],
    )
    step_b = _make_step(
        legal_action_ids=[0, 1],
        search_policy=[0.6, 0.4],
    )
    batch = enc.encode_batch([step_a, step_b])
    assert batch.legal_action_mask.shape == (2, 5)
    # row 0: 5 True; row 1: 2 True
    assert int(batch.legal_action_mask[0].sum().item()) == 5
    assert int(batch.legal_action_mask[1].sum().item()) == 2
    # row 1 unused tail: legal False, policy 0
    assert int(batch.legal_action_mask[1, 2:].sum().item()) == 0
    assert float(batch.policy_target[1, 2:].sum().item()) == 0.0
    # row 1 policy sums to 1 over legal
    assert pytest.approx(
        batch.policy_target[1, :2].sum().item(), abs=1e-6
    ) == 1.0


# ---------------------------------------------------------------------------
# Test 4 — Phase-1 degenerate sample handling (ADR-021)
# ---------------------------------------------------------------------------
def test_phase1_degenerate_sample_aligns_with_summary(
    registry: ContentRegistry,
) -> None:
    enc = TensorEncoder(registry, _NetworkCfg())
    # Construct a step where the degenerate sample mirrors the summary's
    # expected_hp_delta — per ADR-021's degenerate-single convention.
    step = _make_step(
        hp_delta=-7.5,
        survived=True,
        turns_taken=6,
        timeout=False,
        summary_hp_delta=-7.5,
        survival_probability=0.95,
        expected_turns=6.0,
        timeout_probability=0.05,
        uncertainty=0.02,
    )
    batch = enc.encode_batch([step])
    # Sample fields order: hp_delta, survived, turns_taken, timeout
    sample_row = batch.combat_sample_targets[0]
    assert pytest.approx(sample_row[0].item(), abs=1e-5) == -7.5
    assert sample_row[1].item() == 1.0  # survived True → 1.0
    assert sample_row[2].item() == 6.0
    assert sample_row[3].item() == 0.0  # timeout False → 0.0
    # Summary fields order: survival_prob, expected_hp_delta, expected_turns,
    # timeout_prob, uncertainty
    summary_row = batch.combat_summary_targets[0]
    assert pytest.approx(summary_row[0].item(), abs=1e-5) == 0.95
    assert pytest.approx(summary_row[1].item(), abs=1e-5) == -7.5
    assert pytest.approx(summary_row[2].item(), abs=1e-5) == 6.0
    assert pytest.approx(summary_row[3].item(), abs=1e-5) == 0.05
    assert pytest.approx(summary_row[4].item(), abs=1e-5) == 0.02
    # hp_frac_target == summary.expected_hp_delta (Phase-1 bootstrap aux)
    assert pytest.approx(batch.hp_frac_target[0].item(), abs=1e-5) == -7.5
    # The degenerate convention: sample.hp_delta == summary.expected_hp_delta
    assert pytest.approx(sample_row[0].item(), abs=1e-5) == summary_row[1].item()


# ---------------------------------------------------------------------------
# Test 5 — SOURCE_PERFECT leak — strict raises; soft-fail increments
# ---------------------------------------------------------------------------
def test_source_perfect_leak_raises_in_strict_mode(
    registry: ContentRegistry,
) -> None:
    enc = TensorEncoder(registry, _NetworkCfg())  # strict default
    leaky = _make_step(observability_regime=OBSERVABILITY_REGIME_SOURCE_PERFECT)
    with pytest.raises(ValueError) as exc:
        enc.encode_batch([leaky])
    assert "SOURCE_PERFECT" in str(exc.value)


def test_source_perfect_soft_fail_increments_counter(
    registry: ContentRegistry,
) -> None:
    enc = TensorEncoder(
        registry, _NetworkCfg(), strict_source_perfect=False
    )
    assert enc.source_perfect_leak_count == 0
    leaky = _make_step(observability_regime=OBSERVABILITY_REGIME_SOURCE_PERFECT)
    ok = _make_step(observability_regime=OBSERVABILITY_REGIME_POLICY_VISIBLE)
    # Should NOT raise; counter bumps by 1 per leaked step.
    batch = enc.encode_batch([leaky, ok, leaky])
    assert enc.source_perfect_leak_count == 2
    # Tensors still produced for downstream pipeline (audit-defense, not abort)
    assert batch.tokens.shape[0] == 3


# ---------------------------------------------------------------------------
# Test 6 — Unknown token id falls back gracefully OR raises clearly
# ---------------------------------------------------------------------------
def test_unknown_token_id_falls_back_deterministically(
    registry: ContentRegistry,
) -> None:
    """Per the chosen Phase-1 byte-chunk strategy, an unknown id falls back
    to ``byte_value % len(registry)``. The fallback is deterministic and
    stable across batches.
    """
    enc = TensorEncoder(registry, _NetworkCfg())
    # Byte values 250..255 are well outside the 241-token Phase-1 table.
    # Encoder must produce stable token ids without raising.
    step = _make_step(rich_state=bytes([250, 251, 252, 253, 254, 255]))
    batch_a = enc.encode_batch([step])
    batch_b = enc.encode_batch([step])
    # Determinism across two encodes
    assert torch.equal(batch_a.tokens[0, :6], batch_b.tokens[0, :6])
    # All produced ids are within the registry index range.
    n = len(registry)
    for tok in batch_a.tokens[0, :6].tolist():
        assert 0 <= tok < n or registry.has_token_id(tok)


def test_unknown_token_id_named_in_error_when_registry_empty(
    tmp_path: Path,
) -> None:
    """A pathological empty registry triggers a clear RuntimeError naming
    the missing-token failure mode. The fallback can't index into a
    zero-token table.
    """
    import json as _json

    bad = tmp_path / "empty.json"
    bad.write_text(
        _json.dumps(
            {
                "manifest": {
                    "version": "empty.0",
                    "schema_version": {"major": 0, "minor": 0},
                    "parent_version": None,
                },
                "tokens": [],
                "card_dsl": [],
            }
        )
    )
    empty_reg = ContentRegistry.load(bad)
    enc = TensorEncoder(empty_reg, _NetworkCfg())
    step = _make_step(rich_state=b"\x01\x02")
    with pytest.raises(RuntimeError) as exc:
        enc.encode_batch([step])
    msg = str(exc.value).lower()
    assert "registry" in msg
    assert "zero" in msg or "empty" in msg


# ---------------------------------------------------------------------------
# Cross-cutting checks: metadata + macro_context + prior_logits shape
# ---------------------------------------------------------------------------
def test_metadata_contains_content_registry_sha(
    registry: ContentRegistry,
) -> None:
    enc = TensorEncoder(registry, _NetworkCfg())
    batch = enc.encode_batch([_make_step()])
    assert batch.metadata["content_registry_sha"] == registry.content_hash


def test_macro_context_is_phase1_zero_stub(
    registry: ContentRegistry,
) -> None:
    enc = TensorEncoder(registry, _NetworkCfg())
    batch = enc.encode_batch([_make_step(), _make_step()])
    assert batch.macro_context.shape == (2, 9)
    assert torch.all(batch.macro_context == 0.0)


def test_prior_logits_minus_inf_on_illegal(registry: ContentRegistry) -> None:
    enc = TensorEncoder(registry, _NetworkCfg())
    step_small = _make_step(legal_action_ids=[0, 1], search_policy=[0.6, 0.4])
    step_big = _make_step(
        legal_action_ids=[0, 1, 2, 3], search_policy=[0.25, 0.25, 0.25, 0.25]
    )
    batch = enc.encode_batch([step_small, step_big])
    # A = 4; row 0's tail positions 2,3 are illegal → -inf
    assert batch.prior_logits[0, 2].item() == float("-inf")
    assert batch.prior_logits[0, 3].item() == float("-inf")
    # row 0 legal slots are finite (log of positive prob)
    assert torch.isfinite(batch.prior_logits[0, 0])
    assert torch.isfinite(batch.prior_logits[0, 1])


def test_encode_seconds_p99_property_updates(
    registry: ContentRegistry,
) -> None:
    enc = TensorEncoder(registry, _NetworkCfg())
    assert enc.encode_seconds_p99 == 0.0
    enc.encode_batch([_make_step()])
    # After at least one call, p99 is >= 0 and finite.
    p = enc.encode_seconds_p99
    assert p >= 0.0
    assert p < 10.0  # sanity (Phase-1 expectation is microseconds)

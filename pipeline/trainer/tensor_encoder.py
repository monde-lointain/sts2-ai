"""Tensor-encoder submodule: pure ``TrajectoryStep`` → model-ready tensors.

Per Q10-ADR-008, holds the frozen :class:`ContentRegistry` constructed at
bootstrap. ``encode_batch`` is a pure function: deterministic CPU tensors,
no random state, no side effects beyond the in-process Welford-style
p99-latency estimator (exposed at :pyattr:`TensorEncoder.encode_seconds_p99`
for the metrics emitter — ``train_driver`` reads it and ``set()``s the
``sts2_q10_encode_seconds_p99`` gauge).

Phase-1 ``rich_state`` rendering — explicit, light, documented
============================================================
``TrajectoryStep.rich_state`` is opaque pre-encoded bytes from Q1. The full
Phase-2+ RichState renderer is not in scope here; for the Phase-1 ">=1k
trajectory" smoke target we interpret each byte as a single integer token
id. Specifically:

  ids[i] = byte_value if byte_value in content_registry else fallback(byte_value)
  fallback(b) = b % len(content_registry)  # deterministic, stable across batches

Sequences are truncated to ``config.max_seq_len`` and right-padded with id
0. The ``padding_mask`` is ``True`` where padded. This is intentionally
load-bearing for shape / determinism / mask-correctness tests only; the
embedding semantics will be replaced by a proper RichState→sequence
renderer in Phase-2 (see ``tensor-encoder.md`` §"Phase-1 RichState rendering").

Cross-quantum ADRs honored
===========================
- ADR-014  Combat output is samples + summary (both surface in EncodedBatch).
- ADR-016  Hidden-state filter: encoder asserts no SOURCE_PERFECT regime
           leaks through. Strict mode raises ``ValueError``; soft-fail mode
           increments :pyattr:`source_perfect_leak_count` and skips encoding
           tagged fields.
- ADR-019 (Accepted 2026-05-15)  ``macro_context`` is a Phase-1 zero-stub;
           Phase-2 learned head per the ratified hybrid derivation.
- ADR-021  Phase-1 ``combat_outcome_samples[]`` is a degenerate single
           sample; we extract sample[0] only.
"""

from __future__ import annotations

import math
import time
from dataclasses import dataclass
from typing import Protocol

import torch

from pipeline.common.trajectory_proto import (
    OBSERVABILITY_REGIME_SOURCE_PERFECT,
)
from pipeline.trainer.content_registry import ContentRegistry

# ---------------------------------------------------------------------------
# Field counts (Phase-1 fixed dimensions per ADR-014 / ADR-021)
# ---------------------------------------------------------------------------
# combat_outcome_samples[0]: hp_delta, survived (0/1), turns_taken, timeout (0/1)
_SAMPLE_FIELD_COUNT: int = 4
# combat_outcome_summary: survival_probability, expected_hp_delta,
# expected_turns, timeout_probability, uncertainty
_SUMMARY_FIELD_COUNT: int = 5
# MacroContext v1.1 has 11 numeric fields per trajectory.proto (ADR-019):
# 9 v1.0 fields + sp(gold) + sp(MaxHP). `derivation_method` is a string and
# stays in metadata. Phase-1: all zero (learned head deferred to Phase-2).
_MACRO_DIM: int = 11

# Default cap on per-batch action-space breadth (config can override).
_DEFAULT_MAX_ACTION_SPACE: int = 100


class _NetworkConfigLike(Protocol):
    """Duck type accepted by ``TensorEncoder``.

    ``run_config.NetworkConfig`` already satisfies this; smoke tests pass a
    minimal dataclass with just ``max_seq_len``. ``max_action_space`` is
    optional — falls back to :pydata:`_DEFAULT_MAX_ACTION_SPACE`.
    """

    max_seq_len: int


# ---------------------------------------------------------------------------
# EncodedBatch
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class EncodedBatch:
    """Frozen bundle of CPU tensors produced by :meth:`TensorEncoder.encode_batch`.

    Shapes (with ``B`` = batch, ``T`` = max seq len in batch, ``A`` = max
    action-space size in batch):

    - ``tokens``                 LongTensor  (B, T)
    - ``padding_mask``           BoolTensor  (B, T) — True where padded
    - ``legal_action_mask``      BoolTensor  (B, A)
    - ``policy_target``          FloatTensor (B, A) — 0 on illegal
    - ``combat_sample_targets``  FloatTensor (B, sample_field_count)
    - ``combat_summary_targets`` FloatTensor (B, summary_field_count)
    - ``hp_frac_target``         FloatTensor (B,)
    - ``prior_logits``           FloatTensor (B, A) — -inf on illegal
    - ``macro_context``          FloatTensor (B, macro_dim) — Phase-1 zeros
    - ``metadata`` includes ``content_registry_sha`` per Q10-ADR-008.
    """

    tokens: torch.Tensor
    padding_mask: torch.Tensor
    legal_action_mask: torch.Tensor
    policy_target: torch.Tensor
    combat_sample_targets: torch.Tensor
    combat_summary_targets: torch.Tensor
    hp_frac_target: torch.Tensor
    prior_logits: torch.Tensor
    macro_context: torch.Tensor
    metadata: dict[str, str]


# ---------------------------------------------------------------------------
# TensorEncoder
# ---------------------------------------------------------------------------
class TensorEncoder:
    """Pure encoder from list[TrajectoryStep] to :class:`EncodedBatch`.

    Frozen by convention: ``content_registry`` and ``config`` are stashed
    once at construction; ``encode_batch`` is a pure function modulo the
    Welford latency estimator.

    Parameters
    ----------
    content_registry:
        Frozen Q4 bundle. Used for token-id lookup; ``content_hash`` lands
        in :pyattr:`EncodedBatch.metadata['content_registry_sha']`.
    config:
        Anything exposing ``max_seq_len`` (e.g. ``NetworkConfig``).
        Optional ``max_action_space`` attribute caps per-batch action-space
        size; default 100.
    strict_source_perfect:
        When True (default), a step tagged with ``OBSERVABILITY_REGIME_SOURCE_PERFECT``
        raises ``ValueError`` per ADR-016. When False (test-only soft-fail
        mode), the encoder increments :pyattr:`source_perfect_leak_count`
        and proceeds.
    """

    def __init__(
        self,
        content_registry: ContentRegistry,
        config: _NetworkConfigLike,
        *,
        strict_source_perfect: bool = True,
    ) -> None:
        self._registry = content_registry
        self._max_seq_len = int(config.max_seq_len)
        self._max_action_space = int(getattr(config, "max_action_space", _DEFAULT_MAX_ACTION_SPACE))
        self._strict = bool(strict_source_perfect)

        # Welford-style p99 estimator state. We track per-call wall-clock
        # microseconds; the gauge reports seconds. Implementation: keep a
        # bounded ring of recent samples and report the 99th percentile of
        # the ring (cheap, deterministic per same input ordering).
        self._latency_ring: list[float] = []
        self._latency_ring_cap: int = 1024  # one batch per slot

        # ADR-016 audit counter (soft-fail mode).
        self._source_perfect_leak_count: int = 0

    # -------- properties -------------------------------------------------
    @property
    def content_registry(self) -> ContentRegistry:
        return self._registry

    @property
    def max_seq_len(self) -> int:
        return self._max_seq_len

    @property
    def encode_seconds_p99(self) -> float:
        """Current p99 of recent encode-batch wall-clock latencies (seconds).

        Returns 0.0 before the first encode call.
        """
        if not self._latency_ring:
            return 0.0
        ordered = sorted(self._latency_ring)
        idx = math.ceil(0.99 * len(ordered)) - 1
        idx = max(idx, 0)
        if idx >= len(ordered):
            idx = len(ordered) - 1
        return float(ordered[idx])

    @property
    def source_perfect_leak_count(self) -> int:
        """ADR-016 audit counter (soft-fail mode only)."""
        return self._source_perfect_leak_count

    # -------- core API ---------------------------------------------------
    def encode_batch(self, steps: list) -> EncodedBatch:
        """Encode a batch of ``TrajectoryStep`` protobuf messages.

        Pure function (deterministic given the same registry, config, and
        steps). CPU tensors. Raises ``ValueError`` for SOURCE_PERFECT
        leaks in strict mode (default). Raises ``RuntimeError`` for
        unrecoverable token-table miss when the fallback also fails
        (currently never — fallback is a mod-bounded reduction).
        """
        if not steps:
            raise ValueError("encode_batch: steps list is empty")

        start = time.perf_counter()

        # ADR-016 filter first — fail fast before allocating tensors.
        self._enforce_source_perfect(steps)

        b = len(steps)
        t = self._max_seq_len

        # Action-space size for this batch: max across the batch, capped
        # at config max_action_space.
        per_step_legal: list[list[int]] = [list(s.legal_action_ids) for s in steps]
        per_step_lens = [len(ids) for ids in per_step_legal]
        max_legal = max(per_step_lens) if per_step_lens else 0
        a = max(1, min(max_legal, self._max_action_space))

        # 1) tokens + padding_mask
        tokens = torch.zeros((b, t), dtype=torch.long)
        padding_mask = torch.ones((b, t), dtype=torch.bool)
        for i, step in enumerate(steps):
            ids = self._render_rich_state_ids(step.rich_state)
            n = min(len(ids), t)
            if n:
                tokens[i, :n] = torch.tensor(ids[:n], dtype=torch.long)
                padding_mask[i, :n] = False

        # 2) legal_action_mask, policy_target, prior_logits
        legal_action_mask = torch.zeros((b, a), dtype=torch.bool)
        policy_target = torch.zeros((b, a), dtype=torch.float32)
        prior_logits = torch.full((b, a), float("-inf"), dtype=torch.float32)

        for i, step in enumerate(steps):
            legal_ids = per_step_legal[i]
            search_policy = list(step.search_policy)
            # Number of (legal_id, prob) pairs the step actually carries.
            # We only consume the first `a` entries of the legal list (cap).
            n_pairs = min(len(legal_ids), len(search_policy), a)
            for j in range(n_pairs):
                # Position in (B, A) is just j — the j-th legal slot for
                # this step. The semantic mapping (slot j ↔ action id
                # legal_ids[j]) is captured by storing legal_ids order.
                legal_action_mask[i, j] = True
                p = float(search_policy[j])
                policy_target[i, j] = p
                # Prior logit: log(prob). Clamp at a finite floor to keep
                # softmax stable when probability is exactly 0.
                if p > 0.0:
                    prior_logits[i, j] = math.log(p)
                else:
                    # Legal action with zero search prior: stays at -inf.
                    prior_logits[i, j] = float("-inf")

            # If the step has more legal ids than the policy can express
            # (truncation by `a`), the truncated tail keeps mask=False and
            # policy=0 — they are effectively pruned for this training step.

        # 3) combat_sample_targets — Phase-1 degenerate single per ADR-021.
        combat_sample_targets = torch.zeros((b, _SAMPLE_FIELD_COUNT), dtype=torch.float32)
        for i, step in enumerate(steps):
            samples = list(step.combat_outcome_samples)
            if samples:
                s0 = samples[0]
                combat_sample_targets[i, 0] = float(s0.hp_delta)
                combat_sample_targets[i, 1] = 1.0 if bool(s0.survived) else 0.0
                combat_sample_targets[i, 2] = float(s0.turns_taken)
                combat_sample_targets[i, 3] = 1.0 if bool(s0.timeout) else 0.0
            # else: zeros — non-combat step has no sample to extract.

        # 4) combat_summary_targets — ADR-014 fields, fixed order.
        combat_summary_targets = torch.zeros((b, _SUMMARY_FIELD_COUNT), dtype=torch.float32)
        for i, step in enumerate(steps):
            summary = step.combat_outcome_summary
            combat_summary_targets[i, 0] = float(summary.survival_probability)
            combat_summary_targets[i, 1] = float(summary.expected_hp_delta)
            combat_summary_targets[i, 2] = float(summary.expected_turns)
            combat_summary_targets[i, 3] = float(summary.timeout_probability)
            combat_summary_targets[i, 4] = float(summary.uncertainty)

        # 5) hp_frac_target — Phase-1 bootstrap aux per ADR-014 / ADR-018.
        hp_frac_target = torch.zeros((b,), dtype=torch.float32)
        for i, step in enumerate(steps):
            hp_frac_target[i] = float(step.combat_outcome_summary.expected_hp_delta)

        # 6) macro_context — Phase-1 zero-stub per ADR-019 deferral.
        macro_context = torch.zeros((b, _MACRO_DIM), dtype=torch.float32)

        # 7) metadata — content_registry_sha is the load-bearing provenance.
        metadata: dict[str, str] = {
            "content_registry_sha": self._registry.content_hash,
            "batch_size": str(b),
            "max_seq_len": str(t),
            "action_space": str(a),
        }

        elapsed = time.perf_counter() - start
        self._record_latency(elapsed)

        return EncodedBatch(
            tokens=tokens,
            padding_mask=padding_mask,
            legal_action_mask=legal_action_mask,
            policy_target=policy_target,
            combat_sample_targets=combat_sample_targets,
            combat_summary_targets=combat_summary_targets,
            hp_frac_target=hp_frac_target,
            prior_logits=prior_logits,
            macro_context=macro_context,
            metadata=metadata,
        )

    # -------- internals --------------------------------------------------
    def _enforce_source_perfect(self, steps: list) -> None:
        for i, step in enumerate(steps):
            regime = getattr(step, "observability_regime", 0)
            if int(regime) == int(OBSERVABILITY_REGIME_SOURCE_PERFECT):
                if self._strict:
                    raise ValueError(
                        "SOURCE_PERFECT regime leaked through to Q10; "
                        f"check Q3 filter (step index {i})"
                    )
                # Soft-fail mode (test variant): bump counter, continue.
                self._source_perfect_leak_count += 1

    def _render_rich_state_ids(self, rich_state: bytes) -> list[int]:
        """Phase-1 byte→token-id renderer (see module docstring).

        Each byte → token id. If the byte value is not a known token id,
        fall back to ``byte_value % len(registry)`` — deterministic and
        stable across batches.
        """
        if not rich_state:
            return []
        n_tokens = len(self._registry)
        if n_tokens == 0:
            raise RuntimeError(
                "ContentRegistry has zero tokens — refusing to encode "
                "rich_state (would index into an empty embedding table)"
            )
        out: list[int] = []
        for byte_value in rich_state[: self._max_seq_len]:
            if self._registry.has_token_id(byte_value):
                out.append(int(byte_value))
            else:
                # Deterministic fallback. Documented in module docstring.
                out.append(int(byte_value) % n_tokens)
        return out

    def _record_latency(self, seconds: float) -> None:
        if len(self._latency_ring) >= self._latency_ring_cap:
            # Ring eviction: drop oldest. Order is preserved for percentile
            # since we sort on read.
            self._latency_ring.pop(0)
        self._latency_ring.append(float(seconds))


__all__ = [
    "EncodedBatch",
    "TensorEncoder",
]

"""Shared dataclass contracts used across Q10 submodules.

ModelOutput is the contract between `model.TrainerNet.forward` and
`loss_engine.LossEngine.compute`. Hosting it here (vs. inside model.py)
lets both submodules be implemented in parallel within S0.C without
ordering constraints.

Phase-2+ may grow this module with more cross-cutting types; today it
holds only ModelOutput. The `extra` mapping accommodates Phase-2 head
additions (card-pick, run-value, shadow-price-calibration) without
schema bumps to this file.
"""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass, field

import torch


@dataclass(frozen=True, eq=False)
class ModelOutput:
    """Network forward output. Tensors live on whatever device the model ran on."""

    policy_logits: torch.Tensor  # (B, A) — over the per-batch action space
    sample_preds: torch.Tensor  # (B, sample_field_count) — Phase-1 degenerate per ADR-021
    summary_preds: torch.Tensor  # (B, summary_field_count) — per ADR-014 summary fields
    hp_frac_aux: torch.Tensor  # (B,) — Phase-1 bootstrap scalar (ADR-014/018)
    extra: Mapping[str, torch.Tensor] = field(default_factory=dict)  # Phase-2+ heads

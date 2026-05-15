"""Tests for ``pipeline.trainer.model`` (S0.C.α).

Covers the six unit tests called out in
``pipeline/trainer/docs/specs/modules/model.md`` §Testing Strategy plus an
ONNX-export integration check.
"""
from __future__ import annotations

import importlib.util
import io
import tempfile
from dataclasses import dataclass
from pathlib import Path

import pytest
import torch
from torch import nn

from pipeline.trainer.content_registry import ContentRegistry
from pipeline.trainer.model import TrainerNet
from pipeline.trainer.run_config import NetworkConfig
from pipeline.trainer.tensor_encoder import EncodedBatch


_REGISTRY_PATH = (
    Path(__file__).resolve().parents[3]
    / "contracts"
    / "registry"
    / "phase1-silent.json"
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------
@pytest.fixture(scope="module")
def registry() -> ContentRegistry:
    return ContentRegistry.load(_REGISTRY_PATH)


@pytest.fixture
def default_network_config() -> NetworkConfig:
    """Matches ``pipeline/trainer/config/local.json`` defaults.

    These are the values asserted to produce 5M ≤ params ≤ 15M.
    """
    return NetworkConfig(
        expected_token_count=None,
        d_model=256,
        n_layers=8,
        n_heads=8,
        ffn_dim=1024,
        max_seq_len=256,
        max_action_space=100,
    )


@pytest.fixture
def small_network_config() -> NetworkConfig:
    """Tiny config for fast forward/shape tests."""
    return NetworkConfig(
        expected_token_count=None,
        d_model=32,
        n_layers=2,
        n_heads=4,
        ffn_dim=64,
        max_seq_len=20,
        max_action_space=16,
    )


def _make_batch(
    *,
    batch_size: int,
    seq_len: int,
    action_space: int,
    vocab_size: int,
) -> EncodedBatch:
    """Construct an :class:`EncodedBatch` directly (bypasses TensorEncoder).

    Targets / prior_logits / macro_context shapes match what the encoder
    would emit but values are arbitrary — the model never reads them.
    """
    torch.manual_seed(0)
    tokens = torch.randint(0, vocab_size, (batch_size, seq_len), dtype=torch.long)
    padding_mask = torch.zeros((batch_size, seq_len), dtype=torch.bool)
    legal_action_mask = torch.zeros((batch_size, action_space), dtype=torch.bool)
    legal_action_mask[:, :max(1, action_space // 2)] = True
    policy_target = torch.zeros((batch_size, action_space), dtype=torch.float32)
    combat_sample_targets = torch.zeros((batch_size, 4), dtype=torch.float32)
    combat_summary_targets = torch.zeros((batch_size, 5), dtype=torch.float32)
    hp_frac_target = torch.zeros((batch_size,), dtype=torch.float32)
    prior_logits = torch.zeros((batch_size, action_space), dtype=torch.float32)
    macro_context = torch.zeros((batch_size, 11), dtype=torch.float32)
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
        metadata={},
    )


# ---------------------------------------------------------------------------
# 1. Parameter count within bounds
# ---------------------------------------------------------------------------
def test_param_count_within_bounds(
    registry: ContentRegistry, default_network_config: NetworkConfig
) -> None:
    net = TrainerNet(default_network_config, registry)
    count = sum(p.numel() for p in net.parameters())
    assert 5_000_000 <= count <= 15_000_000, f"param count out of range: {count}"


# ---------------------------------------------------------------------------
# 2. Forward shape contract
# ---------------------------------------------------------------------------
def test_forward_shape_contract(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    torch.manual_seed(0)
    net = TrainerNet(small_network_config, registry)
    batch = _make_batch(
        batch_size=4, seq_len=20, action_space=10, vocab_size=len(registry)
    )
    out = net(batch)
    assert out.policy_logits.shape == (4, 10)
    assert out.sample_preds.shape == (4, 4)
    assert out.summary_preds.shape == (4, 5)
    assert out.hp_frac_aux.shape == (4,)
    assert out.extra == {}


def test_forward_action_space_slicing(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    """Different per-batch action-spaces produce correctly sliced logits."""
    torch.manual_seed(0)
    net = TrainerNet(small_network_config, registry)
    for a in (1, 3, 8, small_network_config.max_action_space):
        batch = _make_batch(
            batch_size=2, seq_len=8, action_space=a, vocab_size=len(registry)
        )
        out = net(batch)
        assert out.policy_logits.shape == (2, a)


# ---------------------------------------------------------------------------
# 3. Deterministic forward
# ---------------------------------------------------------------------------
def test_deterministic_forward(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    batch = _make_batch(
        batch_size=4, seq_len=20, action_space=10, vocab_size=len(registry)
    )

    torch.manual_seed(42)
    net_a = TrainerNet(small_network_config, registry)
    net_a.eval()
    with torch.no_grad():
        out_a = net_a(batch)

    torch.manual_seed(42)
    net_b = TrainerNet(small_network_config, registry)
    net_b.eval()
    with torch.no_grad():
        out_b = net_b(batch)

    assert torch.equal(out_a.policy_logits, out_b.policy_logits)
    assert torch.equal(out_a.sample_preds, out_b.sample_preds)
    assert torch.equal(out_a.summary_preds, out_b.summary_preds)
    assert torch.equal(out_a.hp_frac_aux, out_b.hp_frac_aux)


# ---------------------------------------------------------------------------
# 4. Head registry isolates additions
# ---------------------------------------------------------------------------
def test_head_registry_isolates_additions(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    torch.manual_seed(0)
    net = TrainerNet(small_network_config, registry)
    # ``eval()`` so dropout-free determinism makes "unchanged" a sharp
    # claim. The head-registry contract is structural, not a dropout test.
    net.eval()
    batch = _make_batch(
        batch_size=2, seq_len=12, action_space=5, vocab_size=len(registry)
    )
    with torch.no_grad():
        out_before = net(batch)

    torch.manual_seed(7)
    phase2_head = nn.Linear(small_network_config.d_model, 3)
    net.register_head("phase2_card_pick", phase2_head)
    with torch.no_grad():
        out_after = net(batch)

    assert "phase2_card_pick" in out_after.extra
    assert out_after.extra["phase2_card_pick"].shape == (2, 3)

    # Existing heads unchanged bit-for-bit (head modules untouched).
    assert torch.equal(out_before.policy_logits, out_after.policy_logits)
    assert torch.equal(out_before.sample_preds, out_after.sample_preds)
    assert torch.equal(out_before.summary_preds, out_after.summary_preds)
    assert torch.equal(out_before.hp_frac_aux, out_after.hp_frac_aux)


def test_register_head_rejects_builtin_name(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    net = TrainerNet(small_network_config, registry)
    with pytest.raises(ValueError):
        net.register_head("policy", nn.Linear(small_network_config.d_model, 3))


def test_register_head_rejects_duplicate(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    net = TrainerNet(small_network_config, registry)
    net.register_head("aux", nn.Linear(small_network_config.d_model, 2))
    with pytest.raises(ValueError):
        net.register_head("aux", nn.Linear(small_network_config.d_model, 2))


# ---------------------------------------------------------------------------
# 5. Prior snapshot does not require grad
# ---------------------------------------------------------------------------
def test_compute_prior_logits_no_grad(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    torch.manual_seed(0)
    net = TrainerNet(small_network_config, registry)
    batch = _make_batch(
        batch_size=3, seq_len=10, action_space=6, vocab_size=len(registry)
    )
    net.snapshot_prior()
    logits = net.compute_prior_logits(batch)
    assert logits.requires_grad is False
    assert logits.shape == (3, 6)
    # Without the snapshot we still expect requires_grad=False (degenerate
    # path uses self).
    net2 = TrainerNet(small_network_config, registry)
    logits2 = net2.compute_prior_logits(batch)
    assert logits2.requires_grad is False


def test_snapshot_resets_age(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    net = TrainerNet(small_network_config, registry)
    assert net.prior_age_steps == 0
    net.increment_prior_age(5)
    assert net.prior_age_steps == 5
    net.snapshot_prior()
    assert net.prior_age_steps == 0


def test_prior_logits_match_current_after_snapshot(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    """Right after snapshot, prior logits must equal live logits bit-for-bit."""
    torch.manual_seed(0)
    net = TrainerNet(small_network_config, registry)
    batch = _make_batch(
        batch_size=2, seq_len=8, action_space=4, vocab_size=len(registry)
    )
    net.eval()
    net.snapshot_prior()
    with torch.no_grad():
        live = net(batch).policy_logits
    prior = net.compute_prior_logits(batch)
    assert torch.equal(live, prior)


# ---------------------------------------------------------------------------
# 6. state_dict round-trip
# ---------------------------------------------------------------------------
def test_state_dict_round_trip(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    torch.manual_seed(0)
    net_orig = TrainerNet(small_network_config, registry)
    net_orig.eval()
    batch = _make_batch(
        batch_size=2, seq_len=10, action_space=6, vocab_size=len(registry)
    )
    with torch.no_grad():
        out_orig = net_orig(batch)

    # Serialize -> randomize a fresh net (different seed) -> load -> match.
    buffer = io.BytesIO()
    torch.save(net_orig.state_dict(), buffer)

    torch.manual_seed(999)
    net_loaded = TrainerNet(small_network_config, registry)
    net_loaded.eval()
    buffer.seek(0)
    net_loaded.load_state_dict(torch.load(buffer))
    with torch.no_grad():
        out_loaded = net_loaded(batch)

    assert torch.equal(out_orig.policy_logits, out_loaded.policy_logits)
    assert torch.equal(out_orig.sample_preds, out_loaded.sample_preds)
    assert torch.equal(out_orig.summary_preds, out_loaded.summary_preds)
    assert torch.equal(out_orig.hp_frac_aux, out_loaded.hp_frac_aux)


# ---------------------------------------------------------------------------
# ONNX export validates (integration test 2 from model.md)
# ---------------------------------------------------------------------------
_HAS_ONNX = importlib.util.find_spec("onnx") is not None


@pytest.mark.skipif(not _HAS_ONNX, reason="onnx not installed")
def test_onnx_export_check_model(
    registry: ContentRegistry, small_network_config: NetworkConfig
) -> None:
    import onnx  # type: ignore

    torch.manual_seed(0)
    net = TrainerNet(small_network_config, registry)
    net.eval()
    batch = _make_batch(
        batch_size=2, seq_len=10, action_space=6, vocab_size=len(registry)
    )

    # ``torch.onnx.export`` works on plain tensor inputs. Build a wrapper
    # that takes ``tokens``+``padding_mask``+``legal_action_mask`` and
    # routes through forward.
    class _ExportWrap(nn.Module):
        def __init__(self, inner: TrainerNet, action_space: int) -> None:
            super().__init__()
            self.inner = inner
            self.action_space = action_space

        def forward(  # type: ignore[override]
            self,
            tokens: torch.Tensor,
            padding_mask: torch.Tensor,
            legal_action_mask: torch.Tensor,
        ) -> torch.Tensor:
            wrapped = EncodedBatch(
                tokens=tokens,
                padding_mask=padding_mask,
                legal_action_mask=legal_action_mask,
                policy_target=torch.zeros_like(legal_action_mask, dtype=torch.float32),
                combat_sample_targets=torch.zeros((tokens.shape[0], 4)),
                combat_summary_targets=torch.zeros((tokens.shape[0], 5)),
                hp_frac_target=torch.zeros((tokens.shape[0],)),
                prior_logits=torch.zeros_like(
                    legal_action_mask, dtype=torch.float32
                ),
                macro_context=torch.zeros((tokens.shape[0], 11)),
                metadata={},
            )
            return self.inner(wrapped).policy_logits

    wrap = _ExportWrap(net, action_space=batch.legal_action_mask.shape[-1])
    with tempfile.NamedTemporaryFile(suffix=".onnx", delete=False) as fh:
        path = Path(fh.name)
    try:
        torch.onnx.export(
            wrap,
            (batch.tokens, batch.padding_mask, batch.legal_action_mask),
            str(path),
            input_names=["tokens", "padding_mask", "legal_action_mask"],
            output_names=["policy_logits"],
            opset_version=17,
        )
        loaded = onnx.load(str(path))
        onnx.checker.check_model(loaded)
    finally:
        path.unlink(missing_ok=True)

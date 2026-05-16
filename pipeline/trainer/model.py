r"""Model submodule: :class:`TrainerNet` — embedding + transformer + heads.

Architecture per ``pipeline/trainer/docs/specs/modules/model.md``:

- Token embedding sized to the loaded :class:`ContentRegistry`.
- Learned positional embedding sized to ``network.max_seq_len``.
- ``n_layers`` × :class:`torch.nn.TransformerEncoderLayer`
  (``d_model``, ``n_heads``, ``ffn_dim``, ``batch_first=True``).
- Pool to ``(B, d_model)`` via first-token pooling
  (``output[:, 0, :]``) — chosen for Phase-1 simplicity, matches the
  CLS-style convention; spec offers mean-pool as the alternative.
- Multi-head output via ``self.heads: nn.ModuleDict``. Phase-1 heads
  registered at construction:

    * ``policy``        : ``Linear(d_model, max_action_space)`` — sliced
                          to ``(B, A)`` at forward time where
                          ``A == encoded_batch.legal_action_mask.shape[-1]``.
                          Loss masks unused slots via
                          ``encoded_batch.legal_action_mask``.
    * ``combat_sample`` : ``Linear(d_model, 4)``  — ADR-021 degenerate
                          single (hp_delta, survived, turns_taken, timeout).
    * ``combat_summary``: ``Linear(d_model, 5)``  — ADR-014 summary
                          (survival_probability, expected_hp_delta,
                          expected_turns, timeout_probability, uncertainty).
    * ``hp_frac_aux``   : ``Linear(d_model, 1)``  — squeezed to ``(B,)``.

Extra heads registered post-construction via :meth:`register_head` are
called in :meth:`forward` and packed into ``ModelOutput.extra``; the
four built-in heads above are addressed by fixed attribute names and do
NOT spill into ``extra`` (verified in test 4).

Prior-snapshot strategy
=======================
:meth:`snapshot_prior` builds a frozen sibling :class:`TrainerNet`
configured identically to ``self`` and ``load_state_dict``\s the current
weights into it. :meth:`compute_prior_logits` calls
``self._prior_net(...)`` under :func:`torch.no_grad`. This keeps the
training net's parameter buffers untouched between snapshot and the
next training-step backward pass (alternative: temporarily swap params
in-place — rejected as it risks correlated bugs with autograd state).
``self._prior_age_steps`` is the count since last snapshot (gauge fuel
for :data:`sts2_q10_model_prior_age_steps`); incremented elsewhere by
``train_driver``.

This module owns no I/O. Determinism follows from seeded weight
initialization upstream (``run_config.seed_everything``).
"""

from __future__ import annotations

import copy

import torch
from torch import nn

from pipeline.trainer.content_registry import ContentRegistry
from pipeline.trainer.run_config import NetworkConfig
from pipeline.trainer.tensor_encoder import EncodedBatch
from pipeline.trainer.types import ModelOutput

# ---------------------------------------------------------------------------
# Head dimensions (Phase-1 fixed per ADR-014 / ADR-021; mirrors tensor_encoder)
# ---------------------------------------------------------------------------
_SAMPLE_FIELD_COUNT: int = 4
_SUMMARY_FIELD_COUNT: int = 5
# Built-in head names handled out-of-band of ``extra``.
_BUILTIN_HEADS: frozenset[str] = frozenset(
    {"policy", "combat_sample", "combat_summary", "hp_frac_aux"}
)


class TrainerNet(nn.Module):
    """Phase-1 trainer network. See module docstring for architecture."""

    def __init__(
        self,
        network_config: NetworkConfig,
        content_registry: ContentRegistry,
    ) -> None:
        super().__init__()
        self._config = network_config
        self._registry = content_registry

        vocab_size = len(content_registry)
        if vocab_size == 0:
            raise ValueError(
                "TrainerNet: content_registry has zero tokens — refusing "
                "to construct an empty embedding table"
            )

        d_model = int(network_config.d_model)
        max_seq_len = int(network_config.max_seq_len)
        max_action_space = int(network_config.max_action_space)

        # Embeddings ---------------------------------------------------------
        self.token_embedding = nn.Embedding(vocab_size, d_model)
        self.position_embedding = nn.Embedding(max_seq_len, d_model)

        # Transformer trunk --------------------------------------------------
        encoder_layer = nn.TransformerEncoderLayer(
            d_model=d_model,
            nhead=int(network_config.n_heads),
            dim_feedforward=int(network_config.ffn_dim),
            batch_first=True,
        )
        self.encoder = nn.TransformerEncoder(encoder_layer, num_layers=int(network_config.n_layers))

        # Heads --------------------------------------------------------------
        self.heads: nn.ModuleDict = nn.ModuleDict()
        self.heads["policy"] = nn.Linear(d_model, max_action_space)
        self.heads["combat_sample"] = nn.Linear(d_model, _SAMPLE_FIELD_COUNT)
        self.heads["combat_summary"] = nn.Linear(d_model, _SUMMARY_FIELD_COUNT)
        self.heads["hp_frac_aux"] = nn.Linear(d_model, 1)

        # Prior-snapshot state ----------------------------------------------
        # Wrap the prior network in a list to keep it out of the parent's
        # ``_modules`` registry; otherwise its parameters would be tracked
        # for grad and would pollute :meth:`state_dict` for artifact-publish.
        self._prior_net_holder: list[TrainerNet] = []
        self._prior_age_steps: int = 0

    # ------------------------------------------------------------------
    # Public properties
    # ------------------------------------------------------------------
    @property
    def network_config(self) -> NetworkConfig:
        return self._config

    @property
    def prior_age_steps(self) -> int:
        """Steps since :meth:`snapshot_prior` was last called (gauge fuel)."""
        return self._prior_age_steps

    def increment_prior_age(self, n: int = 1) -> None:
        """Bump :attr:`prior_age_steps` (``train_driver`` calls this per step)."""
        self._prior_age_steps += int(n)

    # ------------------------------------------------------------------
    # Head registry
    # ------------------------------------------------------------------
    def register_head(self, name: str, module: nn.Module) -> None:
        """Add a Phase-2+ head. Output lands in ``ModelOutput.extra[name]``.

        Built-in head names are reserved; re-registering them raises.
        """
        if name in _BUILTIN_HEADS:
            raise ValueError(f"register_head: cannot override built-in head {name!r}")
        if name in self.heads:
            raise ValueError(f"register_head: duplicate head name {name!r}")
        self.heads[name] = module

    # ------------------------------------------------------------------
    # Forward
    # ------------------------------------------------------------------
    def forward(self, encoded_batch: EncodedBatch) -> ModelOutput:
        pooled = self._pool(encoded_batch)
        return self._run_heads(pooled, encoded_batch)

    def _pool(self, encoded_batch: EncodedBatch) -> torch.Tensor:
        """Embed + transformer + first-token pool → ``(B, d_model)``."""
        tokens = encoded_batch.tokens  # (B, T) LongTensor
        padding_mask = encoded_batch.padding_mask  # (B, T) BoolTensor, True=pad

        b, t = tokens.shape
        device = tokens.device

        positions = torch.arange(t, device=device).unsqueeze(0).expand(b, t)
        h = self.token_embedding(tokens) + self.position_embedding(positions)
        h = self.encoder(h, src_key_padding_mask=padding_mask)
        # First-token pooling. The token at position 0 is guaranteed
        # to be non-padded for any non-empty rich_state (tensor_encoder
        # left-aligns ids and only right-pads). For all-padded edge
        # rows the first position is still defined; loss masking elsewhere
        # is the safety net.
        return h[:, 0, :]

    def _run_heads(self, pooled: torch.Tensor, encoded_batch: EncodedBatch) -> ModelOutput:
        """Apply every registered head; pack built-ins + ``extra``."""
        policy_full = self.heads["policy"](pooled)  # (B, max_action_space)
        action_space = int(encoded_batch.legal_action_mask.shape[-1])
        policy_logits = policy_full[:, :action_space].contiguous()

        sample_preds = self.heads["combat_sample"](pooled)  # (B, 4)
        summary_preds = self.heads["combat_summary"](pooled)  # (B, 5)
        hp_frac_aux = self.heads["hp_frac_aux"](pooled).squeeze(-1)  # (B,)

        extra: dict[str, torch.Tensor] = {}
        for name, head in self.heads.items():
            if name in _BUILTIN_HEADS:
                continue
            extra[name] = head(pooled)

        return ModelOutput(
            policy_logits=policy_logits,
            sample_preds=sample_preds,
            summary_preds=summary_preds,
            hp_frac_aux=hp_frac_aux,
            extra=extra,
        )

    # ------------------------------------------------------------------
    # Prior-policy snapshot (KL term support)
    # ------------------------------------------------------------------
    @property
    def _prior_net(self) -> TrainerNet | None:
        return self._prior_net_holder[0] if self._prior_net_holder else None

    def snapshot_prior(self) -> None:
        """Deep-copy current weights into a frozen sibling :class:`TrainerNet`.

        Resets :attr:`prior_age_steps` to 0. Subsequent
        :meth:`compute_prior_logits` calls evaluate against this snapshot.
        """
        if not self._prior_net_holder:
            prior = TrainerNet(self._config, self._registry)
            # Mirror any Phase-2+ heads that have been registered. We
            # copy them structurally so the prior net's state_dict shape
            # matches the live net's. ``deepcopy`` is acceptable here —
            # it runs at snapshot cadence, not per step.
            for name, head in self.heads.items():
                if name not in _BUILTIN_HEADS:
                    prior.register_head(name, copy.deepcopy(head))
            self._prior_net_holder.append(prior)
        prior_net = self._prior_net_holder[0]
        prior_net.load_state_dict(self.state_dict())
        for p in prior_net.parameters():
            p.requires_grad_(False)
        prior_net.eval()
        self._prior_age_steps = 0

    def compute_prior_logits(self, encoded_batch: EncodedBatch) -> torch.Tensor:
        """Return ``(B, A)`` prior policy logits under :func:`torch.no_grad`.

        If :meth:`snapshot_prior` has not yet been called, the live
        network's weights serve as the (degenerate) prior — KL=0 for that
        step. The returned tensor has ``requires_grad=False``.
        """
        net = self._prior_net if self._prior_net is not None else self
        was_training = net.training
        net.eval()
        try:
            with torch.no_grad():
                pooled = net._pool(encoded_batch)
                policy_full = net.heads["policy"](pooled)
                action_space = int(encoded_batch.legal_action_mask.shape[-1])
                logits = policy_full[:, :action_space].contiguous()
        finally:
            if was_training:
                net.train()
        # Belt-and-braces: detach guarantees no autograd graph linkage even
        # if a future caller wraps this in an enable_grad block.
        return logits.detach()


__all__ = ["TrainerNet"]

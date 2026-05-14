# Submodule: loss-engine

> Multi-head loss assembly. Phase-1: policy CE on legal mask, sample-pred,
> summary-pred, hp-frac-aux, KL-vs-prior. L2 delegated to `optim`'s
> AdamW weight_decay. Head-loss registry per Q10-ADR-001 for Phase-2
> additions.

## Responsibilities

- Maintain a head-loss registry: `register_head(name: str, fn:
  Callable[[ModelOutput, EncodedBatch], torch.Tensor], weight: float)`.
  Phase-1 registered heads:
  - `policy` — cross-entropy of `policy_logits` against `policy_target`
    masked by `legal_action_mask`. Illegal action logits set to `-inf`
    before softmax.
  - `combat_sample` — Phase-1 degenerate per ADR-014/Q3-ADR-005:
    MSE between predicted single-sample fields and the target sample
    fields. Phase-2+: K-sample distributional loss (e.g., per-sample
    weighted NLL).
  - `combat_summary` — multi-component loss across summary fields
    (survival probability via BCE; expected HP delta via MSE; quantiles
    via pinball loss).
  - `hp_frac_aux` — MSE between scalar prediction and bootstrap target;
    weight set very low Phase-1 (e.g., 0.05); deprecated Phase-2+.
  - `kl_vs_prior` — KL divergence of current `policy_logits` softmax
    against prior-snapshot logits (computed via
    `model.compute_prior_logits`). Anchors stability per scaling-strategy
    §3 Phase 1.
- Compute total loss as `Σ weight_i × loss_i` plus gradient
  diagnostics (per-head gradient norms — Phase-4 PCGrad consumer).
- Return `LossResult(total, components, weights, gradient_diagnostics)`:
  - `total: torch.Tensor` — backward-able scalar.
  - `components: dict[str, float]` — per-head detached scalar losses
    for Q7 metrics.
  - `weights: dict[str, float]` — current head weights (Phase-2+ weight
    schedulers read these).
  - `gradient_diagnostics: dict[str, float]` — per-head ‖∇‖₂ (computed
    only when a Phase-4 PCGrad flag is set; default off for throughput).
- Be a pure function aside from the registry — same `ModelOutput` +
  `EncodedBatch` → same `LossResult` (modulo PyTorch RNG, which the
  training loop should keep deterministic).

Out of scope: parameter updates (`optim`); freeze-unfreeze schedules
(Phase-2+; lives in `train_driver` or a future
`schedule` submodule); gradient projection itself (Phase-4 wraps
`optim.step`, consumes the diagnostics published here).

## Data Ownership

In-process, immutable after bootstrap:

- Head-loss registry: `dict[str, (fn, weight)]`.
- KL coefficient β — read from `RunConfig.loss_weights.kl_beta`.
- L2 coefficient — read from `RunConfig.optim.weight_decay` and
  exposed read-only for cross-reference (the actual decay is applied
  by AdamW in `optim`).

Schema-owning? **No.**

## Communication

**External:** none.

**Internal (in-process function calls, sync):**

- `LossEngine(config: RunConfig, model: TrainerNet) -> LossEngine` —
  constructed once.
- `register_head(name, fn, weight) -> None` — at bootstrap; Phase-2+
  for additions.
- `compute(model_output: ModelOutput, encoded_batch: EncodedBatch) ->
  LossResult` — per training step.

**Metrics (emitted via `train_driver` reading the LossResult components):**

- `sts2_q10_loss_total` — gauge; current step total.
- `sts2_q10_loss_component{head="policy"|"combat_sample"|...}` —
  gauge per head.
- `sts2_q10_kl_to_prior` — gauge; KL divergence to prior snapshot.

## Coupling

- **Afferent (in):** `train_driver` (per-step caller).
- **Efferent (out):** `model.compute_prior_logits` for KL; PyTorch
  loss primitives.
- **Indirect:** `optim` (consumes the returned `total.backward()`);
  Q7 (metrics via `train_driver`).

## Phase-1 degenerate-sample loss formula

Per Q3-ADR-005, Phase-1 trajectory rows carry
`combat_outcome_samples = [Sample(probability_weight=1.0,
hp_delta=summary.expected_hp_delta, …)]`. The `combat_sample` head loss
reduces to MSE on the single sample's `hp_delta` against the network's
predicted single sample. This is mathematically equivalent to a scalar
MSE on `expected_hp_delta`, but the shape contract (B, 1, …) is
preserved so Phase-2 multi-sample data flows through unchanged.

## Testing Strategy

### Unit

1. **Policy CE ignores illegal actions.** Set `legal_action_mask` False
   on action 3 of 10; assert no gradient flows to `policy_logits[:, 3]`.
   Absent test: the policy learns to play illegal moves.
2. **Hand-computed reference on tiny batch.** 2×3 synthetic batch with
   known logits/targets → `compute(...)` returns a `total` matching the
   hand-derived value to within float tolerance. Absent test: silent
   loss-function regressions go undetected.
3. **KL term zero when prior == current.** Snapshot prior, do no
   gradient update, recompute → KL term ≈ 0 (numerical noise only).
   Absent test: KL gradients flow even at convergence.
4. **Phase-1 degenerate sample loss is well-formed.** Single-sample
   target + single-sample prediction → MSE finite, gradient flows.
   Verifies the Q3-ADR-005 reader contract here.
5. **Weight registry change updates total weight in next call.**
   Set `weights["hp_frac_aux"] = 0.0` → `total` drops the hp-frac-aux
   contribution on the next call. Verifies the Phase-2 weight-scheduler
   seam.
6. **Gradient diagnostics on demand.** Enable PCGrad flag → `gradient_diagnostics`
   populated; disabled → empty dict, no extra cost. Verifies Phase-4
   readiness without Phase-1 burden.

### Integration

1. **End-to-end backward pass.** `loss_engine.compute(...).total.backward()`
   produces non-NaN gradients on all `TrainerNet` parameters that
   require_grad. Verifies the wire between `model` and `optim`.
2. **100-step toy convergence.** Synthetic batch repeated 100 times
   under default Phase-1 weights → loss strictly decreases for ≥80
   of 100 steps. Verifies the loss is learnable (catches sign errors).
3. **Phase-1 vs. Phase-2 head set is hot-swappable.** Register a stub
   Phase-2 head, call `compute(...)` → result includes the new
   component; deregister → reverts. Verifies the evolvability claim.

### Smoke (mandatory)

- N/A for direct `/health` schema.

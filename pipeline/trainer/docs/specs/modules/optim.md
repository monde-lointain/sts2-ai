# Submodule: optim

> AdamW + LR scheduler + gradient clipping. Owns optimizer + scheduler
> state across the run (ephemeral; persisted only via `artifact_publisher`).
> L2 weight decay is the AdamW `weight_decay` per
> `scaling-strategy.md` §3 Phase 1.

## Responsibilities

- Construct an AdamW optimizer over `TrainerNet` parameters per
  `RunConfig.optim`: `lr`, `betas`, `eps`, `weight_decay` (the L2 term),
  parameter-group split (Phase-1 single group; Phase-2+ freeze/unfreeze
  toggles `requires_grad` on per-head groups).
- Construct an LR scheduler (Phase 1: cosine with warmup; configurable).
  Step the scheduler after every optimizer step.
- Apply gradient clipping by global norm (`torch.nn.utils.clip_grad_norm_`)
  at the configured threshold (`RunConfig.optim.clip_grad_norm`,
  default 1.0).
- Expose `step(loss: torch.Tensor) -> StepStats`:
  - `loss.backward()` (Phase-1 single-loss; Phase-4 PCGrad replaces this
    with a per-head backward + projection).
  - clip gradients.
  - `optimizer.step()`.
  - `scheduler.step()`.
  - `optimizer.zero_grad(set_to_none=True)`.
  - Return `StepStats(grad_norm, lr, weight_norm, momentum_norm)` for
    metric emission.
- Support `state_dict()` / `load_state_dict()` for atomic checkpoint
  publish/resume by `artifact_publisher`.
- Phase-4 hook: expose `pcgrad_wrap(active: bool)` that swaps the
  `step()` implementation for a per-head-gradient-projection version
  that consumes `loss_engine`'s `gradient_diagnostics`. Phase-1 default:
  off.

Out of scope: loss computation (`loss_engine`); freeze-unfreeze schedule
logic (lives in `train_driver` / a future schedule submodule; `optim`
only honors `requires_grad`).

## Data Ownership

In-process, mutable across steps:

- AdamW optimizer state — first/second-moment running averages per
  parameter. Persisted to checkpoints.
- LR scheduler state — step count, current LR. Persisted to checkpoints.
- EMA loss (diagnostic) — exponential moving average of total loss for
  Q7 emission. Not persisted (rebuildable).

Schema-owning? **No.** Checkpoint binary contents are owned by
`artifact_publisher`; this submodule exposes a `state_dict()` only.

## Communication

**External:** none.

**Internal (in-process function calls, sync):**

- `OptimController(net: TrainerNet, config: RunConfig.optim) ->
  OptimController` — constructed once.
- `step(loss: torch.Tensor) -> StepStats` — per training step.
- `state_dict()` / `load_state_dict(...)` — by `artifact_publisher`.
- `set_requires_grad(group_name: str, value: bool) -> None` — Phase-2+
  for freeze-unfreeze.

**Metrics (emitted via `train_driver`):**

- `sts2_q10_grad_norm` — gauge; global gradient norm post-clip.
- `sts2_q10_lr` — gauge; current learning rate.
- `sts2_q10_weight_norm` — gauge; ℓ₂ norm of all parameters.
- `sts2_q10_grad_clip_fired_total` — counter; how often clipping
  truncated the gradient (informs whether `clip_grad_norm` is right-sized).

## Coupling

- **Afferent (in):** `train_driver` (per-step caller); `artifact_publisher`
  (state_dict).
- **Efferent (out):** PyTorch optimizer + scheduler primitives.
- **Indirect:** `loss_engine` (whose `total` is passed in); `model`
  (whose parameters are being optimized); Q7 (metrics).

## Testing Strategy

### Unit

1. **LR schedule reaches warmup peak then decays.** 1000-step schedule
   with 100-step warmup → LR at step 100 is the peak; LR at step 1000
   is near the floor. Absent test: silent LR misconfiguration.
2. **Gradient clipping caps at configured norm.** Construct a backward
   that produces gradient norm 10 with `clip_grad_norm=1.0` →
   post-step `StepStats.grad_norm ≤ 1.0`. Absent test: gradients
   blow up unbounded.
3. **`state_dict` round-trip preserves momentum.** Run 10 steps →
   `sd = optim.state_dict()` → construct a fresh `OptimController`
   and load → run 1 more step → result matches the original step-11
   output bit-equal. Absent test: resume from checkpoint silently
   re-initializes momentum.
4. **`set_requires_grad` freezes a parameter group.** Toggle off →
   gradient stays at 0 for that group after backward; toggle on →
   gradient flows. Verifies the Phase-2 freeze-unfreeze seam.
5. **`zero_grad(set_to_none=True)` releases gradient buffers.** After
   step, `parameter.grad is None` for all parameters. Absent test:
   gradient memory leaks across steps.

### Integration

1. **Toy convergence on synthetic loss.** Quadratic loss centered at a
   random point → 200 steps reduce loss by ≥3 orders of magnitude.
   Verifies AdamW + scheduler are wired correctly.
2. **Resume from checkpoint matches uninterrupted training.** Run 100
   steps; resume from step-50 checkpoint → step-100 weights match.
   Verifies the checkpoint contract.
3. **PCGrad wrap is a no-op when disabled.** With `pcgrad_wrap(active=False)`,
   step output is identical to the default. Verifies Phase-4 readiness
   does not perturb Phase-1.

### Smoke (mandatory)

- N/A for direct `/health` schema.

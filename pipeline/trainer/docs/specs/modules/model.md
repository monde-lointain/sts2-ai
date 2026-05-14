# Submodule: model

> The neural network: token embedding + 6–8 transformer blocks + multi-head
> output (policy / combat-sample / combat-summary / hp-frac-aux). Phase-2-ready
> head registry. PyTorch `nn.Module`.

## Responsibilities

- Define `TrainerNet(nn.Module)` per `scaling-strategy.md` §3.1:
  - Token embedding table sized to the loaded `ContentRegistry`
    (~500–1000 tokens Phase 1).
  - 6–8 transformer blocks, 128–256 dim, ~10 M parameters total.
  - Multi-head output: `policy` (over legal actions), `combat_sample`
    (per ADR-014; Phase-1 single-sample shape), `combat_summary`
    (per ADR-014 summary fields), `hp_frac_aux` (scalar in [0,1] —
    Phase-1 bootstrap only, ADR-014 / ADR-018).
- Expose a head registry: `register_head(name: str, module: nn.Module)`
  / `forward` looks up registered heads and packs results into
  `ModelOutput(policy_logits, sample_preds, summary_preds, hp_frac_aux,
  extra={...})`. Phase 2+ registrations add card-pick, run-value,
  shadow-price-calibration heads without touching `forward`.
- Support `state_dict()` / `load_state_dict()` for atomic publish to
  Q5 (called by `artifact_publisher`) and for resume-from-parent at
  bootstrap.
- Expose a single `forward(encoded_batch: EncodedBatch) -> ModelOutput`
  method. No internal autograd graph mutation outside this method.
- Provide a `compute_prior_logits()` helper that runs `forward` under
  `torch.no_grad()` with the prior-version weights for the KL penalty.
  (Phase 1: the prior is the previous step's weights, snapshotted at
  N-step intervals; Phase 2+: separate frozen prior network.)

Out of scope: loss computation (`loss_engine`); optimizer (`optim`);
ONNX export (`artifact_publisher`); device placement / DDP wrapping
(`train_driver` wraps in `DistributedDataParallel` when `world_size>1`).

## Data Ownership

In-process, mutable across steps:

- `nn.Module` parameters — the trained weights. Persisted only through
  `artifact_publisher` to Q5.
- Head registry — `dict[str, nn.Module]` constructed once at bootstrap;
  membership immutable for the run.
- Prior-policy snapshot — `state_dict` taken at configurable cadence
  (e.g., every 1000 steps) for the KL penalty. Lives in memory; not
  persisted.

Schema-owning? **No.** ONNX op-set version is stamped in the provenance
manifest by `artifact_publisher`, not owned here.

## Communication

**External:** none.

**Internal (in-process function calls, sync):**

- `TrainerNet(network_config: NetworkConfig, content_registry:
  ContentRegistry) -> TrainerNet` — constructed once.
- `register_head(name: str, module: nn.Module) -> None` — called at
  bootstrap by service.py for each Phase-1 head; Phase-2+ adds more.
- `forward(encoded_batch: EncodedBatch) -> ModelOutput` — per-step.
- `compute_prior_logits(encoded_batch) -> torch.Tensor` — called by
  `loss_engine` for the KL term.
- `snapshot_prior() -> None` — called by `train_driver` on cadence;
  snapshots the current weights to the prior buffer.
- `state_dict()` / `load_state_dict()` — for `artifact_publisher`
  round-trip.

**Metrics:**

- `sts2_q10_model_param_count` — gauge; set once at startup.
- `sts2_q10_model_prior_age_steps` — gauge; how many training steps
  since the prior was snapshotted (informs KL stability).

## Coupling

- **Afferent (in):** `train_driver` (forward, snapshot), `loss_engine`
  (compute_prior_logits), `artifact_publisher` (state_dict).
- **Efferent (out):** PyTorch (`torch.nn`, `torch.jit` for the ONNX
  export hook).
- **Indirect:** Q7 (scrapes metrics), Q5 (state_dict shape becomes the
  artifact payload).

## Testing Strategy

### Unit

1. **Parameter count within bounds.** Constructed with Phase-1 config
   → `sum(p.numel() for p in net.parameters())` is between 5M and 15M.
   Absent test: silent drift toward over-large networks that violate
   the throughput target.
2. **Forward shape contract.** `EncodedBatch[B=4, T=20, A=10]` →
   `ModelOutput.policy_logits.shape == (4, 10)`,
   `summary_preds.shape == (4, summary_field_count)`. Absent test:
   downstream loss code silently broadcasts and produces wrong gradients.
3. **Deterministic forward.** Seeded weights + same input → bit-equal
   output across runs. Absent test: training is not reproducible.
4. **Head registry isolates additions.** Register a no-op head named
   `"phase2_card_pick"` → `ModelOutput.extra["phase2_card_pick"]`
   present; existing heads unchanged. Verifies the Q10-ADR-001
   evolvability claim.
5. **Prior snapshot does not require grad.** `compute_prior_logits`
   runs without populating the autograd graph (verified via
   `torch.is_grad_enabled()` inside the call). Absent test: KL term
   pollutes the main backward pass.
6. **`state_dict` round-trip.** Save → randomize → load → forward output
   matches the saved network's output bit-equal. Absent test: artifact
   publish corrupts weights.

### Integration

1. **End-to-end forward from `tensor_encoder`.** Real encoded batch →
   `net.forward(...)` produces all head outputs in expected shapes.
   No NaN, no inf. Verifies the upstream contract.
2. **ONNX export passes `onnx.checker.check_model`.** Phase-1 net →
   `torch.onnx.export(...)` → `onnx.load(...)` → `onnx.checker.check_model(...)`
   succeeds. Verifies the artifact-publisher hook's input is valid.
3. **DDP wrap is a one-line addition.** With `world_size=2` (mocked),
   `nn.parallel.DistributedDataParallel(net)` constructs without
   modification to `model`. Verifies Phase-4 readiness without
   Phase-1 burden.

### Smoke (mandatory)

- N/A for direct `/health` schema; the model is exercised only when
  the training loop runs.

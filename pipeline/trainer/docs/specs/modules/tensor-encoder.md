# Submodule: tensor-encoder

> Pure function: `TrajectoryStep` → model-ready tensors. Holds the frozen
> `ContentRegistry` loaded from the parent Q5 artifact. No I/O. No state
> mutation after bootstrap (per Q10-ADR-008).

## Responsibilities

- Construct `ContentRegistry` from the Q4 bundle bytes loaded by
  `artifact_publisher.load_parent()`. Validate against
  `RunConfig.network.expected_token_count` if specified.
- Encode a list of `TrajectoryStep` into an `EncodedBatch`:
  - `tokens: torch.LongTensor[B, T]` — variable-length RichState rendered
    via the token table.
  - `padding_mask: torch.BoolTensor[B, T]` — True where padded.
  - `legal_action_mask: torch.BoolTensor[B, A]` — True for legal actions
    in each step (A = max action-space size of the batch).
  - `policy_target: torch.FloatTensor[B, A]` — search policy distribution
    over legal actions; zero on illegal.
  - `combat_sample_targets` — Phase-1 degenerate-single per Q3-ADR-005:
    a `(B, sample_field_count)` tensor populated from
    `step.combat_outcome_samples[0]`. Phase-2+: `(B, K, …)` real samples.
  - `combat_summary_targets: torch.FloatTensor[B, summary_field_count]`
    — per ADR-014 fields.
  - `hp_frac_target: torch.FloatTensor[B]` — bootstrap auxiliary signal
    (Phase 1 only — ADR-014 / ADR-018).
  - `prior_logits: torch.FloatTensor[B, A]` — for the KL penalty against
    prior policy. Phase-1: the `search_policy` from the trajectory step
    itself (the worker's MCTS prior at rollout time).
- Apply hidden-state filter per ADR-016: assert no `SOURCE_PERFECT`
  fields reach the encoder. Q3 should filter at ingest, but Q10 emits
  a P0 metric counter if any leak through (audit-defense per
  `docs/specs/modules/observability.md:48`).
- Compose `macro_context` projection: Phase-1 zero-stub (11 floats per
  v1.1 schema — 9 original v1.0 fields plus `sp(gold)` and `sp(MaxHP)`
  added per ADR-019 Decision 1). Phase-2+ learned head per ratified
  ADR-019 hybrid derivation; shadow-price encoding per ADR-015.
- Be a pure function: same input → same output, no randomness,
  deterministic device placement (CPU here; `train_driver` moves to GPU).

Out of scope: batching across steps from different trajectories (the
caller — `train_driver` — supplies a list); GPU placement (`train_driver`
calls `.to(device)`); model forward (`model`).

## Data Ownership

In-process, frozen at bootstrap (per Q10-ADR-008):

- `ContentRegistry` — token table, content_hash, schema_version. Bytes
  re-attached unchanged when `artifact_publisher` publishes a new artifact.
- `TokenVocabulary` — derived map `name → token_id` and
  `(kind, content_hash) → token_id`, plus the inverse for debug logging.

Schema-owning? **No.** Q4 schema is owned cross-quantum by the Content
Registry workflow; Q10 only consumes the frozen bundle.

## Communication

**External:** none.

**Internal (in-process function calls, sync):**

- `TensorEncoder(content_registry: ContentRegistry, config:
  RunConfig.network) -> TensorEncoder` — construct once; immutable.
- `encode_batch(steps: list[TrajectoryStep]) -> EncodedBatch` — called
  by `train_driver` per training step. Pure function. CPU tensors.

**Metrics:**

- `sts2_q10_encode_steps_total` — counter; total steps encoded.
- `sts2_q10_source_perfect_leak_total` — counter; P0 alert if nonzero
  per ADR-016.
- `sts2_q10_encode_seconds_p99` — gauge; updated by a Welford-style
  moving estimator inside the encoder. (Phase-2 upgrades to a histogram.)

## Coupling

- **Afferent (in):** `train_driver` (per-step), `artifact_publisher`
  (constructs once at bootstrap).
- **Efferent (out):** `pipeline.common.trajectory_proto` (typed access
  to TrajectoryStep fields); PyTorch (tensor construction).
- **Indirect:** Q7 (scrapes metrics).

## Testing Strategy

### Unit

1. **Deterministic encode.** Fixed `TrajectoryStep` + fixed registry →
   encoded tensors bit-equal across runs. Absent test: training is not
   reproducible.
2. **Padding mask correctness.** Batch of 3 steps with token sequences
   of length 10, 20, 15 → padded to `[3, 20]`, mask True for positions
   ≥ each step's actual length. Absent test: model attention leaks
   across padding.
3. **Legal-action mask shape.** Step with 5 legal actions out of 20 →
   `legal_action_mask` has exactly 5 True values; corresponding
   `policy_target` sums to 1.0 over True positions and is 0 over False.
   Absent test: the policy-CE loss ignores or amplifies illegal actions.
4. **Phase-1 degenerate sample handling.** Step with
   `combat_outcome_samples = [Sample(probability_weight=1.0,
   hp_delta=summary.expected_hp_delta, ...)]` → `combat_sample_targets`
   row equals the summary fields. Verifies Q3-ADR-005 reader convention.
5. **`SOURCE_PERFECT` leak alarms.** A constructed step with
   `observability_regime == SOURCE_PERFECT` → encoder raises (or, if
   configured to soft-fail, increments the counter and clears the
   field). Verifies ADR-016 audit-defense.
6. **Unknown token id falls back gracefully.** Step references a
   token_id absent from the loaded `ContentRegistry` (mismatch between
   trajectory generation and parent artifact) → `RuntimeError` with a
   clear message naming the missing token. Absent test: silent
   encoding into a wrong embedding slot.

### Integration

1. **Round-trip with `data_ingest`.** Real protobuf trajectory →
   `data_ingest` parses → `tensor_encoder.encode_batch` produces tensors
   that `model.forward` accepts (shape contract). Verifies the upstream
   contract.
2. **ContentRegistry version stamp.** Load registry version `V` at
   bootstrap; encoded `EncodedBatch.metadata.content_registry_sha`
   matches `V.content_hash`. Verifies Q10-ADR-008 reproducibility chain.

### Smoke (mandatory)

- N/A for direct `/health` schema; `tensor_encoder` is a pure submodule
  exercised only when the training loop is fed data.

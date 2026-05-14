# Module: Trainer (Q10)

> PyTorch training loop. Reads Q3, computes losses, publishes checkpoints to Q5. Stateless across runs (state lives in Q5 + Q3).

## Responsibilities

- Sample minibatches from Q3 under the configured sampling mode.
- Compute the AlphaZero-style training loss for combat policy: cross-entropy on policy, sample-prediction loss + summary-prediction loss on combat outcomes (per ADR-014), L2 weight decay, KL penalty against the prior policy for stability (`scaling-strategy.md` §3 Phase 1). HP-fraction prediction is an **auxiliary** loss head, not the primary value-head training target (per ADR-014, ADR-018 — combat does not bake reward value, so HP-fraction stays as a diagnostic and Phase-1 bootstrap target).
- Compute multi-head losses in Phase 2+ (card-pick categorical, run-value MSE, calibration penalty, `macro_context` shadow-price calibration per ADR-015).
- Use **oracle-agreement signal from Q2** to upweight states where the network disagrees with expectimax (priority replay scaling). Per ADR-017 carve-out: oracle-agreement remains a training-eligible labeled comparison; *path-counterfactual* signals from Q12 stay observational and do NOT feed training.
- **Publish checkpoints** to Q5 on a fixed cadence (e.g., every N steps + every M minutes, atomic via temp+rename).
- **Evaluate before promoting:** the trainer does not promote artifacts. It publishes; promotion goes through the workflow that talks to Q12 + reviewer sign-off (per ADR-007).

Out of scope: dataset storage (Q3); artifact storage (Q5); deciding sampling priorities at the data layer (Q3 owns the index, trainer chooses among modes).

## Data Ownership

None persistent. Trainer state during a run is ephemeral:

- Optimizer state (AdamW).
- LR scheduler state.
- Gradient accumulation buffers.
- Sampling-mode config (read from a run-config file, not stored).

Published checkpoints are owned by Q5.

## Communication

- **Sync — read from Q3:** sampling RPC. Trainer pulls minibatches; backpressure from trainer side, not Q3 side.
- **Sync — read from Q4:** load registry packaged with whichever artifact this run extends (or a fresh registry for a from-scratch run).
- **Sync — read from Q2:** oracle-agreement signal; either via a sideband table in Q3 or a direct RPC depending on Phase 1 implementation choice.
- **Sync — publish to Q5:** atomic checkpoint publish.
- **Pull — metrics:** Q7 scrapes loss curves, gradient norms, sampling-mode breakdowns, throughput.

## Coupling

- **Afferent (in):** none. Trainers are launched on demand by humans / orchestration.
- **Efferent (out):** Q2 (oracle agreement read), Q3 (sampling), Q4 (registry), Q5 (publish), Q7 (metrics).
- **Indirect:** GPU; experiment-tracking sidecar (W&B / MLflow per `scaling-strategy.md` §3.1 tooling).

## Phase Expectations

- **Phase 1.** Single GPU. Combat-only loss with sample-prediction + summary-prediction heads (per ADR-014); HP-fraction auxiliary loss bootstrapped from the existing Phase-1 scalar target. Uniform + priority sampling. KL penalty + L2.
- **Phase 2.** Add run-level heads. Combat policy frozen by default; unfrozen for joint fine-tuning *only after* meta-policy stabilizes (per ADR-009-amended Consequences). `macro_context` derivation policy per ADR-019 (deferred — bootstrap from prior iteration or heuristic curve until Phase-2 evidence ratifies).
- **Phase 3+.** Joint training of all worker heads + value function. Replay buffers per decision-type with balanced sampling. PCGrad / gradient projection if Phase 4+ shows gradient interference.

## Open Risks

- **Off-policy bias.** Workers run a slightly stale policy; the trainer must correct or accept the bias. Mitigation: weight refresh cadence tuned to keep on-policy window narrow; importance-sampling weights if drift is large.
- **Catastrophic forgetting.** Phase 2's meta-policy training degrades the combat policy if joint training starts too early. Mitigation: explicit freeze-unfreeze schedule; continuous combat-policy eval on the live deck distribution.
- **Compute / data starvation.** If Q3 cannot keep up, the trainer waits. Mitigation: monitor Q3 ingest rate vs trainer consumption rate; alert if ingest <consumption.
- **Hyperparameter drift across runs.** Mitigation: every artifact's hyperparameters live in Q5 metadata; CI compares distributions across recent runs.

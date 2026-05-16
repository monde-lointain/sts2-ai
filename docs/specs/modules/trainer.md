---
quantum: Q10
substrate: pipeline/trainer/
---

> Status legend: see ADR-023. Section badges = `[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`.

# Module: Trainer (Q10)

> PyTorch training loop. Reads Q3, computes losses, publishes checkpoints to Q5. Stateless across runs (state lives in Q5 + Q3).

## Responsibilities

- **[SHIPPED]** Sample minibatches from Q3 under the configured sampling mode (serial single-RPC prefetcher per Q10-ADR-002; `data_ingest.DataIngest`).
- **[SHIPPED]** Compute the AlphaZero-style training loss for combat policy: cross-entropy on policy, sample-prediction loss + summary-prediction loss on combat outcomes (per ADR-014), L2 weight decay, KL penalty against the prior policy for stability (`scaling-strategy.md` §3 Phase 1). HP-fraction prediction is an **auxiliary** loss head, not the primary value-head training target (per ADR-014, ADR-018 — combat does not bake reward value, so HP-fraction stays as a diagnostic and Phase-1 bootstrap target). Phase-1 `combat_sample` is the degenerate-single MSE per ADR-021. Registered heads: `policy`, `combat_sample`, `combat_summary`, `hp_frac_aux`, `kl_vs_prior` (`loss_engine.LossEngine._register_phase1_heads`).
- **[PHASE-2+]** Compute multi-head losses (card-pick categorical, run-value MSE, calibration penalty, `macro_context` shadow-price calibration per ADR-015, plus the learned sp head with autodiff/FD supervision against V_run derivatives per ADR-019). `LossEngine.register_head` is the extension point; no Phase-2+ heads are registered today.
- **[ASPIRATION (pre-implementation)]** Use **oracle-agreement signal from Q2** to upweight states where the network disagrees with expectimax (priority replay scaling). Per ADR-017 carve-out: oracle-agreement remains a training-eligible labeled comparison; *path-counterfactual* signals from Q12 stay observational and do NOT feed training. [NOTE: contradicts code at `pipeline/trainer/data_ingest.py`; `DataIngest` issues only `POST /sample` to Q3 with no Q2 RPC and no priority-replay weighting. Q10-ADR-020 (cross-quantum) routes the oracle-agreement sideband through Q3, but the consumer side is not yet wired.]
- **[SHIPPED]** **Publish checkpoints** on a fixed cadence (every N steps + every M minutes, atomic via temp+rename). [NOTE: Phase-1 publishes to a local directory under `data/trainer/runs/<run_id>/checkpoints/<step>/` per Q10-ADR-009 — Q5's `POST /artifacts` substrate is empty (`pipeline/model-registry/service.py` is a `raise SystemExit(main())` stub); local-write call swaps to a Q5 client when Q5 ships.]
- **[ASPIRATION (workflow not implemented)]** **Evaluate before promoting:** the trainer does not promote artifacts. It publishes; promotion goes through the workflow that talks to Q12 + reviewer sign-off (per ADR-007). [NOTE: contradicts code at `pipeline/trainer/artifact_publisher.py`; today the publisher writes every cadence-fired checkpoint with no Q12 coupling and no promotion gate. Per Q10-ADR-006, "any checkpoint is a promotion candidate" — the gating workflow is external and not yet built.]

Out of scope: dataset storage (Q3); artifact storage (Q5); deciding sampling priorities at the data layer (Q3 owns the index, trainer chooses among modes).

## Data Ownership

**[SHIPPED]** None persistent. Trainer state during a run is ephemeral:

- **[SHIPPED]** Optimizer state (AdamW) — `optim.OptimController`.
- **[SHIPPED]** LR scheduler state (held within `OptimController`).
- **[SHIPPED]** Gradient accumulation buffers (in `train_driver.TrainDriver`).
- **[SHIPPED]** Sampling-mode config (read from `RunConfig` at boot per `run_config.py`).

**[PHASE-2+]** Published checkpoints are owned by Q5. [NOTE: Phase-1 ownership is a local directory per Q10-ADR-009 until Q5 ships `POST /artifacts`; the manifest schema published today (`pipeline/trainer/manifest.py` v1) is the de facto wire spec Q5 will validate against.]

## Communication

- **[SHIPPED] Sync — read from Q3:** sampling RPC (`POST /sample`). Trainer pulls minibatches; backpressure from trainer side via bounded prefetch queue (`data_ingest.DataIngest`, Q10-ADR-002). 503-schema_drain retry honored per Q3 sampler recipe.
- **[SHIPPED] Sync — read from Q4:** load registry frozen at bootstrap (`content_registry.ContentRegistry.load`, Q10-ADR-008). [NOTE: Phase-1 reads the bundled `contracts/registry/phase1-silent.json` directly; "registry packaged with the parent artifact" is the Phase-2+ pathway after Q5 boots.]
- **[ASPIRATION (pre-implementation)] Sync — read from Q2:** oracle-agreement signal; either via a sideband table in Q3 (Q10-ADR-020) or a direct RPC. [NOTE: contradicts code at `pipeline/trainer/data_ingest.py`; no Q2 read path exists.]
- **[SHIPPED] Publish:** atomic checkpoint publish (temp + `os.replace`). [NOTE: Phase-1 target is a local directory per Q10-ADR-009, not Q5; one-method swap planned when `POST /artifacts` ships.]
- **[SHIPPED] Pull — metrics:** Q7 scrapes `/metrics` (Prometheus v0.0.4) for loss curves, gradient norms, sampling-mode breakdowns, throughput (`metrics_emitter.MetricsEmitter`).

## Coupling

- **[SHIPPED] Afferent (in):** none. Trainers are launched on demand by humans / orchestration (entry: `service.run`).
- **Efferent (out):**
  - **[ASPIRATION (pre-implementation)]** Q2 (oracle-agreement read).
  - **[SHIPPED]** Q3 (sampling, `POST /sample`).
  - **[SHIPPED]** Q4 (registry, bootstrap-frozen).
  - **[PHASE-2+ (local-dir today)]** Q5 (publish) — see Q10-ADR-009.
  - **[SHIPPED]** Q7 (metrics, scrape).
- **Indirect:**
  - **[SHIPPED]** GPU (PyTorch device — CPU fallback for smoke).
  - **[SHIPPED]** W&B sidecar via internal-queue daemon thread (Q10-ADR-007); disable-able via `wandb_enabled: false` for offline runs. MLflow is not implemented.

## Phase Expectations

- **[SHIPPED] Phase 1.** Single GPU (CPU fallback supported). Combat-only loss with sample-prediction + summary-prediction heads (per ADR-014); HP-fraction auxiliary loss bootstrapped from the existing Phase-1 scalar target. KL penalty + L2. [NOTE: Phase-1 today registers only uniform sampling on the Q3 client side; **priority sampling** is plumbed via Q3's sampler RPC but oracle-agreement-driven priority weighting is not wired (depends on the Q2 read path above). `macro_context` is a Phase-1 zero-stub per Q10-ADR-019 deferral — see `tensor_encoder.py` line 288 `torch.zeros((b, _MACRO_DIM))`.]
- **[PHASE-2]** Add run-level heads. Combat policy frozen by default; unfrozen for joint fine-tuning *only after* meta-policy stabilizes (per ADR-009-amended Consequences). `macro_context` derivation per ADR-019 (Accepted 2026-05-15): heuristic-curve warmup from oracle rollout statistics → learned sp head co-trained with V_run (autodiff first, FD fallback) → joint proximal reserve if (b) destabilizes. Fungibles only — HP, MaxHP, gold, per-potion-slot; no scalar sp(card)/sp(relic) per ADR-018.
- **[PHASE-3+]** Joint training of all worker heads + value function. Replay buffers per decision-type with balanced sampling. PCGrad / gradient projection if Phase 4+ shows gradient interference. [NOTE: PCGrad **diagnostic seam** is shipped — `LossEngine.enable_pcgrad_diag` populates per-head `‖∇‖₂`; the projection consumer is Phase-4.]

## Open Risks

- **Off-policy bias.** Workers run a slightly stale policy; the trainer must correct or accept the bias. Mitigation: weight refresh cadence tuned to keep on-policy window narrow; importance-sampling weights if drift is large.
- **Catastrophic forgetting.** Phase 2's meta-policy training degrades the combat policy if joint training starts too early. Mitigation: explicit freeze-unfreeze schedule; continuous combat-policy eval on the live deck distribution.
- **Compute / data starvation.** If Q3 cannot keep up, the trainer waits. Mitigation: monitor Q3 ingest rate vs trainer consumption rate; alert if ingest <consumption.
- **Hyperparameter drift across runs.** Mitigation: every artifact's hyperparameters live in Q5 metadata; CI compares distributions across recent runs.

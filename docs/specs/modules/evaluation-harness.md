# Module: Evaluation Harness (Q12)

> Runs the regression battery, ascension ladder, counterfactual evaluator, and exploit detector against artifacts in Q5. Writes reports to Q6.

## Responsibilities

- **Pinned regression battery.** Reproduce a fixed set of `(seed × encounter × deck)` tuples; compare against known-good outputs (often expectimax-derived). Run on every commit; CI gate.
- **Held-out seed pool.** 100K seeds reserved, never trained on; used for end-of-phase headline win-rate numbers.
- **Ascension ladder.** A0, A5, A10, A15, A20 per character — produces the per-phase headline metrics.
- **Oracle-agreement metric.** For every state Q2 can fully solve, compare model top-1 to oracle top-1; aggregate.
- **Counterfactual evaluator.** For map decisions, run the not-taken path under simulation and compare. Surfaces map-policy deficiencies. **Observational only per ADR-017** — counterfactual outputs do NOT feed Q10 training; they land in Q6 reports and replay-priority metadata only.
- **Exploit detector.** Flag combats >50 turns, gold/HP loops, repeated identical action sequences, infinite-block states (`scaling-strategy.md` §4.6 #5).
- **Calibration analysis.** Bucket predicted run-value vs empirical win rate; check ±10% calibration per decile.
- **Tradeoff-test reports (per ADR-014..019).** HP-spent-per-reward-value (does the agent take damage proportional to reward gained?), shadow-price calibration across HP / MaxHP / gold / per-potion-slot (do realized resource-exchange decisions at shops and swap events match the predicted `macro_context` shadow prices? — ADR-019 acceptance criterion: empirical win rate of sp-favorable choices on held-out evaluation converges to baseline + ε), elite-vs-hallway counterfactuals (does the agent pick the higher-value path under uncertainty?), observable-input audits (no `SOURCE_PERFECT` field in any deployed inference input, per ADR-016).
- **Inference latency profiling.** Distribution per decision type, per search budget.
- **A/B harness.** Run two artifacts on identical seed batches; surface per-decision diffs.

Out of scope: producing training data (Q11); promoting artifacts (out-of-band workflow per ADR-007); long-term metric storage (Q7).

## Data Ownership

None persistent. Outputs land in Q6.

- Eval run config (in-memory) — `(eval_suite_version, target_artifact_id, seed_pool, batch_size, ...)`.
- Transient batch state during evaluation — discarded after publish to Q6.

## Communication

- **Sync — read from Q5:** fetch artifact under test.
- **Sync — read from Q1:** drive the engine through pinned seeds and held-out seeds.
- **Sync — read from Q2:** oracle agreement RPC for tractable states.
- **Async — read from Q3:** sample recent rollouts for exploit detection and behavior auditing.
- **Sync — write to Q6:** publish eval reports + drilldown indices.
- **Pull — metrics:** Q7 scrapes eval throughput, in-flight batches, gate-check pass/fail counters.

## Coupling

- **Afferent (in):** Q6 (humans + CI gate consumers downstream); promotion workflow (consults Q6 via Q12-produced reports).
- **Efferent (out):** Q1 (run engine), Q2 (oracle), Q3 (sample), Q5 (artifact load), Q6 (write), Q7 (metrics).
- **Indirect:** dashboards; CI.

## Phase Expectations

- **Phase 1.** Combat-only: ≥95% A0 normal-encounter win rate; ≥90% expectimax agreement; latency <100ms at 64-sim budget. Pinned regression battery covers the existing 252-test scope plus oracle agreement on tractable states. **Note:** per Q2-ADR-002, the expectimax-agreement denominator is bounded by CULTISTS_NORMAL states (Phase-1A C++ engine scope); non-cultist encounters reject-with-diagnostic and do not factor into the agreement ratio. Q2-ADR-004 schema-promotion event (when Q10 boots) will revisit how non-verifiable states are reported.
- **Phase 2.** Adds full A0 run win rate (lead character); calibration deciles; deck-archetype entropy.
- **Phase 3.** Adds ascension ladder (A0–A15); counterfactual map evaluator; exploit detector active.
- **Phase 4.** Per-character ladder; cross-character regression checks (no >5% degradation on any character after a new one is trained).
- **Phase 5.** Held-out content evaluation; patch-adaptation timing.

## Open Risks

- **Eval cost blows up** at full ascension ladder × every character × 10K seeds. Mitigation: budget eval compute as a first-class cost; sub-sample for smoke tests on every commit, full suite on phase boundaries.
- **CI gate flakiness** if held-out seeds bleed into training. Mitigation: hash-fence the held-out set; pre-commit hook that refuses to add held-out seeds to training datasets.
- **Counterfactual evaluator amplifies variance** if used for training, not just analysis (per `scaling-strategy.md` §6.6). Mitigation: counterfactual outputs are observational only — Q12 writes them to Q6, no feedback loop to Q10.
- **Exploit detector tuned too tight** flags benign edge cases; tuned too loose misses real exploits. Mitigation: detector versioned; thresholds reviewed per phase.

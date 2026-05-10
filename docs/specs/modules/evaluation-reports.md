# Module: Evaluation Reports (Q6)

> Versioned eval reports. The truth source for "is the agent better?" Read by humans and CI gates; written only by Q12 (Evaluation Harness).

## Responsibilities

- Store evaluation outputs in a queryable, versioned form: regression battery results, ascension-ladder rows, calibration buckets, exploit-detector incidents, latency distributions.
- Enforce versioning by `(game_version, agent_version, eval_suite_version)`. Cross-version comparisons require explicit acknowledgment of all three (`scaling-strategy.md` §4.6).
- Index drilldowns: every aggregate metric is traceable to underlying replays in Q3. Click-through navigation is the user-facing contract.
- Gate CI: phase-gate metrics readable as a single `pass/fail` per artifact; CI consults Q6 to allow / block merges that touch model code.

Out of scope: producing the evaluations (that is Q12); deciding what to evaluate (research-team policy); long-term metric storage in TSDB form (Q7's job).

## Data Ownership

- **Report tables** — Parquet on object storage, partitioned by `(eval_suite_version, agent_version, run_date)`. One row per evaluated `(seed × character × ascension × encounter)` tuple, plus aggregated rollups.
- **Report-version manifest** — `(report_id, eval_suite_version, agent_version, game_version, started_at, finished_at, owner, status)`.
- **Drilldown index** — for each report row, pointers into Q3 (trajectories), Q1 replay files, and Q2 oracle-agreement rows.
- **CI gate rules** — phase-gate criteria as machine-readable policy (e.g., `phase_1.gate.combat_win_rate ≥ 0.95`).
- **Exploit-incident records** — flagged runs (combats >50 turns, gold/HP loops, repeated identical action sequences, infinite-block states) with replay pointers.

## Communication

- **Sync — write:** Q12 writes report rows and manifests via Q6's append API.
- **Sync — read:** humans via dashboards (Q7-served Grafana panels backed by Q6 Parquet); CI via a `gate_check(artifact_id, phase) → pass/fail` RPC.
- **Drilldown:** UI navigates from Q6 row → Q3 trajectory → Q1 replay reproduction.
- **Pull — metrics:** Q7 surfaces some Q6 aggregates as time-series for live dashboards (separate from raw report storage).

## Coupling

- **Afferent (in):** humans (research, eval, leadership), CI (gate enforcement).
- **Efferent (out):** Q3 (drilldown pointers), Q5 (artifact metadata), Q1 (replay reproduction), Q7 (metric surfacing).
- **Indirect:** dashboards (Grafana, Jupyter).

## Phase Expectations

- **Phase 1.** Combat-only reports: A0 encounter-pool win rate, expectimax-agreement rate, inference-latency distribution. CI gate enforces Phase 1 numerical thresholds.
- **Phase 2.** Adds card-pick top-1 agreement, calibration deciles, deck-archetype entropy, run-value-vs-empirical-win-rate calibration.
- **Phase 3+.** Adds counterfactual map-decision evaluator output, full ascension ladder rows, exploit-detector incidence, patch-adaptation timing.

## Open Risks

- **Eval-suite drift.** Adding an eval changes what "good" means. Mitigation: eval-suite version is a first-class field; cross-version comparisons require explicit version pin.
- **Drilldown index rot.** A trajectory eviction in Q3 or a replay deletion in Q1 leaves dangling drilldown pointers. Mitigation: retention policies aligned across Q3, Q1 replays, and Q6 reports; broken-link auditor runs nightly.
- **CI gate as a single boolean** can hide important context. Mitigation: gate failure surfaces the failing sub-criterion, not just `false`.
- **Exploit incidents underreported** because the detector is noisy or under-tuned. Mitigation: the detector is itself versioned; raise-the-bar reviews periodically widen criteria.

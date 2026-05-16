---
quantum: Q7
substrate: pipeline/observability/
---

> Status legend: see ADR-023. Section badges = `[SHIPPED]` / `[PHASE-N]` / `[ASPIRATION]`.

# Module: Observability (Q7)

> Time-series metrics, structured logs, replay drilldown index. The platform that lets humans see what the system is doing.

> **Substrate status:** `pipeline/observability/` is a 4-LOC service-host stub (one entry point delegating to `pipeline.common.service_host.main`). Nothing in this spec is implemented yet — every section below describes future design intent.

## Responsibilities [ASPIRATION (pre-implementation)]

- **Metrics.** Pull-based scrape of every quantum's Prometheus exposition endpoint. Dashboards in Grafana.
- **Logs.** Receive structured logs from every quantum; index for query.
- **Alerts.** Rule-driven alerting on throughput drops, win-rate regressions, value-loss spikes, replay starvation, determinism-test failures.
- **Replay drilldown.** From any metric anomaly, click through to a sample of underlying runs (replays in Q1 / trajectories in Q3 / report rows in Q6).
- **A/B harness surface.** When two models are evaluated on identical seed batches, surface per-decision diffs.

Out of scope: durable storage of training data (Q3); durable storage of eval reports (Q6); model artifacts (Q5).

## Data Ownership [ASPIRATION (pre-implementation)]

- **TSDB** — Prometheus or Mimir. Standard Prometheus metric model. Retention scaled to 90 days hot; longer in cold tier if needed.
- **Log store** — Loki or equivalent. Structured (JSON) logs; indexed by quantum, run_id, model_version.
- **Alert rules** — declarative, in Git. Rule changes are PRs.
- **Dashboards** — Grafana JSON in Git. Dashboards as code.
- **Drilldown index** — links from metric panels to Q3 / Q6 / Q1 replay-reproduction tools. Index is config, not stored data.

## Communication [ASPIRATION (pre-implementation)]

- **Pull — scrape:** every quantum exposes a Prometheus endpoint; Q7 scrapes on a schedule.
- **Push — logs:** structured logs pushed via standard agent (e.g., Promtail) to Q7.
- **Push — alerts:** alertmanager fans out to email, chat, pager.
- **Read — humans:** Grafana, Loki UI, alert console.
- **Read — automation:** anomaly hooks (e.g., autoscaler reading worker utilization).

## Coupling [ASPIRATION (pre-implementation)]

- **Afferent (in):** humans (operators, researchers, leads), alerting recipients, autoscalers.
- **Efferent (out):** every other quantum (pull metrics endpoint).
- **Indirect:** Q3, Q6, Q1 for drilldown navigation; Git (rules + dashboards as code).

## Phase Expectations

- **Phase 1.** `[PHASE-1]` Throughput dashboards (combat-steps/sec, decisions/sec); win-rate-over-time; sample-prediction loss + summary-prediction loss + HP-fraction-aux loss (per ADR-014); KL between successive policy versions. Alert on determinism-test failure and replay starvation.
- **Phase 2.** `[PHASE-2]` Adds run-level dashboards (full A0 win rate, deck composition entropy, archetype distribution). Adds **`macro_context` shadow-price calibration** dashboards (predicted HP / MaxHP / gold / per-potion-slot shadow prices vs. realized run-value lift per resource unit, per ADR-015 + ADR-019). Adds **sp derivation-method breakdown** (warmup_heuristic_curve / learned_autodiff / learned_finitediff / joint_proximal / fallback_lagged share over time, per ADR-019). Adds **sample-quality** dashboards (per-call sample count, summary uncertainty distribution).
- **Phase 3+.** `[PHASE-3+]` Adds A/B comparison view; counterfactual evaluator output (observational only per ADR-017); exploit-detector live counts. Adds **observability-regime audit** dashboard (per ADR-016: count of `SOURCE_PERFECT` field appearances in deployed inputs — target zero; any non-zero is a P0 alert).

## Open Risks

- **Metric proliferation** without curation makes dashboards useless. Mitigation: dashboard reviews per phase boundary; deprecate stale metrics.
- **Alert fatigue.** Mitigation: alert criteria reviewed per phase; pager-grade vs ticket-grade vs dashboard-only severities.
- **Drilldown link rot** as Q3 retention windows roll. Mitigation: drilldown UI surfaces a "trajectory expired" state gracefully.
- **Cardinality blowups** in metrics labeled by fine-grained run_id or seed. Mitigation: keep high-cardinality identifiers in logs, not metrics.

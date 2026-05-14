# 00 — Q10 Trainer: System Overview

Entry point for Q10's internal specs. Subsequent files (`01-decisions-log.md`,
`modules/*.md`) refine this; anything that contradicts this document must be
reconciled here first.

Cross-quantum context lives at `docs/specs/00-system-overview.md` and
`docs/specs/modules/trainer.md`. Cross-quantum ADRs that constrain Q10:
ADR-001 (service-based pipeline), ADR-006 (Q3 as async backbone), ADR-007
(registry separated from serving authority — Q10 does not promote), ADR-009
+ ADR-014..018 (AlphaZero at combat, samples+summary output, macro_context
inputs, observability regime, macro-owned reward), ADR-020 (oracle-agreement
sideband routes through Q3 — Q10 reads via prioritized sampling), ADR-021
(Phase-1 degenerate-single sample convention).

## 1. Purpose & Boundaries

Q10 is the PyTorch training loop. Pulls minibatches from Q3 via `POST /sample`,
computes AlphaZero-style losses on a small (~10 M-param) transformer policy,
publishes `(state-dict, ONNX export, Q4 bundle, provenance manifest)` to Q5
on cadence. Stateless across runs — all persistent state lives in Q5
(artifacts) and Q3 (trajectories).

In scope: sampling client + framing, RichState→tensor encoding, network
definition, multi-head loss, optimizer + scheduler, atomic Q5 publish, ONNX
export, metrics + W&B sidecar, training-loop orchestration.

Out of scope: trajectory schema (cross-quantum, owned at
`contracts/schemas/trajectory/`), priority-score computation (Q2 + Q3),
artifact promotion (external workflow per ADR-007 — Q12 + reviewer), inference
serving (Q9), curriculum (Q11), RichState design (Q1).

## 2. Context Diagram (textual)

External producers / consumers:

```
                                   ┌────────── Q5 model-registry
   Q3 experience-store ──[POST     │              ▲    [POST publish]
   /sample, length-                │              │
   delimited proto] ───────────►   │              │
                                   ▼              │
                          ┌──────────────────────────────────────┐
                          │  Q10 Trainer (one process, port 18110)│
                          │                                       │
                          │   ┌──────────────┐                    │
                          │   │ run_config   │ (immutable id)     │
                          │   └──────┬───────┘                    │
                          │          │ bootstrap                  │
                          │          ▼                            │
                          │   ┌──────────────┐  parent artifact   │
                          │   │ artifact_    │◄───── Q5 ──────┐   │
                          │   │ publisher    │                │   │
                          │   └──────┬───────┘                │   │
                          │          │ ContentRegistry        │   │
                          │          ▼                        │   │
                          │   ┌──────────────┐                │   │
                          │   │ tensor_encoder│               │   │
                          │   └──────┬───────┘                │   │
                          │          ▲                        │   │
                          │          │ EncodedBatch           │   │
                          │   ┌──────┴───────┐                │   │
                          │   │ data_ingest  │ ──[queue]──►   │   │
                          │   │ (prefetcher) │                │   │
                          │   └──────┬───────┘                │   │
                          │          │ (Q3 RPC)               │   │
                          │          ▼                        │   │
                          │   ┌──────────────┐                │   │
                          │   │ train_driver │────────────────┘   │
                          │   │ (main loop)  │                    │
                          │   └──┬───┬───┬───┘                    │
                          │      │   │   │                        │
                          │      ▼   ▼   ▼                        │
                          │  model loss optim                     │
                          │      │   │   │                        │
                          │      └─┬─┴───┘                        │
                          │        ▼                              │
                          │  ┌─────────────────┐                  │
                          │  │ metrics_emitter │ ──► Prom / W&B   │
                          │  └─────────────────┘                  │
                          │                                       │
                          │  GET /health, /metrics                │
                          └──────────────────────────────────────┘
                                          ▲
   Q7 observability ────[GET /metrics]────┘
   W&B SaaS (egress) ◄── [sidecar inline thread]
```

Sync edges (latency-relevant):
- Q10 → Q3 Sampler: `POST /sample` length-delimited protobuf stream. Trainer
  is GPU-coupled; tail-RPC latency hidden by prefetch queue.
- Q10 → Q5 ArtifactPublisher: `POST publish` blob + manifest, atomic
  temp+rename on Q5 side.
- Q7 → Q10 `/metrics`: Prometheus text v0.0.4 scrape.

Async edges:
- W&B sidecar: internal queue → background uploader; main metric path never
  blocks on egress.

Internal edges are in-process function calls today. Three daemon threads
share a `threading.Event` stop signal (Q3 pattern):
1. `data_ingest` prefetcher (one outstanding Q3 RPC; queue capacity 4–8).
2. `train_driver` main loop (forward→loss→backward→step→checkpoint cadence).
3. `artifact_publisher` cadence worker (single-slot publish queue).

## 3. Architecture Characteristics (ranked)

Specialized from the pipeline-level ranking at
`docs/specs/00-system-overview.md` §3.

1. **GPU Utilization / Sample Throughput.** Phase-1 single-GPU target
   ≥85% utilization. Q3 RPC tail latency (~50 ms) and Q5 publish wall-time
   (~seconds) must not stall the GPU step (~150 ms target). Drives the
   producer-consumer data path: bounded prefetch queue (Q10-ADR-001),
   serial single-RPC prefetcher (Q10-ADR-002), non-blocking publisher
   thread.
2. **Reproducibility of Artifacts.** Q10 is the enforcement point for the
   pipeline-level reproducibility characteristic. Every published artifact
   reproducible from `(code_sha, dataset_sha, seed, hyperparameters,
   parent_artifact_id, content_registry_sha)`. Drives frozen-at-bootstrap
   Q4 (Q10-ADR-008), trajectory-id-list `dataset_sha` (Q10-ADR-003),
   immutable `RunProvenance` captured once, atomic temp+rename publish.
3. **Phase-Evolution Headroom.** Phase-2 multi-head losses, Phase-3
   stratified per-decision-type sampling, Phase-4 multi-GPU DDP,
   Phase-4+ PCGrad must land without service rewrite. Drives
   head-registration in `model` + `loss_engine`, sampling-mode as a
   request-payload field in `data_ingest`, ONNX-every-checkpoint
   (Q10-ADR-006), single-line DDP wrap in `train_driver`.

Below the line — constraints, not characteristics:
- Latency: not load-bearing. Throughput-bound; Q9 owns inference latency.
- Availability: restart-the-run failure mode. Fail-fast on Q3 outage in
  Phase 1 (Q10-ADR-004).
- Security: internal-only system, no PII, no auth.
- Multi-tenancy: one run per process.

## 4. Submodule Map

Q10 ships as **one deployable service** (modular monolith — see Q10-ADR-001).
Internal decomposition into 8 functional-cohesion submodules + 1 cross-cutting
emitter. None require their own on-disk schema; the only schema Q10 owns is
the **provenance manifest** written into the Q5 artifact bundle.

| # | Submodule | Phase 1 | Schema-owning | Persistent state |
|---|---|---|---|---|
| 1 | [run-config](modules/run-config.md) | yes | no | none (in-process, immutable) |
| 2 | [data-ingest](modules/data-ingest.md) | yes (uniform + prioritized) | no | none (HTTP session + bounded queue) |
| 3 | [tensor-encoder](modules/tensor-encoder.md) | yes (combat) | no | none (ContentRegistry frozen at bootstrap) |
| 4 | [model](modules/model.md) | yes (encoder + 4 heads) | no | `nn.Module` weights (ephemeral; persisted via artifact-publisher) |
| 5 | [loss-engine](modules/loss-engine.md) | yes (5 head losses) | no | head registry (in-memory) |
| 6 | [optim](modules/optim.md) | yes (AdamW + cosine) | no | optimizer + scheduler state (ephemeral) |
| 7 | [artifact-publisher](modules/artifact-publisher.md) | yes | **yes** (provenance manifest v1) | none on disk (artifact bytes flow through; Q5 owns storage) |
| 8 | [train-driver](modules/train-driver.md) | yes | no | step counter + cadence timers |
| + | [metrics-emitter](modules/metrics-emitter.md) | yes | no | counter/gauge map; W&B handle |

Trained policies, ONNX exports, optimizer snapshots, content registries are
**not** Q10 artifacts on disk. Q10 emits them through `artifact-publisher`
to Q5. Q10's local `data_dir` is for transient scratch only (e.g.,
ONNX-export staging before temp+rename).

## 5. Where to Look Next

- `01-decisions-log.md` — Q10-internal ADRs (Q10-ADR-001..008). Read before
  challenging any submodule boundary; each ADR's Consequences block leads
  with the negative trade-offs being accepted.
- `modules/<name>.md` — one per submodule: responsibilities, data ownership,
  communication, coupling, testing strategy.
- Cross-quantum ADRs that Q10 inherits: `docs/specs/01-decisions-log.md`
  ADR-001, ADR-006, ADR-007, ADR-009..010, ADR-014..018, ADR-020..021.
- Cross-quantum ADR mirrored from Q10: Q10-ADR-005 (proto-binding lift)
  will require a cross-quantum mirror at `docs/specs/01-decisions-log.md`
  when Q3 boot directive next cycles.

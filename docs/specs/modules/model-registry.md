# Module: Model Registry (Q5)

> Versioned blob store + metadata table for trained model artifacts. Distinct from serving authority (ADR-007).

## Responsibilities

- **Store artifacts.** Each artifact = `(weights, ONNX export, packaged Q4 registry, provenance manifest)`. Artifacts are immutable; promotion / demotion happen elsewhere (ADR-007).
- **Metadata.** Maintain searchable rows: `(artifact_id, code SHA, dataset SHA, seed, hyperparameters JSON, parent_artifact_id?, eval_suite_version, created_at, owner)` per `scaling-strategy.md` §5.5.
- **ONNX export pipeline.** A trainer-produced PyTorch checkpoint becomes an ONNX export bound for Q9. Export step is part of the artifact build, not a runtime concern.
- **Lifecycle.** Newly-published artifacts are atomic via temp-write + rename. Eval-evicted artifacts may be marked for archival, never silently deleted while referenced.

Out of scope: deciding which artifact is "current production" (Q9 + a separate config workflow); training; inference.

## Data Ownership

- **Artifact blob store** — large-object storage (S3-equivalent for primary, local mirror for recent artifacts). Each blob immutable; addressed by content-hash SHA.
- **Metadata table** — relational (Postgres or SQLite at small scale). Schema versioned; columns above. Foreign key from `parent_artifact_id` to `artifact_id` for lineage.
- **Promotion log** — append-only record of every "this artifact became serving" event with reviewer, timestamp, prior artifact, and rationale (per ADR-007 the promotion *workflow* is external; the *log* lives here for auditability).

No other quantum writes the metadata table. The trainer (Q10) writes blobs through Q5's publish API; promotion is via a separate workflow that records into the promotion log.

## Communication

- **Sync — publish:** Q10 calls `publish(blob, metadata) → artifact_id`. Atomic; deduplicated on content hash.
- **Sync — fetch:** Q9 calls `fetch(artifact_id) → blob_path` at startup and on configured promotion. Q12 calls the same to evaluate.
- **Sync — query:** ad-hoc CLI / dashboard queries over metadata for lineage and provenance.
- **Out-of-band — promotion workflow:** an external script gates production updates; reviewer sign-off appends to promotion log.
- **Pull — metrics:** Q7 reads publish rate, fetch rate, blob storage size.

## Coupling

- **Afferent (in):** Q9 (fetches at startup / promotion), Q12 (fetches under-test artifact), humans (auditing lineage).
- **Efferent (out):** Q7 (metrics).
- **Indirect:** Q4 (bundled inside artifacts but sourced from its own release workflow); object storage backend.

## Phase Expectations

- **Phase 1.** Single-host metadata store; local filesystem blob store mirrored to S3-equivalent. Manual promotion (eyeball + edit serving config).
- **Phase 2.** Promotion workflow scripted with reviewer sign-off. Lineage queries supported.
- **Phase 3+.** Multi-region blob mirror if rollouts go cross-region. Retention policy on archival.

## Open Risks

- **Two control planes (registry vs serving config) can drift.** Mitigation: deletion guard — Q5 refuses to delete an artifact still referenced by any serving config. Periodic reconciliation job alerts on drift.
- **Artifact bundle size growth.** Q4 inside the artifact plus weights plus ONNX can become several GB per artifact. Mitigation: dedupe on content hash; archive cold artifacts.
- **Provenance integrity.** A trainer that publishes an artifact with a missing dataset SHA breaks reproducibility. Mitigation: publish API rejects incomplete metadata; CI check on metadata schema.
- **ONNX export divergence.** ONNX export and PyTorch checkpoint may diverge if export options differ across runs. Mitigation: export config pinned in the artifact metadata; CI verifies parity on a small sample.

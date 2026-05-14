# Submodule: artifact-publisher

> Q5 client + ONNX export + provenance manifest. The **only schema-owning
> submodule** in Q10 — owns the provenance manifest format (v1 Phase 1).
> Runs publish work on a dedicated daemon thread off the GPU loop.

## Responsibilities

- **Bootstrap path:** `load_parent()` — issue `GET <q5_url>/artifact/<parent_id>`
  (or `None` for from-scratch runs); deserialize the bundle into
  `(state_dict_bytes, content_registry_bytes, parent_manifest)`; return
  these to `service.py` for downstream submodule construction
  (`model.load_state_dict`, `tensor_encoder.ContentRegistry`,
  `RunProvenance.parent_artifact_id`).
- **Cadence path:** spawn a daemon thread at `start(stop_event)`. The
  thread blocks on a single-slot `queue.Queue` of `PublishRequest`
  records; `train_driver` posts a request when the checkpoint cadence
  fires (every N steps + every M minutes, whichever first).
- **Publish work** (on the dedicated thread):
  1. Snapshot `model.state_dict()` and `optim.state_dict()`. The
     snapshot must be a deep copy so further training steps don't
     mutate it.
  2. Export to ONNX per Q10-ADR-006 via `torch.onnx.export(...)`,
     validate with `onnx.checker.check_model(...)`.
  3. Compute `dataset_sha` per Q10-ADR-003: hash the trajectory-id list
     snapshotted via `data_ingest.snapshot_consumed_ids()`.
  4. Build the **provenance manifest v1**:
     ```json
     {
       "schema_version": 1,
       "artifact_id": "<uuid4>",
       "code_sha": "<git sha>",
       "code_dirty": false,
       "dataset_sha": "<sha256>",
       "dataset_size": 1234567,
       "seed": 42,
       "hyperparameters": { ... },
       "parent_artifact_id": "<uuid or null>",
       "content_registry_sha": "<sha256>",
       "onnx_opset_version": 17,
       "phase": 1,
       "step": 12345,
       "created_at_ns": 1715692800000000000,
       "host": "trainer-host-01"
     }
     ```
  5. Assemble the bundle as a tar (or zip — Phase-1 detail, fixed at
     boot): `weights.pt`, `model.onnx`, `content_registry.tar` (passed
     through unchanged from parent per Q10-ADR-008), `manifest.json`.
  6. `POST <q5_url>/publish` with the bundle bytes. Q5 stores atomically
     via its own temp+rename (per cross-quantum spec).
  7. Update `LastPublishedRef` and emit metrics.
- Honor SIGTERM: a publish in flight completes if possible (with a
  bounded join timeout, e.g., 30 s); otherwise drops and lets the
  next run resume from the previous parent.

Out of scope: artifact storage (Q5 owns); promotion (external workflow
per ADR-007); ONNX schema design (op-set chosen at config; not invented
here).

## Data Ownership

In-process, mutable:

- `LastPublishedRef(artifact_id, step, wall_clock_ns)` — read by
  `train_driver` to enforce "do not publish twice at the same step."
- Publish queue — `queue.Queue(maxsize=1)`; if a publish is in flight
  and another cadence fires, the request is dropped with a metric
  counter incremented (preferred over backlog).
- ONNX exporter handle — a small wrapper around `torch.onnx.export`
  with the fixed op-set version.

Schema-owning? **YES.** The provenance manifest schema is the one
schema Q10 owns. Phase-1 v1. Future bumps via a new ADR + this module.

## Communication

**External (HTTP, sync, to Q5):**

- `GET <q5_url>/artifact/<artifact_id>` — fetches the parent bundle at
  bootstrap.
- `POST <q5_url>/publish` — body = bundle bytes; response =
  `{"artifact_id": "<uuid>"}`. Atomic on Q5 side.

**Internal (in-process function calls, sync):**

- `ArtifactPublisher(config, run_provenance) -> ArtifactPublisher` —
  constructed at bootstrap.
- `load_parent() -> (state_dict_bytes, content_registry_bytes,
  parent_manifest)` — once at bootstrap.
- `start(stop_event)` — spawn the publisher daemon.
- `request_publish(snapshot: ModelSnapshot) -> None` — called by
  `train_driver` on cadence; non-blocking (drops if queue full).
- `last_published() -> Optional[ArtifactRef]` — read by `train_driver`
  and `/metrics`.

**Metrics:**

- `sts2_q10_publish_total{result="ok"|"err"|"dropped"}` — counter.
- `sts2_q10_publish_latency_seconds` — histogram (Phase-2; gauge
  Phase 1).
- `sts2_q10_artifact_size_bytes` — gauge; size of the last published
  bundle.
- `sts2_q10_onnx_export_seconds` — gauge.
- `sts2_q10_last_published_step` — gauge; for staleness alerting.

## Coupling

- **Afferent (in):** `service.py` (load_parent at bootstrap),
  `train_driver` (request_publish on cadence), `/metrics` (last_published).
- **Efferent (out):** Q5 over HTTP; `model.state_dict`, `optim.state_dict`,
  `data_ingest.snapshot_consumed_ids`, `tensor_encoder.content_registry`
  (for the bundled bytes), PyTorch `onnx.export`, `onnx.checker`.
- **Indirect:** Q7 (metrics).

## Phase-1 atomicity contract

Q5's atomicity (`temp+rename` on the Q5 side) is the durability guarantee.
Q10's contract is:

- The manifest is finalized **before** the POST body is sent (no
  manifest-after-bytes inconsistency).
- The ONNX export passes `check_model` **before** inclusion; a failed
  check aborts the publish (metrics counter `sts2_q10_publish_total{result="err"}`).
- On a SIGTERM mid-publish, the publish completes if the HTTP body is
  already streaming to Q5; otherwise the request is dropped and the
  next run resumes from the previous parent.

## Testing Strategy

### Unit (mock Q5)

1. **Provenance manifest matches v1 schema.** Construct a manifest from
   a known `RunProvenance` + step → JSON validates against the v1
   schema (a JSON-schema fixture lives at
   `pipeline/trainer/tests/fixtures/manifest_v1.schema.json`). Absent
   test: silent schema drift.
2. **ONNX export passes `onnx.checker.check_model`.** Phase-1 net →
   exported file → `check_model` succeeds. Absent test: Q9 can't load.
3. **`dataset_sha` is deterministic given the same trajectory-id list.**
   Two calls with the same list → same SHA. Verifies Q10-ADR-003.
4. **`dataset_sha` differs when one ID is added.** List+1 ID → different
   SHA. Defense against the chosen hash collapsing.
5. **Atomic temp+rename observed under simulated SIGTERM.** Mock SIGTERM
   mid-write of `weights.pt` to the staging dir → no half-written file
   remains in Q5's view (Q5's own atomicity verifies; trainer's job is
   to send a complete body). Absent test: corrupt-checkpoint risk.
6. **Drop-on-full queue.** Queue full + new `request_publish` →
   counter `publish_total{result="dropped"}` increments; no exception.
   Verifies the back-pressure policy.

### Integration

1. **Against mock Q5 server.** Publish round-trip preserves all metadata
   fields including `parent_artifact_id` chain. Verifies the contract.
2. **Resume from last published artifact.** Publish at step 100; restart
   trainer with `parent_artifact_id=<the new id>`; model weights at
   bootstrap match step-100 weights bit-equal; optimizer state matches.
   Verifies the full reproducibility loop.
3. **`load_parent` with `parent_artifact_id=None`.** Returns sentinel
   "from-scratch" tuple; `tensor_encoder` constructs a fresh
   `ContentRegistry` from Phase-1 minimal config (per cross-quantum
   `content-registry.md` Phase 1). Verifies the cold-start path.

### Smoke (mandatory)

- N/A for direct `/health` schema. `artifact_publisher` is exercised
  only when a checkpoint cadence fires.

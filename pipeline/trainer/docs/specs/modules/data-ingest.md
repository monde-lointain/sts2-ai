# Submodule: data-ingest

> Q3 sampling client + length-delimited protobuf framing + bounded prefetch
> queue. Single serial RPC at a time per Q10-ADR-002. Owns the HTTP session
> and the prefetch queue; no persistent state.

## Responsibilities

- Issue `POST /sample` against Q3 with `mode = "uniform" | "prioritized"`
  (Phase 1) per `RunConfig.sampling.mode`. Stratified mode reserved for
  Phase 2+ (per-decision-type replay).
- Decode the Q3 response: length-delimited protobuf stream of
  `TrajectoryStep` messages terminated by a `{Status: ok|exhausted}` trailer.
  Use the framing varint decoder mirroring
  `pipeline/experience-store/sampler/framing.py`.
- Filter Phase-1 degenerate combat samples per Q3-ADR-005 (oracle-agreement
  prioritization upstream of Q10 is already filtered at Q3's sampler;
  trainer-side check is a defense-in-depth assertion only, off the hot
  path).
- Assemble decoded steps into `Batch` records `(steps: list[TrajectoryStep],
  cursor_token: str)` and push to a bounded `queue.Queue` (capacity 4
  Phase 1; configurable).
- Honor Q3-ADR-006 schema-drain `503 {reason, retry_after_sec}`: sleep
  the advertised duration and retry. Hard errors (connection refused,
  unretryable 5xx) propagate as `RuntimeError` per Q10-ADR-004 fail-fast.
- Accumulate the trajectory-ID list (Q10-ADR-003) as batches stream
  through, used by `artifact_publisher` to compute `dataset_sha`.
- Spawn one daemon prefetcher thread at `start(stop_event)`; join on
  `stop_event`-set within the timeout (default 5 s).
- Expose `get_batch(timeout: float) -> Optional[Batch]` to `train_driver`;
  returns `None` only on stop.

Out of scope: tensor encoding (`tensor_encoder`); priority-score
computation (Q2 + Q3); cross-tier assembly (Q3 hides hot/cold mix from
the trainer); per-trajectory provenance enrichment beyond what Q3 returns
on each step.

## Data Ownership

None persistent. Transient in-process:

- **Prefetch queue.** `queue.Queue(maxsize=4)` of `Batch` records. Single
  producer (prefetcher thread), single consumer (`train_driver`).
- **HTTP session.** A `urllib`-based session held by `data_ingest.client`
  with the `requests`-shaped retry recipe inlined per
  `pipeline/experience-store/docs/specs/modules/sampler.md:107-122`.
- **Trajectory-ID accumulator.** A growing list of consumed trajectory IDs
  for `dataset_sha` (Q10-ADR-003). Accessed by `artifact_publisher` at
  publish time via `data_ingest.snapshot_consumed_ids()`.
- **Cursor token.** Returned by Q3 on each response; passed back on the
  next request for resumable sampling.

Schema-owning? **No.** Reads via Q3's `trajectory_pb2` bindings (imported
from `pipeline.common.trajectory_proto` per Q10-ADR-005).

## Communication

**External (HTTP, sync, to Q3):**

- `POST <q3_url>/sample`
  - Body (per Q3 Sampler spec):
    ```json
    {
      "mode": "uniform" | "prioritized",
      "batch_size": int,
      "filters": {
        "decision_type": ["COMBAT"]?,
        "schema_version": {"major": 1, "minor": 1}?
      },
      "cursor_id": str?
    }
    ```
  - Response: length-delimited protobuf stream of `TrajectoryStep` +
    trailer frame.
  - `503 schema_drain` → sleep `retry_after_sec`, retry once; on second
    503 abort the prefetch attempt and let `train_driver` decide.
  - Other 4xx/5xx → `RuntimeError` (fail-fast per Q10-ADR-004).

**Internal (in-process function calls, sync):**

- `DataIngest(config, run_provenance) -> DataIngest` — construct;
  `start(stop_event: threading.Event)` spawns the prefetcher daemon.
- `get_batch(timeout: float) -> Optional[Batch]` — called by
  `train_driver`. Blocks up to timeout; returns `None` on stop.
- `snapshot_consumed_ids() -> list[str]` — called by `artifact_publisher`
  at publish time. Returns the current accumulator; the caller hashes it.

**Metrics:**

- `sts2_q10_sample_request_total{result="ok"|"503"|"err"}` — counter.
- `sts2_q10_sample_rows_total` — counter; total TrajectoryStep rows
  consumed.
- `sts2_q10_prefetch_queue_depth` — gauge; visible to operators for
  starvation alerting.
- `sts2_q10_sample_latency_seconds` — histogram (Phase-2; gauge in
  Phase 1).
- `sts2_q10_consumed_traj_ids_total` — counter; informs `dataset_sha`
  list growth.

## Coupling

- **Afferent (in):** `train_driver` (consumer of `get_batch`);
  `artifact_publisher` (consumer of `snapshot_consumed_ids`).
- **Efferent (out):** Q3 over HTTP; `pipeline.common.trajectory_proto`
  for parsing.
- **Indirect:** Q7 (scrapes metrics).

## Testing Strategy

### Unit (mock Q3 over `unittest.mock`)

1. **Varint framing decode roundtrip.** Encode N protobuf messages with
   varint-prefixed lengths; decoder returns the same N messages. Absent
   test: silent frame mis-alignment loses data.
2. **Trailer frame terminates batch.** Stream ends with
   `{Status: exhausted}` trailer; `Batch` is emitted with the partial
   step list and a marker. Absent test: hang on end-of-data.
3. **503 schema-drain retries once with advertised delay.** Mock Q3
   returns 503 once then 200; prefetcher succeeds after the second call.
   Verifies Q3-ADR-006 trainer-side retry recipe.
4. **Two consecutive 503s abort the attempt.** Second 503 raises
   `Q3UnavailableError`; the prefetcher loop catches and propagates per
   Q10-ADR-004. Verifies the fail-fast posture.
5. **Prefetch queue back-pressures the prefetcher.** Queue full;
   `prefetcher.run` blocks on `queue.put` and resumes when a consumer
   reads. Absent test: silent memory growth.
6. **Trajectory-ID accumulator grows monotonically and deterministically.**
   100 batches → accumulator has 100×batch_size IDs in arrival order;
   snapshot is a copy (no aliasing). Verifies Q10-ADR-003 inputs.

### Integration

1. **Against running Q3 (local).** Spin up a real Q3 service via
   `pipeline/tests/smoke_services.py` fixture; ingest a known dataset;
   sample 1000 steps in `mode=uniform`; `data_ingest` returns 1000 steps
   distributed across `Batch` records of configured size. Verifies the
   protobuf wire-format contract end-to-end.
2. **Stop-event shutdown drains cleanly.** Mid-fetch SIGTERM →
   prefetcher exits within 5 s; `get_batch(timeout=10)` returns `None`
   and `train_driver` exits cleanly. Verifies the Q3-pattern shutdown
   contract.

### Smoke (mandatory)

- N/A for direct `/health` schema; `data_ingest` is a consumer of Q3.
  Smoke confirms the trainer service starts even if Q3 is unreachable
  (the prefetcher fails fast on first request but the HTTP server lives;
  this is intentional so `/health` always responds).

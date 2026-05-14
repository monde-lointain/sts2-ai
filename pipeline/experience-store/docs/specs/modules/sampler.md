# Submodule: Sampler

> Read-path RPC for Q10 trainer and Q12 evaluation-harness. Serves
> uniform / stratified / prioritized minibatches. Assembles across
> hot+cold tiers transparently. Owns no persistent state.

## Responsibilities

- Accept sampling requests via `POST /sample`. Decode mode, batch_size,
  and filters. Validate `schema_version` filter against SchemaRegistry.
- **Uniform mode (Phase 1):** reservoir-sample from HotStore range
  scan; Phase-2+ cross-tier bias per Q3-ADR-009 (`α=2.0` hot bias).
- **Prioritized mode (Phase 2+):** read top-K from PriorityIndex; fetch
  corresponding rows from HotStore (or ColdStore on miss).
- **Stratified mode (Phase 3+):** read from per-bucket `step_idx` CF;
  balance across buckets per request config.
- Honor `cold_only=true` filter for backfill jobs (Q3-ADR-009).
- Stream response as length-delimited protobuf bytes; large batches
  arrive incrementally.
- Maintain a transient LRU cursor cache for resumable cursors
  (`GET /sample/cursor/<id>`). Bounded (default 1024 cursors); expires
  after idle timeout (default 300 s).
- Return structured `503 {"reason":"schema_drain","retry_after_sec":N}`
  during schema-locked windows (Q3-ADR-006). Trainer is expected to
  retry.

Out of scope: priority score computation (PriorityIndex stores; Q10/Q2
produce); cold-tier latency tuning (ColdStore); cross-tier weighting
parameters (Q3-ADR-009 documents).

## Data Ownership

None persistent. Transient in-process:

- **Cursor LRU.** `{cursor_id → CursorState(mode, filters, position_ts_ns,
  served_count, last_touch_ts_ns)}`. Phase-1 cap = 1024; Phase-2+
  configurable.
- **Per-request transient state.** request body, decoded filters, in-flight
  iterator; freed at response completion.

Schema-owning? **No.** Reads via SchemaRegistry.

## Communication

**External (HTTP, sync):**

- `POST /sample`
  - Body:
    ```json
    {
      "mode": "uniform" | "stratified" | "prioritized",
      "batch_size": int,
      "filters": {
        "decision_type": ["COMBAT", ...]?,
        "model_version": [str]?,
        "generator": [str]?,
        "sampling_mode": [str]?,
        "schema_version": {"major": int, "minor": int}?,
        "cold_only": bool? /* default false */,
        "after_ts_ns": int?
      },
      "cursor_id": str?   /* resume an existing cursor */
    }
    ```
  - Response: `200` length-delimited protobuf stream of `TrajectoryStep`
    messages, terminated by `{Status: ok|exhausted}` trailer frame.
  - `400` on malformed filter; `503 {reason, retry_after_sec}` on schema
    lock; `404` on missing cursor; `429` on Phase-2+ rate-limit (not
    Phase 1).
- `GET /sample/cursor/<id>`
  - Response: cursor state for debugging.
- `GET /sample/recent` *(Q12 evaluation-harness)*
  - Convenience wrapper over `POST /sample` with `mode=uniform,
    after_ts_ns=<now - 5 min>`.

**Internal (in-process function calls, sync):**

- `SchemaRegistry.validate(version, op="read")` — per request.
- `HotStore.scan(after_ts_ns, limit)` — Phase 1 uniform.
- `HotStore.read(trajectory_id)` — Phase 2+ prioritized + cross-tier
  assembly.
- `PriorityIndex.top_k(k, filters)` — Phase 2+ prioritized mode.
- `ColdStore.scan(predicate, limit)` — Phase 2+ cold reads.
- `ControlPlane.provenance.lookup(trajectory_id) -> Provenance` — Phase
  2+ for off-policy correction provenance enrichment.

**Metrics:**

- `sts2_q3_sample_request_total{mode, result="ok"|"exhausted"|"err"}` —
  counter.
- `sts2_q3_sample_rows_returned_total{mode, tier="hot"|"cold"}` —
  counter.
- `sts2_q3_sample_latency_seconds{mode}` — histogram.
- `sts2_q3_sample_cursor_count{}` — gauge.
- `sts2_q3_sample_schema_503_total{}` — counter.

## Coupling

- **Afferent (in):** Q10 trainer, Q12 evaluation-harness.
- **Efferent (out, internal):** SchemaRegistry, HotStore, ColdStore (P2+),
  PriorityIndex (P2+), ControlPlane.ProvenanceIndex (P2+),
  ControlPlane.ObservabilityAdapter (metrics).
- **Indirect:** Q7 (scrapes metrics).

## Trainer-side retry recipe (per Q3-ADR-006)

Documented here for the trainer team:

```python
import time, requests
def sample_with_retry(url, payload, max_attempts=5):
    for attempt in range(max_attempts):
        r = requests.post(url, json=payload, stream=True)
        if r.status_code == 200:
            return r
        if r.status_code == 503:
            retry_after = r.json().get("retry_after_sec", 5)
            time.sleep(retry_after)
            continue
        r.raise_for_status()
    raise RuntimeError("Q3 sample retries exhausted")
```

## Phase-1 degenerate-sample filter (per Q3-ADR-005)

Returned `TrajectoryStep` rows with `decision_type == COMBAT` carry a
degenerate `combat_outcome_samples = [Sample(probability_weight=1.0,
hp_delta=summary.expected_hp_delta, ...)]`. Distributional analyses MUST
filter these out:

```python
def is_degenerate_combat_sample(step):
    return (step.decision_type == COMBAT
            and len(step.combat_outcome_samples) == 1
            and step.combat_outcome_samples[0].probability_weight == 1.0
            and step.combat_outcome_samples[0].hp_delta ==
                step.combat_outcome_summary.expected_hp_delta)
```

## Testing Strategy

### Unit (mock HotStore / ColdStore / PriorityIndex)

1. **Uniform sample of size K returns ≤ K.** HotStore has 10k rows;
   request `batch_size=512` → returns 512. HotStore has 100 rows;
   request `batch_size=512` → returns 100 with trailer `{Status:
   exhausted}`. Absent test: silent under-fills or out-of-bound reads.
2. **Filter by `model_version` excludes others.** HotStore has rows from
   v1, v2, v3; filter `model_version=["v2"]` → all returned rows have
   `model_version=="v2"`. Absent test: off-policy correction breaks.
3. **Schema filter excludes drained-out versions.** SchemaRegistry
   reports `(1,1)` current; filter `schema_version=(1,0)` → result
   empty if no v1.0 rows remain after drain. Absent test: trainer
   silently reads mixed-schema data.
4. **Cursor resume returns next page.** First call returns rows
   `[0..511]` and cursor_id `X`; resume with `cursor_id=X` returns
   `[512..1023]`. Absent test: pagination repeats or skips rows.
5. **`503 schema_drain` during lock.** SchemaRegistry in `locked` →
   `POST /sample` returns `503 {"reason":"schema_drain",
   "retry_after_sec":5}`. Absent test: trainers crash during planned
   migration.
6. **`mode="prioritized"` (Phase 2+) calls PriorityIndex.top_k.**
   `batch_size=64` → exactly one `top_k(64, filters)` call; returned
   rows ordered by descending score. Absent test: prioritization
   silently degrades to uniform.

### Integration

1. **Round-trip with IngestAPI.** Ingest 10k trajectories; sample
   `mode=uniform, batch_size=1000` repeatedly; verify each returned
   row carries `model_version`, `sampling_mode`, `generator` matching
   what was ingested (provenance pass-through). Verifies the provenance-
   load-bearing contract from ADR-006.
2. **Cross-tier assembly (Phase 2+).** Ingest 10k rows; Lifecycle
   promotes oldest 5k to ColdStore; uniform sample returns rows from
   both tiers in the ratio dictated by Q3-ADR-009; total returned
   = batch_size. Verifies the cross-tier read path.
3. **Concurrent sample + ingest.** Run a writer streaming 1k traj/sec
   and a sampler pulling 1k traj/req at 1Hz for 60 s; no errors, no
   blocking beyond `ThreadingHTTPServer`'s per-request thread.
   Verifies read/write coexistence under stress.

### Smoke (mandatory)

- N/A for direct gating (Sampler is read-only and does not own
  `/health` or `/metrics`). But its metrics counters must be
  registered with ObservabilityAdapter at startup so `/metrics` shows
  `sts2_q3_sample_request_total 0` before the first request — verified
  by Phase-2 smoke richer test.

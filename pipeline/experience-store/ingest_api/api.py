"""IngestAPI: write-path HTTP front door (S0.C.alpha, Phase-1A).

Implements `pipeline/experience-store/docs/specs/modules/ingest-api.md`.

Phase-1A architecture note
--------------------------
The spec's "Data Ownership" section (modules/ingest-api.md lines 30-35,
modules/hot-store.md line 13) describes a model where IngestAPI enqueues
trajectories on a bounded queue read by HotStore's single consumer
thread. The current W2 HotStore does not own a consumer — `HotStore.append`
is a synchronous write. Phase-1A therefore inverts that pipeline:

1. The HTTP handler synchronously calls `SchemaRegistry.validate`,
   then `HotStore.append` (assigns `ingest_ts_ns`), then
   `ProvenanceLog.append` (mandatory, non-droppable per Q3-ADR-001).
2. The bounded `queue.Queue` and consumer thread still exist so that:
   - `queue_depth()` is a real surface Lifecycle's sustained-pressure
     predicate (Q3-ADR-008) can read.
   - HTTP 503 back-pressure (Q3-ADR-006) surfaces via `queue.full()`.
   But the queue's contents are diagnostic — a `(trajectory_id,
   ingest_ts_ns)` tuple — and the consumer just drains/discards them.

Phase-2+ refactor target: HotStore owns the consumer, IngestAPI puts
serialized trajectory bytes on the queue and returns `ingest_ts_ns=-1`
until the consumer assigns one. Tracked at Phase-1 close per the
S0.C.alpha dispatch prompt's "re-surface trigger #2" carve-out.

API surface
-----------
IngestAPI is constructed once at service boot. The three HTTP handlers
are pure functions of (body, content_type) → (status, headers, body):
W4's `service.py` HTTP dispatch calls them directly. `consumer_loop`
runs in a daemon thread started by `service.py`; `metrics_lines`
returns Prometheus text v0.0.4 lines for W4 aggregation.

Trajectory invariants (rejected with 400)
-----------------------------------------
Per the dispatch prompt's "Constraints" block, on every accepted POST
the parsed `Trajectory` is checked against:
- `schema_version.major/minor` non-default (caught by SchemaRegistry.validate).
- Every step has `decision_type != UNSPECIFIED`.
- Every step has `observability_regime != UNSPECIFIED`.
- Combat steps have `len(combat_outcome_samples) >= 1` (Q3-ADR-005).
- Provenance triple (`model_version`, `sampling_mode`, `generator`) all
  non-empty (single-writer invariant per Q3-ADR-001).
Any violation maps to `400 {"error": "malformed", "detail": "..."}`.
"""

from __future__ import annotations

import json
import queue
import threading
from typing import Any

from _metrics import PrometheusLineBuilder
from control_plane.provenance import ProvenanceLog
from hot_store import HotStore
from proto import DecisionType, ObservabilityRegime, Trajectory
from schema_registry import Reject, SchemaRegistry
from schema_registry.versions import SchemaVersion

# Content type accepted on writes (Q3-ADR-003).
_CONTENT_TYPE = "application/x-protobuf"

# Phase-1A retry-after seconds reported on 503 queue-full responses.
# Constant; Phase-2+ may compute from queue drain rate.
_RETRY_AFTER_SEC = 5

# Rejection reason tokens — kept in one place so `metrics_lines` knows the
# full label set. Order matters only for output stability in tests.
_REJECTION_REASONS: tuple[str, ...] = (
    "schema_drain",
    "queue_full",
    "schema_unknown",
    "malformed",
    "too_large",
    "content_type",
)


class IngestAPI:
    """Write-path HTTP front door.

    Owns no persistent state. Holds an internal bounded `queue.Queue`
    for `queue_depth()` visibility, an internal `threading.Lock`
    guarding counter increments, and references to W2 collaborators
    (HotStore, SchemaRegistry, ProvenanceLog).
    """

    def __init__(
        self,
        hot_store: HotStore,
        schema_registry: SchemaRegistry,
        provenance_log: ProvenanceLog,
        queue_capacity: int = 4096,
        max_body_bytes: int = 64 * 1024 * 1024,
    ) -> None:
        if queue_capacity <= 0:
            raise ValueError(f"queue_capacity must be > 0; got {queue_capacity}")
        if max_body_bytes <= 0:
            raise ValueError(f"max_body_bytes must be > 0; got {max_body_bytes}")
        self._hot_store = hot_store
        self._schema_registry = schema_registry
        self._provenance_log = provenance_log
        self._queue_capacity = int(queue_capacity)
        self._max_body_bytes = int(max_body_bytes)
        self._queue: queue.Queue[tuple[bytes, int]] = queue.Queue(maxsize=self._queue_capacity)

        # Counters. Guarded by `_counter_lock`; never the queue lock —
        # the two locks are independent.
        self._counter_lock = threading.Lock()
        self._accepted_total = 0
        self._rejected_total: dict[str, int] = dict.fromkeys(_REJECTION_REASONS, 0)
        self._bytes_total = 0

    # ---------------- HTTP handlers (called by service.py) ----------------

    def handle_post_trajectories(
        self, body: bytes, content_type: str
    ) -> tuple[int, dict[str, str], bytes]:
        """Single-trajectory append. Returns (status, headers, body)."""
        # 1. Content-type gate.
        if not _content_type_matches(content_type):
            self._reject("content_type")
            return self._json(
                415,
                {"error": "content_type", "expected": _CONTENT_TYPE},
            )

        # 2. Body size gate.
        if len(body) > self._max_body_bytes:
            self._reject("too_large")
            return self._json(
                413,
                {"error": "too_large", "max_bytes": self._max_body_bytes},
            )

        # 3. Parse.
        trajectory = Trajectory()
        try:
            trajectory.ParseFromString(body)
        except Exception as parse_err:  # protobuf raises DecodeError
            self._reject("malformed")
            return self._json(
                400,
                {"error": "malformed", "detail": f"parse: {parse_err}"},
            )

        return self._ingest_one(trajectory, len(body))

    def handle_post_trajectories_batch(
        self, body: bytes, content_type: str
    ) -> tuple[int, dict[str, str], bytes]:
        """Batch append (length-delimited frames). Per-frame status."""
        # The :batch endpoint enforces the same gates at the body level.
        if not _content_type_matches(content_type):
            self._reject("content_type")
            return self._json(
                415,
                {"error": "content_type", "expected": _CONTENT_TYPE},
            )
        if len(body) > self._max_body_bytes:
            self._reject("too_large")
            return self._json(
                413,
                {"error": "too_large", "max_bytes": self._max_body_bytes},
            )

        from .framing import FramingError, parse_frames

        # Frame-level parse-of-the-body. Whole-body parse failure → 400.
        try:
            frames = parse_frames(body)
        except FramingError as framing_err:
            self._reject("malformed")
            return self._json(
                400,
                {"error": "malformed", "detail": f"framing: {framing_err}"},
            )

        # Per-frame loop. Each frame is independently parsed + ingested
        # so one bad frame fails only itself (spec lines 53-55).
        results: list[dict[str, Any]] = []
        for i, frame_bytes in enumerate(frames):
            trajectory = Trajectory()
            try:
                trajectory.ParseFromString(frame_bytes)
            except Exception as parse_err:
                self._reject("malformed")
                results.append(
                    {
                        "status": 400,
                        "error": "malformed",
                        "detail": f"frame[{i}] parse: {parse_err}",
                    }
                )
                continue

            status, _headers, frame_resp = self._ingest_one(trajectory, len(frame_bytes))
            entry: dict[str, Any] = {"status": status}
            try:
                entry.update(json.loads(frame_resp.decode("utf-8")))
            except (UnicodeDecodeError, json.JSONDecodeError):
                # Shouldn't happen — `_ingest_one` always returns JSON.
                # Defensive: surface the raw bytes hex so the operator sees
                # something useful instead of swallowing.
                entry["detail"] = f"frame[{i}] non-json response"
            results.append(entry)

        return self._json(207, {"frames": results})

    def handle_get_ingest_status(self) -> tuple[int, dict[str, str], bytes]:
        """Writer-side health probe.

        Returns the current queue depth, capacity, totals, and the
        SchemaRegistry's drain state (Phase-1A always "open").
        """
        with self._counter_lock:
            accepted = self._accepted_total
            rejected = sum(self._rejected_total.values())
        return self._json(
            200,
            {
                "queue_depth": self.queue_depth(),
                "queue_capacity": self._queue_capacity,
                "accepted_total": accepted,
                "rejected_total": rejected,
                "schema_drain_state": self._schema_registry.drain_state(),
            },
        )

    # ----------------- consumer + queue surface (W4 starts) ------------------

    def consumer_loop(self, stop_event: threading.Event) -> None:
        """Drain the queue while `stop_event` is unset.

        Phase-1A: the queue carries `(trajectory_id, ingest_ts_ns)` for
        queue-depth visibility only. The HTTP handler has already done
        the synchronous HotStore + ProvenanceLog write before enqueueing,
        so this loop just removes items. Errors are swallowed; the
        consumer must not crash the service thread (a crashed daemon
        would leak the queue depth gauge upward and break Lifecycle).
        """
        while not stop_event.is_set():
            try:
                self._queue.get(timeout=0.5)
            except queue.Empty:
                continue
            try:
                self._queue.task_done()
            except ValueError:
                # task_done() may raise if the queue was reinitialized
                # mid-flight; swallow rather than crashing the loop.
                pass

    def queue_depth(self) -> int:
        """Current queue depth (lock-free; backed by Queue.qsize)."""
        return self._queue.qsize()

    # ------------------------------ metrics ----------------------------------

    def metrics_lines(self, service_name: str) -> list[bytes]:
        """Prometheus text v0.0.4 lines for ingest-owned metrics.

        Line shape matches `pipeline/common/service_host.py:28-34`:
            <metric>{<labels>,service="<service>"} <value>

        Per spec lines 76-80:
        - sts2_q3_ingest_accepted_total{}
        - sts2_q3_ingest_rejected_total{reason=...} (one line per reason
          observed since boot — emitted only after first increment so the
          label set is stable in tests).
        - sts2_q3_ingest_queue_depth{} (sampled at call time)
        - sts2_q3_ingest_bytes_total{}
        """
        svc = str(service_name)
        with self._counter_lock:
            accepted = self._accepted_total
            rejected_by_reason = dict(self._rejected_total)
            bytes_total = self._bytes_total
        depth = self.queue_depth()

        builder = PrometheusLineBuilder(svc)
        builder.counter("sts2_q3_ingest_accepted_total", value=accepted)
        # Emit a stable sorted order for determinism in tests.
        for reason in sorted(rejected_by_reason):
            count = rejected_by_reason[reason]
            if count > 0:
                builder.counter(
                    "sts2_q3_ingest_rejected_total",
                    {"reason": reason},
                    value=count,
                )
        builder.gauge("sts2_q3_ingest_queue_depth", value=depth)
        builder.counter("sts2_q3_ingest_bytes_total", value=bytes_total)
        return builder.lines()

    # ----------------------------- internals ---------------------------------

    def _ingest_one(
        self, trajectory: Trajectory, body_size: int
    ) -> tuple[int, dict[str, str], bytes]:
        """Common ingest path shared by single + batch endpoints.

        Order of checks per the dispatch prompt:
        1. SchemaRegistry.validate (must pass before any further work).
        2. Trajectory invariants (decision_type / observability_regime /
           combat samples / provenance triple non-empty).
        3. Queue capacity (back-pressure → 503).
        4. HotStore.append (synchronous; assigns ingest_ts_ns).
        5. ProvenanceLog.append (synchronous; mandatory).
        6. Enqueue + return 202.
        """
        # 1. Schema gate. Maps Reject.http_status -> response per spec.
        schema_version = SchemaVersion(
            int(trajectory.schema_version.major),
            int(trajectory.schema_version.minor),
        )
        decision = self._schema_registry.validate(
            schema_version.as_tuple(),
            op="write",
        )
        if isinstance(decision, Reject):
            return self._map_schema_reject(decision)

        # 2. Invariants — distinct failures get distinct details so
        # writer-side logs are actionable.
        invariant_err = _check_invariants(trajectory)
        if invariant_err is not None:
            self._reject("malformed")
            return self._json(
                400,
                {"error": "malformed", "detail": invariant_err},
            )

        # 3. Back-pressure gate. We test for `full()` before doing any
        # write work so a saturated service doesn't waste a HotStore
        # write on a request we'll fail.
        if self._queue.full():
            self._reject("queue_full")
            return self._json(
                503,
                {"error": "queue_full", "retry_after_sec": _RETRY_AFTER_SEC},
            )

        # 4. HotStore write (synchronous; assigns ingest_ts_ns).
        trajectory_id = _new_trajectory_id()
        traj_bytes = trajectory.SerializeToString()
        try:
            ingest_ts_ns = self._hot_store.append(traj_bytes, trajectory_id)
        except Exception as hot_err:
            self._reject("malformed")  # closest extant reason; HotStore
            # write errors are extremely rare and not separately bucketed
            # in the spec metric label set. Surface in detail.
            return self._json(
                500,
                {"error": "hot_store_unavailable", "detail": str(hot_err)},
            )

        # 5. Provenance — non-droppable. Failure here means we may have
        # a row in HotStore without an audit log entry. Spec line 117:
        # "ControlPlane raises on append → IngestAPI returns 500, queue
        # depth unchanged. Mitigation for partial-state-write hazards."
        # We can't undo the HotStore write (no API for it) — Phase-1A
        # accepts that risk and returns 500 so the writer retries; the
        # duplicate write will be detected by deduplication at the
        # trainer side (Phase-2+ work).
        try:
            self._provenance_log.append(
                trajectory_id=trajectory_id.hex(),
                model_version=trajectory.model_version,
                sampling_mode=trajectory.sampling_mode,
                generator=trajectory.generator,
                ingest_ts_ns=ingest_ts_ns,
                schema_version=schema_version.as_tuple(),
            )
        except Exception as prov_err:
            return self._json(
                500,
                {"error": "provenance_unavailable", "detail": str(prov_err)},
            )

        # 6. Diagnostic enqueue + counters.
        try:
            self._queue.put_nowait((trajectory_id, ingest_ts_ns))
        except queue.Full:
            # Race: between `full()` check above and now another caller
            # filled the queue. Treat as 503 — we have NOT yet committed
            # the accept (the HotStore + provenance rows are written but
            # the queue is the back-pressure surface). This is a
            # pragmatic compromise; in Phase-2+ the queue is the
            # critical path and this race goes away.
            self._reject("queue_full")
            return self._json(
                503,
                {"error": "queue_full", "retry_after_sec": _RETRY_AFTER_SEC},
            )

        with self._counter_lock:
            self._accepted_total += 1
            self._bytes_total += int(body_size)

        return self._json(
            202,
            {
                "trajectory_id": trajectory_id.hex(),
                "ingest_ts_ns": ingest_ts_ns,
            },
        )

    def _map_schema_reject(self, reject: Reject) -> tuple[int, dict[str, str], bytes]:
        """Map a SchemaRegistry.Reject to the HTTP response shape."""
        if reject.reason == "schema_unknown":
            self._reject("schema_unknown")
            return self._json(
                reject.http_status,
                {
                    "error": "schema_unknown",
                    "accepted": [
                        {"major": maj, "minor": minr} for (maj, minr) in (reject.accepted or [])
                    ],
                },
            )
        if reject.reason == "schema_drain_stale":
            # Phase-1A: drain_state is always "open" so this path is
            # unreachable in normal smoke. Spec requires the mapping
            # exists for Phase-2+ enable to be a no-op.
            self._reject("schema_drain")
            return self._json(
                reject.http_status,
                {
                    "error": "schema_drain_stale",
                    "current_write_target": _format_version(
                        self._schema_registry.current_write_target()
                    ),
                },
            )
        # Unknown reason — defensive 400 surfaces the upstream rejection.
        self._reject("malformed")
        return self._json(
            reject.http_status,
            {"error": reject.reason},
        )

    def _reject(self, reason: str) -> None:
        with self._counter_lock:
            if reason not in self._rejected_total:
                # Unknown reason — bucket as malformed to avoid the
                # counter map growing unboundedly. Spec metric label
                # set is bounded.
                reason = "malformed"
            self._rejected_total[reason] += 1

    @staticmethod
    def _json(status: int, payload: dict[str, Any]) -> tuple[int, dict[str, str], bytes]:
        body = json.dumps(payload, ensure_ascii=False, sort_keys=True).encode("utf-8")
        headers = {
            "Content-Type": "application/json",
            "Content-Length": str(len(body)),
        }
        return status, headers, body


# -------------------------- module-private helpers --------------------------


def _content_type_matches(content_type: str) -> bool:
    """Strict match on `application/x-protobuf` ignoring trailing params.

    `Content-Type: application/x-protobuf; charset=binary` is acceptable.
    """
    if not content_type:
        return False
    primary = content_type.split(";", 1)[0].strip().lower()
    return primary == _CONTENT_TYPE


def _new_trajectory_id() -> bytes:
    """16 cryptographically-random bytes (HotStore key size)."""
    import os

    return os.urandom(16)


def _format_version(version: tuple[int, int]) -> dict[str, int]:
    return {"major": int(version[0]), "minor": int(version[1])}


def _check_invariants(trajectory: Trajectory) -> str | None:
    """Trajectory-level invariants. Returns error detail or None.

    Per dispatch prompt's "Constraints" block.
    """
    # Provenance triple non-empty (Q3-ADR-001).
    if not trajectory.model_version:
        return "model_version is empty"
    if not trajectory.sampling_mode:
        return "sampling_mode is empty"
    if not trajectory.generator:
        return "generator is empty"

    # Per-step invariants.
    for i, step in enumerate(trajectory.steps):
        if step.decision_type == DecisionType.DECISION_TYPE_UNSPECIFIED:
            return f"steps[{i}].decision_type is UNSPECIFIED"
        if step.observability_regime == ObservabilityRegime.OBSERVABILITY_REGIME_UNSPECIFIED:
            return f"steps[{i}].observability_regime is UNSPECIFIED"
        if step.decision_type == DecisionType.DECISION_TYPE_COMBAT:
            if len(step.combat_outcome_samples) < 1:
                # Q3-ADR-005: combat steps carry >=1 degenerate sample.
                return f"steps[{i}].combat_outcome_samples is empty (combat step)"
    return None

"""Sampler HTTP handler + sampling engine (Phase-1A, uniform mode only).

Implements `POST /sample`, `GET /sample/cursor/<id>`, and
`GET /sample/recent` per `modules/sampler.md`. Phase-1A scope:

- Uniform mode only. Prioritized (P2+) and stratified (P3+) reject
  with 400 malformed-mode.
- Cold-tier reads are P2+; `cold_only=true` short-circuits to an empty
  body + trailer `{"status":"exhausted"}` (spec line 17 + Q3-ADR-009).
- Schema-drain 503 path is wired but cannot fire in Phase-1A since
  SchemaRegistry stays `drain_state == "open"` (`modules/schema-registry.md`).
- Cursor LRU is in-process, bounded 1024, idle 300 s (lazy eviction).

Filter semantics (AND across fields; unspecified / None doesn't restrict):

- `decision_type`: list of DecisionType enum names (e.g., ["COMBAT"]) or
  numeric ids. Each candidate step matches if its `step.decision_type`
  is in the list. Step-grain, not trajectory-grain.
- `model_version`, `generator`, `sampling_mode`: list of strings.
  Trajectory-grain (the Trajectory header carries them).
- `schema_version`: {"major": int, "minor": int}. Trajectory.schema_version
  must equal exactly.
- `cold_only`: bool. Phase-1A only honors True (empty result); False is
  the implied default.
- `after_ts_ns`: int. Honored as the strictly-after start position into
  HotStore.scan.

Response framing — see `framing.py` module docstring. Briefly:
varint(len) || TrajectoryStep_bytes, repeated, then a final
varint(len) || JSON trailer `{"status":"ok"|"exhausted"}`.
"""

from __future__ import annotations

import json
import threading
from typing import Any, Iterator

from proto import DecisionType, Trajectory, TrajectoryStep
from schema_registry import Accept, Reject, SchemaRegistry
from schema_registry.versions import SchemaVersion

from .cursor import CursorCache, CursorState
from .framing import frame_payload, frame_trailer

# Over-fetch factor: HotStore.scan returns *trajectories*, the response is
# in *steps*. Most Phase-1 trajectories carry multiple steps so a 10x
# trajectory limit comfortably covers a batch_size of step demand. The
# loop bails out as soon as `batch_size` steps are produced; over-fetch
# is upper-bound only.
_SCAN_OVERFETCH = 10
_SCAN_OVERFETCH_MIN = 64  # don't under-fetch tiny batches into empty scans


def _enum_value(spec: Any) -> int | None:
    """Coerce a DecisionType enum value from a name or int spec.

    Accepts:
    - int: returned as-is.
    - str: looked up in DecisionType by name; supports the spec's short
      form ("COMBAT") and the proto canonical form ("DECISION_TYPE_COMBAT").
      Returns None if unknown (a filter list with unknown entries silently
      drops them — same behavior as if they had no candidates).
    """
    if isinstance(spec, int):
        return int(spec)
    if isinstance(spec, str):
        if spec in DecisionType.keys():
            return int(DecisionType.Value(spec))
        prefixed = f"DECISION_TYPE_{spec}"
        if prefixed in DecisionType.keys():
            return int(DecisionType.Value(prefixed))
    return None


class _Metrics:
    """Thread-safe counter set for the Sampler.

    Kept private to the module: `Sampler.metrics_lines` exposes the
    Prometheus-text rendering and is the only public surface.
    """

    def __init__(self) -> None:
        self._lock = threading.Lock()
        # {(mode, result): n}
        self._request_total: dict[tuple[str, str], int] = {}
        # {(mode, tier): n}
        self._rows_returned: dict[tuple[str, str], int] = {}
        # gauge
        self._cursor_count: int = 0
        # 503 counter (always 0 in Phase-1A; emitted for discoverability)
        self._schema_503: int = 0

    def request(self, mode: str, result: str) -> None:
        with self._lock:
            key = (mode, result)
            self._request_total[key] = self._request_total.get(key, 0) + 1

    def rows(self, mode: str, tier: str, n: int) -> None:
        if n <= 0:
            return
        with self._lock:
            key = (mode, tier)
            self._rows_returned[key] = self._rows_returned.get(key, 0) + n

    def schema_503(self) -> None:
        with self._lock:
            self._schema_503 += 1

    def set_cursor_count(self, n: int) -> None:
        with self._lock:
            self._cursor_count = int(n)

    def render(self, service_name: str) -> list[bytes]:
        with self._lock:
            request_total = dict(self._request_total)
            rows_returned = dict(self._rows_returned)
            cursor_count = self._cursor_count
            schema_503 = self._schema_503
        service = str(service_name)
        lines: list[bytes] = []
        for (mode, result), n in sorted(request_total.items()):
            lines.append(
                f'sts2_q3_sample_request_total{{mode="{mode}",'
                f'result="{result}",service="{service}"}} {n}'.encode("utf-8")
            )
        for (mode, tier), n in sorted(rows_returned.items()):
            lines.append(
                f'sts2_q3_sample_rows_returned_total{{mode="{mode}",'
                f'tier="{tier}",service="{service}"}} {n}'.encode("utf-8")
            )
        lines.append(
            f'sts2_q3_sample_cursor_count{{service="{service}"}} '
            f'{cursor_count}'.encode("utf-8")
        )
        lines.append(
            f'sts2_q3_sample_schema_503_total{{service="{service}"}} '
            f'{schema_503}'.encode("utf-8")
        )
        return lines


class Sampler:
    """Phase-1A Sampler — uniform mode, hot-tier only, cursor LRU resume.

    The constructor takes the W2 substrate it needs by dependency
    injection (HotStore + SchemaRegistry). The `cursor_cache_capacity`
    and `cursor_idle_timeout_seconds` parameters tune the LRU; defaults
    match the spec (1024 / 300 s).

    Optional `now_ns` and `recent_window_seconds` injections support
    deterministic testing of `handle_get_sample_recent`.
    """

    DEFAULT_RECENT_WINDOW_SECONDS = 300

    def __init__(
        self,
        hot_store: Any,
        schema_registry: SchemaRegistry,
        cursor_cache_capacity: int = 1024,
        cursor_idle_timeout_seconds: int = 300,
        recent_window_seconds: int = DEFAULT_RECENT_WINDOW_SECONDS,
        now_ns=None,
    ) -> None:
        self._hot = hot_store
        self._schema = schema_registry
        self._cursors = CursorCache(
            capacity=cursor_cache_capacity,
            idle_timeout_seconds=cursor_idle_timeout_seconds,
            now_ns=now_ns if now_ns is not None else __import__("time").time_ns,
        )
        self._recent_window_ns = int(recent_window_seconds) * 1_000_000_000
        self._now_ns = now_ns if now_ns is not None else __import__("time").time_ns
        self._metrics = _Metrics()

    # ---------- HTTP entrypoints ----------

    def handle_post_sample(
        self, body: bytes
    ) -> tuple[int, dict[str, str], bytes]:
        """Handle POST /sample.

        Returns `(status_code, response_headers, response_body)`. The
        caller (service.py at W4) is responsible for writing the tuple
        out over the HTTP server.
        """
        try:
            request = self._decode_request(body)
        except ValueError as exc:
            self._metrics.request("unknown", "err")
            return self._error_response(
                400, {"error": "malformed", "detail": str(exc)}
            )

        mode = request["mode"]
        if mode != "uniform":
            self._metrics.request(mode, "err")
            return self._error_response(
                400,
                {
                    "error": "malformed",
                    "detail": f"mode {mode!r} not supported in Phase-1A (uniform only)",
                },
            )

        filters = request["filters"]

        # SchemaRegistry gate — Phase-1A only schema_unknown(400) can fire.
        schema_filter = filters.get("schema_version")
        if schema_filter is not None:
            decision = self._schema.validate(
                (schema_filter["major"], schema_filter["minor"]), op="read"
            )
            if isinstance(decision, Reject):
                return self._map_schema_reject(mode, decision)

        # cold_only=true short-circuits in Phase-1A (no cold tier).
        if filters.get("cold_only", False):
            self._metrics.request(mode, "exhausted")
            return self._stream_response(iter(()), trailer="exhausted")

        cursor_id = request.get("cursor_id")
        state, cursor_id = self._resolve_cursor(mode, filters, cursor_id)
        if state is None:
            # cursor_id was supplied but missing/expired.
            self._metrics.request(mode, "err")
            return self._error_response(
                404, {"error": "cursor_not_found", "cursor_id": cursor_id}
            )

        batch_size = request["batch_size"]
        steps_iter, trailer, new_state = self._sample_uniform(
            state, batch_size
        )

        # Persist the advanced cursor state. If this is the first call
        # for a brand-new cursor, `cursor_id` is empty -> create; else
        # update in place. Cursor count metric is gauge-style refreshed
        # after the LRU mutation.
        if cursor_id:
            self._cursors.update(cursor_id, new_state)
        else:
            cursor_id = self._cursors.create(new_state)
        self._metrics.set_cursor_count(self._cursors.count())

        # Metric: 503 is the only "err" we don't count as ok/exhausted;
        # the request line counts a successful uniform request once.
        self._metrics.request(mode, trailer)

        # Materialize the iterator so we can count returned rows for the
        # rows-returned metric. Phase-1A batches are bounded so this is
        # cheap; if Phase-2+ wants true streaming, swap for an iterator-
        # wrapping counter.
        step_frames = list(steps_iter)
        self._metrics.rows(mode, "hot", len(step_frames))

        return self._stream_response(
            iter(step_frames), trailer=trailer, cursor_id=cursor_id
        )

    def handle_get_sample_cursor(
        self, cursor_id: str
    ) -> tuple[int, dict[str, str], bytes]:
        """Debug endpoint: return cursor state as JSON."""
        state = self._cursors.get(cursor_id)
        self._metrics.set_cursor_count(self._cursors.count())
        if state is None:
            return self._error_response(
                404, {"error": "cursor_not_found", "cursor_id": cursor_id}
            )
        body = json.dumps(
            {"cursor_id": cursor_id, **state.snapshot()},
            sort_keys=True,
        ).encode("utf-8")
        return (
            200,
            {
                "Content-Type": "application/json",
                "Content-Length": str(len(body)),
            },
            body,
        )

    def handle_get_sample_recent(
        self, batch_size: int = 512
    ) -> tuple[int, dict[str, str], bytes]:
        """Convenience wrapper for Q12 evaluation-harness.

        Equivalent to `POST /sample` with
        `{mode:"uniform", batch_size, filters:{after_ts_ns: now - 300s}}`.
        """
        after_ts_ns = max(0, self._now_ns() - self._recent_window_ns)
        body = json.dumps(
            {
                "mode": "uniform",
                "batch_size": int(batch_size),
                "filters": {"after_ts_ns": after_ts_ns},
            }
        ).encode("utf-8")
        return self.handle_post_sample(body)

    # ---------- metrics ----------

    def metrics_lines(self, service_name: str) -> list[bytes]:
        """Prometheus text v0.0.4 lines for the Sampler's owned metrics."""
        self._metrics.set_cursor_count(self._cursors.count())
        return self._metrics.render(service_name)

    # ---------- internals: request decode ----------

    @staticmethod
    def _decode_request(body: bytes) -> dict[str, Any]:
        """Parse + validate the POST /sample body.

        Raises ValueError with a human-readable detail on any structural
        problem. Defaults applied: filters={}, mode required, batch_size
        required + positive.
        """
        if not body:
            raise ValueError("empty body")
        try:
            payload = json.loads(body.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise ValueError(f"invalid JSON: {exc}") from exc
        if not isinstance(payload, dict):
            raise ValueError("body must be a JSON object")

        mode = payload.get("mode")
        if not isinstance(mode, str):
            raise ValueError("'mode' must be a string")

        batch_size = payload.get("batch_size")
        if not isinstance(batch_size, int) or isinstance(batch_size, bool):
            raise ValueError("'batch_size' must be a positive integer")
        if batch_size <= 0:
            raise ValueError("'batch_size' must be > 0")

        filters_raw = payload.get("filters", {})
        if not isinstance(filters_raw, dict):
            raise ValueError("'filters' must be an object")
        filters = Sampler._normalize_filters(filters_raw)

        cursor_id = payload.get("cursor_id")
        if cursor_id is not None and not isinstance(cursor_id, str):
            raise ValueError("'cursor_id' must be a string when present")

        return {
            "mode": mode,
            "batch_size": batch_size,
            "filters": filters,
            "cursor_id": cursor_id,
        }

    @staticmethod
    def _normalize_filters(raw: dict[str, Any]) -> dict[str, Any]:
        """Validate filter shape; coerce list-of-string fields."""
        out: dict[str, Any] = {}

        for field_name in ("model_version", "generator", "sampling_mode"):
            val = raw.get(field_name)
            if val is None:
                continue
            if not isinstance(val, list) or not all(
                isinstance(x, str) for x in val
            ):
                raise ValueError(
                    f"filter {field_name!r} must be a list of strings"
                )
            out[field_name] = list(val)

        dec = raw.get("decision_type")
        if dec is not None:
            if not isinstance(dec, list) or not all(
                isinstance(x, (str, int)) and not isinstance(x, bool)
                for x in dec
            ):
                raise ValueError(
                    "filter 'decision_type' must be a list of strings or ints"
                )
            out["decision_type"] = list(dec)

        schema = raw.get("schema_version")
        if schema is not None:
            if (
                not isinstance(schema, dict)
                or not isinstance(schema.get("major"), int)
                or isinstance(schema.get("major"), bool)
                or not isinstance(schema.get("minor"), int)
                or isinstance(schema.get("minor"), bool)
            ):
                raise ValueError(
                    "filter 'schema_version' must be {major: int, minor: int}"
                )
            out["schema_version"] = {
                "major": int(schema["major"]),
                "minor": int(schema["minor"]),
            }

        cold = raw.get("cold_only")
        if cold is not None:
            if not isinstance(cold, bool):
                raise ValueError("filter 'cold_only' must be bool")
            out["cold_only"] = bool(cold)

        after = raw.get("after_ts_ns")
        if after is not None:
            if not isinstance(after, int) or isinstance(after, bool):
                raise ValueError("filter 'after_ts_ns' must be an integer")
            if after < 0:
                raise ValueError("filter 'after_ts_ns' must be >= 0")
            out["after_ts_ns"] = int(after)

        return out

    # ---------- internals: cursor lifecycle ----------

    def _resolve_cursor(
        self,
        mode: str,
        filters: dict[str, Any],
        cursor_id: str | None,
    ) -> tuple[CursorState | None, str]:
        """Look up an existing cursor or stage a fresh one.

        Returns (state, cursor_id). cursor_id == "" signals "not yet
        created"; the caller will create after the first successful page.
        On a supplied-but-unknown cursor_id, returns (None, cursor_id) so
        the caller can raise 404.
        """
        if cursor_id:
            state = self._cursors.get(cursor_id)
            if state is None:
                return None, cursor_id
            return state, cursor_id
        # Fresh cursor: position_ts_ns = filters.after_ts_ns (default 0).
        # HotStore.scan is strictly-after; the W2 convention is that 0
        # returns from the start of the keyspace.
        position = int(filters.get("after_ts_ns", 0))
        state = CursorState(
            mode=mode,
            filters=dict(filters),
            position_ts_ns=position,
            served_count=0,
        )
        return state, ""

    # ---------- internals: core sampling loop ----------

    def _sample_uniform(
        self, state: CursorState, batch_size: int
    ) -> tuple[Iterator[bytes], str, CursorState]:
        """Phase-1A uniform sample loop.

        Iterates `hot_store.scan(after_ts_ns=state.position_ts_ns,
        limit=batch_size * _SCAN_OVERFETCH)`, deserializes trajectories,
        applies trajectory-grain filters, then iterates trajectory steps
        applying step-grain filters. Yields serialized + framed step
        bytes lazily.

        Returns (framed_step_iterator, trailer_status, new_state). Trailer
        is "ok" if the iterator was bounded by batch_size (the scan
        may or may not have more rows), "exhausted" if the underlying
        scan returned fewer trajectories than the over-fetch limit and we
        ran out of rows to consider.

        Cursor advancement (handles mid-trajectory batch boundary):
        - If the loop finishes a trajectory cleanly, position_ts_ns =
          that trajectory's ts (next resume's strictly-after skips it).
        - If the loop stops mid-trajectory at ts T, position_ts_ns is
          set to T - 1 (so the next resume's strictly-after scan
          re-includes that trajectory) and step_offset records the
          already-yielded count so the resume drops those steps. T is
          guaranteed >= 1 because HotStore assigns ns-precision wall-
          clock timestamps and position_ts_ns starts at 0 = "from the
          beginning."
        """
        filters = state.filters
        decision_type_filter = self._build_decision_type_filter(
            filters.get("decision_type")
        )
        model_version_filter = self._build_str_set(filters.get("model_version"))
        generator_filter = self._build_str_set(filters.get("generator"))
        sampling_mode_filter = self._build_str_set(filters.get("sampling_mode"))
        schema_filter = filters.get("schema_version")
        filter_version: SchemaVersion | None = (
            SchemaVersion(
                int(schema_filter["major"]), int(schema_filter["minor"])
            )
            if schema_filter is not None
            else None
        )

        scan_limit = max(_SCAN_OVERFETCH_MIN, int(batch_size) * _SCAN_OVERFETCH)
        emitted_frames: list[bytes] = []
        # Track the ts of the last *fully drained* trajectory so the next
        # resume's strictly-after scan never accidentally skips a
        # partially-served trajectory.
        last_fully_drained_ts: int = state.position_ts_ns
        # Inside the current trajectory, how many steps already yielded.
        # Initial value is the inherited step_offset (left over from a
        # prior page that stopped mid-trajectory).
        pending_offset: int = state.step_offset
        # On exit, if we stop mid-trajectory we record (T, offset_so_far).
        partial_ts: int | None = None
        partial_offset: int = 0
        scan_yielded = 0

        try:
            scan_iter = self._hot.scan(
                after_ts_ns=state.position_ts_ns, limit=scan_limit
            )
        except TypeError:
            # HotStore.scan(after_ts_ns, limit) — positional fallback.
            scan_iter = self._hot.scan(state.position_ts_ns, scan_limit)

        for ts_ns, _traj_id, traj_bytes in scan_iter:
            scan_yielded += 1

            traj = Trajectory()
            try:
                traj.ParseFromString(traj_bytes)
            except Exception:
                # Corrupt row: skip silently. Treat as fully drained so
                # we don't loop forever on the same row across resumes.
                last_fully_drained_ts = ts_ns
                pending_offset = 0
                continue

            if model_version_filter is not None and (
                traj.model_version not in model_version_filter
            ):
                last_fully_drained_ts = ts_ns
                pending_offset = 0
                continue
            if generator_filter is not None and (
                traj.generator not in generator_filter
            ):
                last_fully_drained_ts = ts_ns
                pending_offset = 0
                continue
            if sampling_mode_filter is not None and (
                traj.sampling_mode not in sampling_mode_filter
            ):
                last_fully_drained_ts = ts_ns
                pending_offset = 0
                continue
            if filter_version is not None and (
                SchemaVersion(
                    int(traj.schema_version.major),
                    int(traj.schema_version.minor),
                )
                != filter_version
            ):
                last_fully_drained_ts = ts_ns
                pending_offset = 0
                continue

            # Count yielded-from-this-trajectory inclusive of pending
            # offset so the cursor's served-from-this-traj counter is
            # absolute, not relative.
            yielded_from_this_traj = pending_offset
            for step_idx, step in enumerate(traj.steps):
                if step_idx < pending_offset:
                    continue  # already served by an earlier page
                if decision_type_filter is not None and (
                    int(step.decision_type) not in decision_type_filter
                ):
                    # Still count this step as "processed" so resume
                    # doesn't re-evaluate it. The pending_offset of the
                    # next page must include skipped + yielded.
                    yielded_from_this_traj = step_idx + 1
                    continue
                emitted_frames.append(frame_payload(step.SerializeToString()))
                yielded_from_this_traj = step_idx + 1
                if len(emitted_frames) >= batch_size:
                    break

            if len(emitted_frames) >= batch_size:
                if yielded_from_this_traj >= len(traj.steps):
                    last_fully_drained_ts = ts_ns
                    pending_offset = 0
                else:
                    # Stopped mid-trajectory; record partial state for
                    # the cursor.
                    partial_ts = ts_ns
                    partial_offset = yielded_from_this_traj
                break

            # Trajectory fully drained for this page.
            last_fully_drained_ts = ts_ns
            pending_offset = 0

        # Trailer: "exhausted" iff the underlying scan returned strictly
        # fewer trajectories than the over-fetch limit AND we did not
        # hit batch_size. "ok" otherwise (more rows may exist).
        if len(emitted_frames) >= batch_size:
            trailer = "ok"
        elif scan_yielded >= scan_limit:
            # Hit the scan-limit safety bound; be conservative.
            trailer = "ok"
        else:
            trailer = "exhausted"

        # Compute the new cursor position. Strict-after semantics on
        # HotStore.scan means we record the largest ts we want the NEXT
        # call to NOT include. For mid-trajectory partial consumption,
        # set position to (partial_ts - 1) so re-scan re-includes it.
        if partial_ts is not None:
            new_position = max(0, partial_ts - 1)
            new_step_offset = partial_offset
        else:
            new_position = last_fully_drained_ts
            new_step_offset = 0

        new_state = CursorState(
            mode=state.mode,
            filters=dict(state.filters),
            position_ts_ns=new_position,
            step_offset=new_step_offset,
            served_count=state.served_count + len(emitted_frames),
        )
        return iter(emitted_frames), trailer, new_state

    @staticmethod
    def _build_str_set(spec: list[str] | None) -> set[str] | None:
        if spec is None:
            return None
        return set(spec)

    @staticmethod
    def _build_decision_type_filter(
        spec: list[Any] | None,
    ) -> set[int] | None:
        if spec is None:
            return None
        out: set[int] = set()
        for entry in spec:
            val = _enum_value(entry)
            if val is not None:
                out.add(val)
        return out

    # ---------- internals: response shaping ----------

    def _stream_response(
        self,
        step_frames: Iterator[bytes],
        trailer: str,
        cursor_id: str | None = None,
    ) -> tuple[int, dict[str, str], bytes]:
        """Concatenate already-framed step bytes + framed trailer.

        Phase-1A buffers the full response in memory (Phase-2+ may stream
        chunked). Headers include the cursor id for the caller to echo
        back; the body is documented in `framing.py`.
        """
        body = b"".join(step_frames) + frame_trailer(trailer)
        headers: dict[str, str] = {
            "Content-Type": "application/octet-stream",
            "Content-Length": str(len(body)),
            "X-Sts2-Q3-Trailer": trailer,
        }
        if cursor_id:
            headers["X-Sts2-Q3-Cursor-Id"] = cursor_id
        return 200, headers, body

    @staticmethod
    def _error_response(
        status: int, payload: dict[str, Any]
    ) -> tuple[int, dict[str, str], bytes]:
        body = json.dumps(payload, sort_keys=True).encode("utf-8")
        return (
            status,
            {
                "Content-Type": "application/json",
                "Content-Length": str(len(body)),
            },
            body,
        )

    def _map_schema_reject(
        self, mode: str, decision: Reject
    ) -> tuple[int, dict[str, str], bytes]:
        """Translate a SchemaRegistry Reject into the spec's HTTP response.

        Phase-1A only schema_unknown(400) is reachable; the 503 path is
        wired so Phase-2+ schema-flip doesn't need a code change.
        """
        if decision.http_status == 503:
            self._metrics.schema_503()
            self._metrics.request(mode, "err")
            return self._error_response(
                503,
                {
                    "reason": "schema_drain",
                    "retry_after_sec": int(decision.retry_after_sec or 5),
                },
            )
        # 400 schema_unknown (and any future 4xx) — emit accepted list
        # for diagnostic visibility.
        self._metrics.request(mode, "err")
        payload: dict[str, Any] = {
            "error": "malformed",
            "detail": f"schema rejected: {decision.reason}",
        }
        if decision.accepted is not None:
            payload["accepted"] = [
                {"major": maj, "minor": minr} for (maj, minr) in decision.accepted
            ]
        return self._error_response(decision.http_status, payload)

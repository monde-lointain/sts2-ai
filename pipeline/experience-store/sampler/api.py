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

from schema_registry import Reject, SchemaRegistry

from .cursor import CursorCache, CursorState
from .engine import SamplingEngine
from .framing import frame_trailer


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
        self._engine = SamplingEngine(hot_store, schema_registry)
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
        """Delegate to SamplingEngine. Kept as a thin shim so the HTTP
        handler's call site stays readable; engine owns the algorithm."""
        return self._engine.sample(state, batch_max=batch_size, filters=state.filters)

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

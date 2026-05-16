"""SchemaRegistry: wire-version policy + drain/flip FSM (Phase-1A).

Implements `pipeline/experience-store/docs/specs/modules/schema-registry.md`
for S0 Phase-1A. Single source-of-truth for accepted `(major, minor)`
wire-schema versions; rejects stale-version writes structurally on
`validate(version, op)`.

Phase-1A scope:
- `drain_state == "open"` always (FSM degenerate; operator endpoints not
  implemented). `drain`, `flip`, `revert` raise NotImplementedError marked
  `# TODO: Phase-1 close`.
- Persistence + sentinel file written on init.
- `validate(version, op)` gate fully active; Phase-1 rule set per spec
  section "Internal communication".
- Metrics emitted in Prometheus text v0.0.4 shape compatible with
  `pipeline/common/service_host.py:28-34`.

Cross-submodule consumers (W3+):
- IngestAPI: `validate(version, op="write")` per POST.
- Sampler: `validate(version, op="read")` per request.
- Lifecycle: `current_write_target()` for cold-tier sentinel stamp.
- ControlPlane.ObservabilityAdapter / service.py: `current_health_schema()`
  to override the stock `/health` `"schema": 0`.
"""

from __future__ import annotations

import json
import pathlib
import threading
from typing import Literal

from _atomic_io import atomic_write_json
from _metrics import PrometheusLineBuilder

from .decision import Accept, Decision, Reject
from .versions import PHASE1, PHASE1_1, SchemaVersion

SCHEMA_DIR = "schema"
REGISTRY_FILE = "registry.json"
MIGRATION_LOG_FILE = "migration_log.ndjson"

DrainState = Literal["open", "draining", "locked"]


def _sentinel_name(major: int, minor: int) -> str:
    """`schema/<major>.<minor>.active` per spec section Data Ownership."""
    return f"{int(major)}.{int(minor)}.active"


class SchemaRegistry:
    """In-process wire-version registry and drain/flip FSM.

    Phase-1A: degenerate `drain_state == "open"`. Validate gate active for
    all three Phase-1 reject conditions (unknown / drain_stale / flip);
    drain_stale and flip rejects are unreachable in Phase-1A by construction
    but the code path exists so Phase-2+ transitions are a no-op enable.
    """

    def __init__(self, data_dir: pathlib.Path) -> None:
        self._data_dir = pathlib.Path(data_dir)
        self._schema_dir = self._data_dir / SCHEMA_DIR
        self._schema_dir.mkdir(parents=True, exist_ok=True)

        self._registry_path = self._schema_dir / REGISTRY_FILE
        self._migration_log_path = self._schema_dir / MIGRATION_LOG_FILE

        # Validate-counter map keyed by (op, result); lazy initialization
        # keeps the metric surface tied to observed traffic. The full key
        # space is bounded (op in {read,write}, result in {accept,
        # schema_unknown, schema_drain_stale, schema_flip}) so unbounded
        # growth isn't a concern.
        self._validate_counts: dict[tuple[str, str], int] = {}

        # Single lock guards (a) state reads/writes (state is constant in
        # Phase-1A but Phase-2+ flip flips it under this lock), (b) validate
        # counter increments. Threading.Lock is sufficient: validate is
        # short, lock-free read of state would race against a future flip.
        self._lock = threading.Lock()

        self._load_or_init()

    # ---------- init / persistence ----------

    def _load_or_init(self) -> None:
        if self._registry_path.exists():
            self._load()
        else:
            self._init_fresh()

        # Sentinel is reconciled on every boot: ensure the file matching
        # `current_write_target` exists. Phase-2+ flip is responsible for
        # removing the prior sentinel; Phase-1A there's only one target so
        # we just touch.
        sentinel = self._schema_dir / _sentinel_name(
            self._current_write_target.major, self._current_write_target.minor
        )
        if not sentinel.exists():
            sentinel.touch()

        # Migration log: ensure the file exists for downstream rotation
        # logic; Phase-1A never writes a record (no transitions).
        if not self._migration_log_path.exists():
            self._migration_log_path.touch()

    def _init_fresh(self) -> None:
        self._accepted: list[SchemaVersion] = [PHASE1, PHASE1_1]
        self._current_write_target: SchemaVersion = PHASE1_1
        self._drain_state: DrainState = "open"
        self._drain_target: SchemaVersion | None = None
        self._persist()

    def _load(self) -> None:
        with self._registry_path.open("r", encoding="utf-8") as handle:
            data = json.load(handle)
        self._accepted = [SchemaVersion.from_fields(item) for item in data["accepted"]]
        self._current_write_target = SchemaVersion.from_fields(data["current_write_target"])
        self._drain_state = data["drain_state"]
        dt = data.get("drain_target")
        self._drain_target = SchemaVersion.from_fields(dt) if dt else None

    def _persist(self) -> None:
        """Atomic write of registry.json via `atomic_write_json` helper.

        Phase-1A never calls this after _init_fresh; the path exists for
        Phase-2+ drain/flip transitions to keep the on-disk state
        consistent under crash.
        """
        payload = {
            "accepted": [v.as_fields() for v in self._accepted],
            "current_write_target": self._current_write_target.as_fields(),
            "drain_state": self._drain_state,
            "drain_target": (
                self._drain_target.as_fields() if self._drain_target is not None else None
            ),
        }
        self._schema_dir.mkdir(parents=True, exist_ok=True)
        atomic_write_json(self._registry_path, payload)

    # ---------- public API per spec ----------

    def validate(self, version: tuple[int, int], op: Literal["read", "write"]) -> Decision:
        """Gate per spec section Internal communication, Phase-1 rule set.

        Order matters:
        1. Unknown version -> 400 schema_unknown (always, regardless of op).
        2. op=write AND draining AND version != drain_target -> 423 schema_drain_stale.
        3. op=read AND locked -> 503 schema_flip (retry_after_sec=5).
        4. Else Accept.
        """
        if op not in ("read", "write"):
            raise ValueError(f"op must be 'read' or 'write'; got {op!r}")
        ver = SchemaVersion.from_tuple((int(version[0]), int(version[1])))

        with self._lock:
            accepted_snapshot = list(self._accepted)
            drain_state = self._drain_state
            drain_target = self._drain_target

            if ver not in accepted_snapshot:
                self._validate_counts[(op, "schema_unknown")] = (
                    self._validate_counts.get((op, "schema_unknown"), 0) + 1
                )
                return Reject(
                    reason="schema_unknown",
                    http_status=400,
                    accepted=[v.as_tuple() for v in accepted_snapshot],
                )

            if op == "write" and drain_state == "draining" and ver != drain_target:
                self._validate_counts[(op, "schema_drain_stale")] = (
                    self._validate_counts.get((op, "schema_drain_stale"), 0) + 1
                )
                return Reject(reason="schema_drain_stale", http_status=423)

            if op == "read" and drain_state == "locked":
                self._validate_counts[(op, "schema_flip")] = (
                    self._validate_counts.get((op, "schema_flip"), 0) + 1
                )
                return Reject(
                    reason="schema_flip",
                    http_status=503,
                    retry_after_sec=5,
                )

            self._validate_counts[(op, "accept")] = self._validate_counts.get((op, "accept"), 0) + 1
            return Accept()

    def current_health_schema(self) -> int:
        """Major of current write target; ControlPlane.ObservabilityAdapter
        reads this to override the stock `/health` `"schema": 0`."""
        with self._lock:
            return self._current_write_target.major

    def current_write_target(self) -> tuple[int, int]:
        with self._lock:
            return self._current_write_target.as_tuple()

    def accepted(self) -> list[tuple[int, int]]:
        with self._lock:
            return [v.as_tuple() for v in self._accepted]

    def drain_state(self) -> DrainState:
        with self._lock:
            return self._drain_state

    def state_snapshot(self) -> dict:
        """registry.json content as a dict for GET /schema (W3 handler)."""
        with self._lock:
            return {
                "accepted": [v.as_fields() for v in self._accepted],
                "current_write_target": self._current_write_target.as_fields(),
                "drain_state": self._drain_state,
                "drain_target": (
                    self._drain_target.as_fields() if self._drain_target is not None else None
                ),
            }

    def metrics_lines(self, service_name: str) -> list[bytes]:
        """Prometheus text v0.0.4 lines for SchemaRegistry's owned metrics.

        Line shape matches `pipeline/common/service_host.py:28-34`:
            <metric>{<labels>,service="<service>"} <value>

        Emits:
        - sts2_q3_schema_state{state=...} 0/1 — gauge, one line per state.
        - sts2_q3_schema_validate_total{op,result} N — counter, one line
          per observed (op, result) combo.
        - sts2_q3_schema_migration_total{from,to} 0 — counter, single
          baseline line for the no-op (1.0)->(1.0) so the metric is
          discoverable in Phase-1A.
        """
        with self._lock:
            current_state = self._drain_state
            counts = dict(self._validate_counts)
            cwt = self._current_write_target

        builder = PrometheusLineBuilder(str(service_name))
        for state in ("open", "draining", "locked"):
            builder.gauge(
                "sts2_q3_schema_state",
                {"state": state},
                1 if state == current_state else 0,
            )

        # Counter lines are emitted in a stable sort order (op asc, result
        # asc) so test assertions and scrape diffs are deterministic.
        for (op, result), count in sorted(counts.items()):
            builder.counter(
                "sts2_q3_schema_validate_total",
                {"op": op, "result": result},
                count,
            )

        baseline = str(cwt)
        builder.counter(
            "sts2_q3_schema_migration_total",
            {"from": baseline, "to": baseline},
            0,
        )
        return builder.lines()

    # ---------- operator API (Phase-1 close) ----------

    def drain(self, target: tuple[int, int]) -> None:
        # TODO: Phase-1 close — operator-initiated drain start.
        raise NotImplementedError("drain: Phase-1 close")

    def flip(self) -> None:
        # TODO: Phase-1 close — promote drain_target to current; sentinel swap.
        raise NotImplementedError("flip: Phase-1 close")

    def revert(self) -> None:
        # TODO: Phase-1 close — undo drain before flip.
        raise NotImplementedError("revert: Phase-1 close")

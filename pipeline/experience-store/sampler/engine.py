"""SamplingEngine — filter algebra + scan loop + frame emission.

Extracted from `Sampler` (R3a) so the HTTP shell stays small and the
sampling core can be unit-tested without spinning a CursorCache, an
HTTP request decoder, or response shaping. The Sampler keeps cursor
LRU + HTTP wiring; the engine owns the hot-store scan, trajectory-grain
+ step-grain filters, batch-bounded step yield, and cursor advance.

Construction takes the W2 substrate (`hot_store`, `schema_registry`)
by dependency injection — same shape as the previous `Sampler` did.
The engine never reaches back into the Sampler.
"""

from __future__ import annotations

from typing import Any, Iterator

from proto import DecisionType, Trajectory
from schema_registry.versions import SchemaVersion

from .cursor import CursorState
from .framing import frame_payload

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


class SamplingEngine:
    """Phase-1A uniform sampling core.

    Pure function over (cursor state, batch budget, filters) -> (frames,
    trailer status, advanced cursor state). Holds no per-request state;
    safe to share across HTTP threads provided the underlying HotStore
    + SchemaRegistry are themselves thread-safe (they are, per W2).
    """

    def __init__(self, hot_store: Any, schema_registry: Any) -> None:
        self._hot = hot_store
        self._schema = schema_registry

    def sample(
        self,
        state: CursorState,
        batch_max: int,
        filters: dict[str, Any] | None = None,
    ) -> tuple[Iterator[bytes], str, CursorState]:
        """Phase-1A uniform sample loop.

        Iterates `hot_store.scan(after_ts_ns=state.position_ts_ns,
        limit=batch_max * _SCAN_OVERFETCH)`, deserializes trajectories,
        applies trajectory-grain filters, then iterates trajectory steps
        applying step-grain filters. Yields serialized + framed step
        bytes lazily.

        Returns (framed_step_iterator, trailer_status, new_state). Trailer
        is "ok" if the iterator was bounded by batch_max (the scan
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
        if filters is None:
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

        scan_limit = max(_SCAN_OVERFETCH_MIN, int(batch_max) * _SCAN_OVERFETCH)
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
                if len(emitted_frames) >= batch_max:
                    break

            if len(emitted_frames) >= batch_max:
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
        # hit batch_max. "ok" otherwise (more rows may exist).
        if len(emitted_frames) >= batch_max:
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

"""Length-delimited protobuf framing for the Sampler read path.

Varint + payload framing primitives are canonical in
``pipeline/experience-store/_framing.py`` (shared with IngestAPI).
This module re-exports those primitives and adds the sampler-specific
helpers: ``frame_steps`` (lazy frame generator over TrajectoryStep
iterables), ``frame_trailer`` (terminator JSON frame), and
``is_degenerate_combat_sample`` (Q3-ADR-005 filter helper).
"""

from __future__ import annotations

from typing import Iterable, Iterator

from proto import DecisionType, TrajectoryStep

from _framing import (
    FramingError,
    decode_varint,
    encode_varint,
    frame_payload,
)

__all__ = [
    "FramingError",
    "decode_varint",
    "encode_varint",
    "frame_payload",
    "frame_steps",
    "frame_trailer",
    "is_degenerate_combat_sample",
]


def frame_steps(steps: Iterable[TrajectoryStep]) -> Iterator[bytes]:
    """Yield one length-delimited frame per serialized `TrajectoryStep`.

    Lazy: serializes on demand so a 500-row response doesn't double the
    in-memory footprint (callers may stream-write to the wire as frames
    are produced).
    """
    for step in steps:
        yield frame_payload(step.SerializeToString())


def frame_trailer(status: str) -> bytes:
    """Build the terminator frame.

    `status` must be `"ok"` or `"exhausted"` per spec line 66. Other
    values raise ValueError so the wire never carries an undefined token.
    The trailer's payload is canonical JSON: `{"status":"<status>"}`.
    """
    if status not in ("ok", "exhausted"):
        raise ValueError(
            f"trailer status must be 'ok' or 'exhausted'; got {status!r}"
        )
    json_bytes = f'{{"status":"{status}"}}'.encode("utf-8")
    return frame_payload(json_bytes)


def is_degenerate_combat_sample(step: TrajectoryStep) -> bool:
    """Detect Phase-1 degenerate combat samples per Q3-ADR-005.

    Phase-1 combat steps carry exactly one `CombatOutcomeSample` with
    `probability_weight=1.0` and `hp_delta` mirroring
    `combat_outcome_summary.expected_hp_delta`. Distributional analyses
    in Phase-2+ MUST filter these rows out (their delta distribution
    is degenerate by construction).

    This helper is exposed at module level for Q10 trainer consumption;
    the Sampler does NOT auto-filter (the trainer decides — see prompt).
    """
    if step.decision_type != DecisionType.DECISION_TYPE_COMBAT:
        return False
    if len(step.combat_outcome_samples) != 1:
        return False
    sample = step.combat_outcome_samples[0]
    if sample.probability_weight != 1.0:
        return False
    # Mirror condition per spec lines 132-137: sample.hp_delta ==
    # summary.expected_hp_delta. Float equality is appropriate here —
    # Phase-1 populates both fields from the same scalar value.
    if sample.hp_delta != step.combat_outcome_summary.expected_hp_delta:
        return False
    return True

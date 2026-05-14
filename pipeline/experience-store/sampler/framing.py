"""Length-delimited protobuf framing for the Sampler read path.

Wire format (Phase-1A, per Q3-ADR-003 + `modules/sampler.md` line 65):

    frame := varint(payload_len) || payload_bytes
    stream := frame+ trailer
    trailer := frame(json_bytes)  /* payload is JSON: {"status":"ok"|"exhausted"} */

Each `TrajectoryStep` is encoded as one frame whose payload is the
protobuf wire bytes of the step. After all step frames, a single trailer
frame carries the terminator status as UTF-8 JSON. The reader peels one
varint, reads that many bytes, repeats; when the payload parses as JSON
with a `status` field, the stream is complete.

The varint encoding matches protobuf's standard
`writeRawVarint32`/`MessageToLengthPrefixedFile` convention: little-endian
groups of seven payload bits with the MSB set on all but the final byte.
Limit: 10 bytes (covers full uint64 range; protobuf-canonical).

This module is also the home of the Phase-1 degenerate-combat-sample
filter helper (Q3-ADR-005) — downstream consumers import
`is_degenerate_combat_sample`. The Sampler itself MUST NOT auto-filter:
per the prompt, Q10 trainer decides.
"""

from __future__ import annotations

from typing import Iterable, Iterator

from proto import DecisionType, TrajectoryStep

# protobuf varint is canonically capped at 10 bytes (uint64). Keep the
# constant local so downstream callers don't have to import google.protobuf
# internals.
_VARINT_MAX_BYTES = 10


def encode_varint(value: int) -> bytes:
    """Encode a non-negative integer as a protobuf-canonical varint.

    Raises ValueError on negative inputs or values that would exceed the
    10-byte protobuf cap (uint64).
    """
    if value < 0:
        raise ValueError(f"varint value must be non-negative; got {value}")
    if value > 0xFFFFFFFFFFFFFFFF:
        raise ValueError(f"varint value exceeds uint64 range; got {value}")
    out = bytearray()
    while True:
        if value < 0x80:
            out.append(value)
            return bytes(out)
        out.append((value & 0x7F) | 0x80)
        value >>= 7


def decode_varint(buf: bytes, offset: int = 0) -> tuple[int, int]:
    """Decode a varint from `buf` starting at `offset`.

    Returns `(value, bytes_consumed)`. Raises ValueError if the varint is
    truncated or runs past the 10-byte protobuf cap.
    """
    value = 0
    shift = 0
    consumed = 0
    while True:
        if offset + consumed >= len(buf):
            raise ValueError("truncated varint: buffer ended mid-varint")
        if consumed >= _VARINT_MAX_BYTES:
            raise ValueError(
                f"varint exceeds {_VARINT_MAX_BYTES}-byte protobuf cap"
            )
        b = buf[offset + consumed]
        consumed += 1
        value |= (b & 0x7F) << shift
        if not (b & 0x80):
            return value, consumed
        shift += 7


def frame_payload(payload: bytes) -> bytes:
    """Wrap a payload in one length-delimited frame: varint(len) || payload."""
    if not isinstance(payload, (bytes, bytearray)):
        raise TypeError(
            f"payload must be bytes; got {type(payload).__name__}"
        )
    return encode_varint(len(payload)) + bytes(payload)


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

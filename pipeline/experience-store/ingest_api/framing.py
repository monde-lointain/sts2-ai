"""Length-delimited protobuf frame parser (S0.C.alpha).

Per `docs/specs/modules/ingest-api.md` `POST /trajectories:batch`:
- Body is a sequence of length-delimited frames.
- Each frame: <varint length><N bytes of serialized Trajectory>.
- Matches protobuf's canonical wire `length-delimited` framing — same
  convention `Sampler.POST /sample` will emit (Q3-ADR-003).

The encoder/decoder are hand-rolled rather than importing
`google.protobuf.internal.encoder._VarintEncoder` because the upstream
helpers expect an int-accepting `write` callback that differs subtly
between protobuf versions and is documented as private. Hand-rolled
varint is a few lines and stable across CPython releases.
"""

from __future__ import annotations

from typing import Iterator

# Maximum varint length on the wire: 10 bytes (uint64). Frames whose
# length prefix exceeds this are rejected as malformed; this caps the
# decoder work per malformed input.
_MAX_VARINT_BYTES = 10


class FramingError(ValueError):
    """Raised on malformed length-delimited input.

    Inherits ValueError so callers can catch as a single category.
    Distinct subtype lets the IngestAPI handler tag the 400 detail
    with `framing` rather than `malformed` when desired (Phase-1A
    bundles both as `malformed`).
    """


def encode_varint(value: int) -> bytes:
    """Encode a non-negative int as a protobuf base-128 varint."""
    if value < 0:
        raise ValueError(f"varint value must be non-negative; got {value}")
    out = bytearray()
    while True:
        b = value & 0x7F
        value >>= 7
        if value:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)


def decode_varint(buf: bytes, offset: int) -> tuple[int, int]:
    """Decode a varint starting at `offset`. Returns (value, new_offset).

    Raises FramingError on truncated or over-long input.
    """
    value = 0
    shift = 0
    consumed = 0
    pos = offset
    n = len(buf)
    while True:
        if pos >= n:
            raise FramingError(
                f"truncated varint at offset {offset}: end-of-buffer reached"
            )
        consumed += 1
        if consumed > _MAX_VARINT_BYTES:
            raise FramingError(
                f"varint at offset {offset} exceeds {_MAX_VARINT_BYTES} bytes"
            )
        b = buf[pos]
        pos += 1
        value |= (b & 0x7F) << shift
        if not (b & 0x80):
            return value, pos
        shift += 7


def encode_frame(payload: bytes) -> bytes:
    """Encode one frame: varint(len(payload)) || payload."""
    return encode_varint(len(payload)) + bytes(payload)


def encode_frames(payloads: list[bytes] | tuple[bytes, ...]) -> bytes:
    """Encode a sequence of frames into one contiguous buffer."""
    out = bytearray()
    for p in payloads:
        out += encode_frame(p)
    return bytes(out)


def iter_frames(buf: bytes) -> Iterator[bytes]:
    """Yield each frame's payload from a length-delimited buffer.

    Raises FramingError on truncated frame or unexpected trailing bytes.
    """
    offset = 0
    n = len(buf)
    while offset < n:
        length, after_len = decode_varint(buf, offset)
        end = after_len + length
        if end > n:
            raise FramingError(
                f"truncated frame at offset {offset}: declared length "
                f"{length} but only {n - after_len} bytes remain"
            )
        yield bytes(buf[after_len:end])
        offset = end


def parse_frames(buf: bytes) -> list[bytes]:
    """Eager equivalent of `iter_frames`. Returns list of payload bytes."""
    return list(iter_frames(buf))

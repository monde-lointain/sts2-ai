"""Shared length-delimited varint + frame codec.

Both `ingest_api/framing.py` and `sampler/framing.py` had hand-rolled
copies of `encode_varint`/`decode_varint` that diverged in subtle ways:
sampler returned `(value, consumed_bytes)` (a count) while ingest returned
`(value, new_offset)` (an absolute position). This module is the single
source of truth; submodule `framing.py` files become thin re-export shims.

Unified `decode_varint` semantics: returns `(value, consumed_bytes)` —
the number of bytes consumed starting at `offset`. Callers that need an
absolute new offset compute `offset + consumed`.

Wire format (protobuf-canonical):

    varint    := base-128 little-endian, MSB=continuation, 1..10 bytes
    frame     := varint(len(payload)) || payload
    stream    := frame*

The module is intentionally pure-bytes — no `proto`, no `DecisionType`
imports. Domain-specific helpers (e.g. `is_degenerate_combat_sample`,
`frame_trailer`, `frame_steps`) remain in their respective modules.
"""

from __future__ import annotations

from typing import Iterator

# protobuf-canonical uint64 varint cap: 10 bytes. Length prefixes whose
# varint exceeds this are rejected as malformed, bounding decoder work
# per malformed input.
_VARINT_MAX_BYTES = 10

_UINT64_MAX = 0xFFFFFFFFFFFFFFFF


class FramingError(ValueError):
    """Raised on malformed length-delimited input.

    Inherits `ValueError` so callers can catch by either name; the
    distinct subtype lets handlers tag errors as `framing` specifically.
    """


def encode_varint(value: int) -> bytes:
    """Encode a non-negative int as a protobuf base-128 varint.

    Raises `ValueError` on negative inputs or values exceeding uint64
    (0xFFFFFFFFFFFFFFFF).
    """
    if value < 0:
        raise ValueError(f"varint value must be non-negative; got {value}")
    if value > _UINT64_MAX:
        raise ValueError(f"varint value exceeds uint64 range; got {value}")
    out = bytearray()
    while True:
        if value < 0x80:
            out.append(value)
            return bytes(out)
        out.append((value & 0x7F) | 0x80)
        value >>= 7


def decode_varint(buf: bytes, offset: int = 0) -> tuple[int, int]:
    """Decode a varint at `buf[offset:]`.

    Returns `(value, consumed_bytes)` where `consumed_bytes` is the
    number of bytes read (not an absolute offset). Raises `FramingError`
    on truncated input or varints exceeding the 10-byte uint64 cap.
    """
    value = 0
    shift = 0
    consumed = 0
    n = len(buf)
    while True:
        if offset + consumed >= n:
            raise FramingError(
                f"truncated varint at offset {offset}: end-of-buffer reached"
            )
        if consumed >= _VARINT_MAX_BYTES:
            raise FramingError(
                f"varint at offset {offset} exceeds "
                f"{_VARINT_MAX_BYTES}-byte protobuf cap"
            )
        b = buf[offset + consumed]
        consumed += 1
        value |= (b & 0x7F) << shift
        if not (b & 0x80):
            return value, consumed
        shift += 7


def frame_payload(payload: bytes) -> bytes:
    """Wrap a payload in one length-delimited frame: varint(len) || payload."""
    return encode_varint(len(payload)) + bytes(payload)


# Alias: ingest_api historically called this `encode_frame`. Keep the
# name as a binding (not a separate function body) so the two names
# stay byte-for-byte identical.
encode_frame = frame_payload


def encode_frames(payloads: list[bytes] | tuple[bytes, ...]) -> bytes:
    """Concat-encode a sequence of payloads into one contiguous buffer."""
    out = bytearray()
    for p in payloads:
        out += frame_payload(p)
    return bytes(out)


def iter_frames(buf: bytes) -> Iterator[bytes]:
    """Yield each frame's payload from a length-delimited buffer.

    Raises `FramingError` on truncated frame (declared length exceeds
    remaining buffer).
    """
    offset = 0
    n = len(buf)
    while offset < n:
        length, consumed = decode_varint(buf, offset)
        after_len = offset + consumed
        end = after_len + length
        if end > n:
            raise FramingError(
                f"truncated frame at offset {offset}: declared length "
                f"{length} but only {n - after_len} bytes remain"
            )
        yield bytes(buf[after_len:end])
        offset = end


def parse_frames(buf: bytes) -> list[bytes]:
    """Eager equivalent of `iter_frames`. Returns a list of payload bytes."""
    return list(iter_frames(buf))

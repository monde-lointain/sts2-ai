"""Tests for the shared length-delimited varint + frame codec."""

from __future__ import annotations

import pytest
from _framing import (
    FramingError,
    decode_varint,
    encode_frame,
    encode_frames,
    encode_varint,
    frame_payload,
    iter_frames,
    parse_frames,
)

_UINT64_MAX = 0xFFFFFFFFFFFFFFFF


# -- encode_varint ----------------------------------------------------------


def test_encode_varint_zero() -> None:
    assert encode_varint(0) == b"\x00"


def test_encode_varint_127_single_byte() -> None:
    assert encode_varint(127) == b"\x7f"


def test_encode_varint_128_msb_unset_on_last_byte() -> None:
    out = encode_varint(128)
    assert len(out) == 2
    # Continuation bit clear on the terminal byte.
    assert out[-1] & 0x80 == 0


def test_encode_varint_negative_raises() -> None:
    with pytest.raises(ValueError):
        encode_varint(-1)


def test_encode_varint_overflow_raises() -> None:
    with pytest.raises(ValueError):
        encode_varint(_UINT64_MAX + 1)


# -- roundtrip --------------------------------------------------------------


@pytest.mark.parametrize("v", [0, 1, 127, 128, 16384, 2**32 - 1, _UINT64_MAX])
def test_encode_decode_roundtrip(v: int) -> None:
    encoded = encode_varint(v)
    value, consumed = decode_varint(encoded)
    assert value == v
    assert consumed == len(encoded)


# -- decode_varint errors ---------------------------------------------------


def test_decode_varint_empty_raises_framing_error() -> None:
    with pytest.raises(FramingError):
        decode_varint(b"")


def test_decode_varint_truncated_mid_varint() -> None:
    # 0x80 has continuation bit set; no next byte -> truncated.
    with pytest.raises(FramingError):
        decode_varint(b"\x80")


def test_decode_varint_over_long_raises() -> None:
    # 11 bytes all with MSB set -> exceeds 10-byte protobuf cap.
    bad = b"\x80" * 11
    with pytest.raises(FramingError):
        decode_varint(bad)


def test_decode_varint_default_offset_returns_consumed_count() -> None:
    # 300 encodes as two bytes: 0xAC, 0x02.
    encoded = encode_varint(300)
    assert len(encoded) == 2
    value, consumed = decode_varint(encoded)
    assert value == 300
    # `consumed` is a byte count, not an absolute new offset.
    assert consumed == 2


def test_decode_varint_with_offset_reads_count_not_position() -> None:
    # Prepend filler so offset is non-zero; verify return is still a count.
    encoded = b"\xff\xff" + encode_varint(300)
    value, consumed = decode_varint(encoded, offset=2)
    assert value == 300
    assert consumed == 2  # NOT 4 (would be the absolute new offset)


# -- framing ----------------------------------------------------------------


def test_encode_frame_is_alias_of_frame_payload() -> None:
    # The required alias: same callable, not just same output.
    assert encode_frame is frame_payload


def test_frame_payload_shape() -> None:
    assert frame_payload(b"abc") == b"\x03abc"


def test_iter_frames_multi_frame_buffer() -> None:
    buf = encode_frame(b"abc") + encode_frame(b"de")
    assert list(iter_frames(buf)) == [b"abc", b"de"]


def test_iter_frames_truncated_raises() -> None:
    # Declares 5 bytes but only 2 follow.
    buf = encode_varint(5) + b"ab"
    with pytest.raises(FramingError):
        list(iter_frames(buf))


def test_parse_frames_empty_buffer_returns_empty_list() -> None:
    assert parse_frames(b"") == []


def test_parse_frames_matches_iter_frames() -> None:
    buf = encode_frames([b"hello", b"", b"world"])
    assert parse_frames(buf) == [b"hello", b"", b"world"]


def test_encode_frames_concatenates() -> None:
    assert encode_frames([b"a", b"bc"]) == encode_frame(b"a") + encode_frame(b"bc")


# -- FramingError taxonomy --------------------------------------------------


def test_framing_error_is_value_error() -> None:
    # Callers using `pytest.raises(ValueError)` should match FramingError.
    assert issubclass(FramingError, ValueError)
    with pytest.raises(ValueError):
        decode_varint(b"")

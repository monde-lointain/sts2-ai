"""Length-delimited framing tests (S0.C.alpha)."""

from __future__ import annotations

import pytest

from ingest_api.framing import (
    FramingError,
    decode_varint,
    encode_frame,
    encode_frames,
    encode_varint,
    iter_frames,
    parse_frames,
)

# ---------------------------- varint roundtrip ----------------------------


@pytest.mark.parametrize(
    "value,expected_hex",
    [
        (0, "00"),
        (1, "01"),
        (127, "7f"),
        (128, "8001"),
        (150, "9601"),
        (300, "ac02"),
        (16384, "808001"),
        ((1 << 30), "8080808004"),
    ],
)
def test_encode_varint_known_values(value: int, expected_hex: str) -> None:
    assert encode_varint(value).hex() == expected_hex


@pytest.mark.parametrize("value", [0, 1, 127, 128, 300, 65535, 1 << 20, 1 << 50])
def test_varint_roundtrip(value: int) -> None:
    buf = encode_varint(value)
    decoded, consumed = decode_varint(buf, 0)
    assert decoded == value
    assert consumed == len(buf)


def test_encode_varint_rejects_negative() -> None:
    with pytest.raises(ValueError):
        encode_varint(-1)


def test_decode_varint_truncated_raises() -> None:
    # 0x80 means "more bytes follow"; if buffer ends, raise.
    with pytest.raises(FramingError):
        decode_varint(b"\x80", 0)


def test_decode_varint_overlong_raises() -> None:
    # 11 continuation bytes -> exceeds the 10-byte cap.
    buf = b"\x80" * 11
    with pytest.raises(FramingError):
        decode_varint(buf, 0)


# ---------------------------- frame roundtrip ----------------------------


def test_encode_frame_prefix_then_payload() -> None:
    f = encode_frame(b"hello")
    # 0x05 || "hello"
    assert f == b"\x05hello"


def test_encode_frames_concatenates() -> None:
    out = encode_frames([b"a", b"bb", b"ccc"])
    assert out == b"\x01a\x02bb\x03ccc"


def test_parse_frames_three_payloads() -> None:
    buf = encode_frames([b"alpha", b"beta", b"gamma"])
    assert parse_frames(buf) == [b"alpha", b"beta", b"gamma"]


def test_parse_frames_empty_buffer_returns_empty_list() -> None:
    assert parse_frames(b"") == []


def test_parse_frames_single_empty_payload() -> None:
    # Varint zero + zero bytes => valid empty payload.
    assert parse_frames(b"\x00") == [b""]


def test_parse_frames_large_payload_roundtrip() -> None:
    payload = b"\xa5" * 1024
    assert parse_frames(encode_frame(payload)) == [payload]


def test_iter_frames_yields_lazily() -> None:
    buf = encode_frames([b"x" * 10, b"y" * 20, b"z" * 30])
    it = iter_frames(buf)
    assert next(it) == b"x" * 10
    assert next(it) == b"y" * 20
    assert next(it) == b"z" * 30
    with pytest.raises(StopIteration):
        next(it)


def test_parse_frames_truncated_payload_raises() -> None:
    # Declares length 5 but only 3 bytes follow.
    bad = b"\x05" + b"abc"
    with pytest.raises(FramingError):
        parse_frames(bad)


def test_parse_frames_truncated_varint_raises() -> None:
    bad = b"\x80"  # continuation but no follow byte
    with pytest.raises(FramingError):
        parse_frames(bad)

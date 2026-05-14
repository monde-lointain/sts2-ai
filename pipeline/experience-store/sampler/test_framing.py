"""Tests for the length-delimited protobuf framing (S0.C.beta)."""

from __future__ import annotations

import pytest

from proto import (
    CombatOutcomeSample,
    CombatOutcomeSummary,
    DecisionType,
    Trajectory,
    TrajectoryStep,
)
from sampler.framing import (
    decode_varint,
    encode_varint,
    frame_payload,
    frame_steps,
    frame_trailer,
    is_degenerate_combat_sample,
)


# ---------- varint ----------


@pytest.mark.parametrize(
    "value,expected",
    [
        (0, b"\x00"),
        (1, b"\x01"),
        (127, b"\x7f"),
        (128, b"\x80\x01"),
        (300, b"\xac\x02"),
        (0x7FFF, b"\xff\xff\x01"),
        (1 << 32, b"\x80\x80\x80\x80\x10"),
    ],
)
def test_encode_varint_canonical(value: int, expected: bytes) -> None:
    assert encode_varint(value) == expected


def test_encode_varint_negative_raises() -> None:
    with pytest.raises(ValueError):
        encode_varint(-1)


def test_encode_varint_uint64_overflow_raises() -> None:
    with pytest.raises(ValueError):
        encode_varint(1 << 64)


def test_encode_decode_round_trip() -> None:
    for v in [0, 1, 7, 127, 128, 16384, 1 << 32, (1 << 64) - 1]:
        encoded = encode_varint(v)
        val, n = decode_varint(encoded)
        assert val == v
        assert n == len(encoded)


def test_decode_varint_truncated_raises() -> None:
    # 0x80 means "continue" but there's no continuation
    with pytest.raises(ValueError):
        decode_varint(b"\x80")


def test_decode_varint_over_10_bytes_raises() -> None:
    too_long = b"\x80" * 10 + b"\x01"
    with pytest.raises(ValueError):
        decode_varint(too_long)


def test_decode_varint_offset() -> None:
    buf = b"\x99\xac\x02\xff"
    val, n = decode_varint(buf, offset=1)
    assert val == 300
    assert n == 2


# ---------- frame_payload ----------


def test_frame_payload_empty() -> None:
    framed = frame_payload(b"")
    assert framed == b"\x00"
    length, n = decode_varint(framed)
    assert length == 0
    assert n == 1


def test_frame_payload_typical() -> None:
    payload = b"hello world" * 10
    framed = frame_payload(payload)
    length, n = decode_varint(framed)
    assert length == len(payload)
    assert framed[n:] == payload


def test_frame_payload_rejects_non_bytes() -> None:
    with pytest.raises(TypeError):
        frame_payload("not bytes")  # type: ignore[arg-type]


# ---------- frame_steps + frame_trailer ----------


def _make_step(decision_type: int = DecisionType.DECISION_TYPE_CARD_PICK) -> TrajectoryStep:
    s = TrajectoryStep()
    s.decision_type = decision_type
    s.rich_state = b"state-bytes"
    s.legal_action_ids.extend([1, 2, 3])
    s.search_policy.extend([0.5, 0.3, 0.2])
    s.action_taken = 2
    s.reward = 0.0
    s.terminal = False
    return s


def test_frame_steps_round_trip_two_steps() -> None:
    steps = [_make_step(), _make_step(DecisionType.DECISION_TYPE_MAP)]
    body = b"".join(frame_steps(steps))
    # Manually demux back
    parsed: list[TrajectoryStep] = []
    offset = 0
    while offset < len(body):
        length, n = decode_varint(body, offset)
        offset += n
        s = TrajectoryStep()
        s.ParseFromString(body[offset : offset + length])
        parsed.append(s)
        offset += length
    assert len(parsed) == 2
    assert parsed[0].decision_type == DecisionType.DECISION_TYPE_CARD_PICK
    assert parsed[1].decision_type == DecisionType.DECISION_TYPE_MAP


def test_frame_trailer_ok() -> None:
    framed = frame_trailer("ok")
    length, n = decode_varint(framed)
    assert framed[n : n + length] == b'{"status":"ok"}'


def test_frame_trailer_exhausted() -> None:
    framed = frame_trailer("exhausted")
    length, n = decode_varint(framed)
    assert framed[n : n + length] == b'{"status":"exhausted"}'


def test_frame_trailer_rejects_unknown_status() -> None:
    with pytest.raises(ValueError):
        frame_trailer("partial")


# ---------- is_degenerate_combat_sample (Q3-ADR-005) ----------


def _make_degenerate_combat_step(expected_hp_delta: float = -3.5) -> TrajectoryStep:
    step = TrajectoryStep()
    step.decision_type = DecisionType.DECISION_TYPE_COMBAT
    summary = CombatOutcomeSummary()
    summary.expected_hp_delta = expected_hp_delta
    step.combat_outcome_summary.CopyFrom(summary)
    sample = CombatOutcomeSample()
    sample.probability_weight = 1.0
    sample.hp_delta = expected_hp_delta
    step.combat_outcome_samples.append(sample)
    return step


def test_is_degenerate_combat_sample_positive() -> None:
    step = _make_degenerate_combat_step()
    assert is_degenerate_combat_sample(step) is True


def test_is_degenerate_combat_sample_non_combat() -> None:
    step = _make_degenerate_combat_step()
    step.decision_type = DecisionType.DECISION_TYPE_MAP
    assert is_degenerate_combat_sample(step) is False


def test_is_degenerate_combat_sample_two_samples() -> None:
    step = _make_degenerate_combat_step()
    extra = CombatOutcomeSample()
    extra.probability_weight = 0.5
    step.combat_outcome_samples.append(extra)
    assert is_degenerate_combat_sample(step) is False


def test_is_degenerate_combat_sample_weight_not_one() -> None:
    step = _make_degenerate_combat_step()
    step.combat_outcome_samples[0].probability_weight = 0.9
    assert is_degenerate_combat_sample(step) is False


def test_is_degenerate_combat_sample_hp_mismatch() -> None:
    step = _make_degenerate_combat_step(expected_hp_delta=-3.5)
    step.combat_outcome_samples[0].hp_delta = -2.0  # divergent
    assert is_degenerate_combat_sample(step) is False


def test_is_degenerate_combat_sample_zero_samples() -> None:
    step = TrajectoryStep()
    step.decision_type = DecisionType.DECISION_TYPE_COMBAT
    # no combat_outcome_samples
    assert is_degenerate_combat_sample(step) is False

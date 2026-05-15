"""Tests for SchemaVersion value class."""
import dataclasses
import pytest
from schema_registry.versions import SchemaVersion, PHASE1, PHASE1_1


def test_phase1_constant():
    assert PHASE1.major == 1 and PHASE1.minor == 0
    assert PHASE1.as_tuple() == (1, 0)

def test_as_fields_round_trip():
    fields = PHASE1.as_fields()
    assert fields == {"major": 1, "minor": 0}
    assert SchemaVersion.from_fields(fields) == PHASE1

def test_equality_and_hash():
    a = SchemaVersion(1, 0); b = SchemaVersion(1, 0); c = SchemaVersion(1, 1)
    assert a == b and a != c and hash(a) == hash(b)
    assert {a, b, c} == {a, c}

def test_immutable():
    v = SchemaVersion(1, 0)
    with pytest.raises(dataclasses.FrozenInstanceError):
        v.major = 2  # type: ignore[misc]

def test_str():
    assert str(PHASE1) == "1.0"

def test_from_tuple():
    assert SchemaVersion.from_tuple((1, 0)) == PHASE1

def test_rejects_negative():
    with pytest.raises(ValueError):
        SchemaVersion(-1, 0)

def test_rejects_non_int():
    with pytest.raises(TypeError):
        SchemaVersion(1.0, 0)  # type: ignore[arg-type]


# --- PHASE1_1 (v1.1, ADR-019 additive bump) ---

def test_phase1_1_constant():
    assert PHASE1_1.major == 1 and PHASE1_1.minor == 1
    assert PHASE1_1.as_tuple() == (1, 1)

def test_phase1_1_as_fields_round_trip():
    fields = PHASE1_1.as_fields()
    assert fields == {"major": 1, "minor": 1}
    assert SchemaVersion.from_fields(fields) == PHASE1_1

def test_phase1_1_str():
    assert str(PHASE1_1) == "1.1"

def test_phase1_1_from_tuple():
    assert SchemaVersion.from_tuple((1, 1)) == PHASE1_1

def test_phase1_distinct_from_phase1_1():
    assert PHASE1 != PHASE1_1
    assert hash(PHASE1) != hash(PHASE1_1)

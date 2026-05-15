"""Unit tests for ``pipeline.trainer.manifest`` (S0.D.β).

Schema-v1 invariants:

1. Construct a manifest → ``to_dict()`` returns every required field with
   the correct runtime type.
2. ``validate(d)`` accepts a valid dict.
3. Missing field → ``ValueError`` naming the missing key.
4. Wrong type → ``ValueError`` naming the field.
5. Round-trip ``to_dict`` → ``from_dict`` preserves values.
"""
from __future__ import annotations

import pytest

from pipeline.trainer.manifest import (
    ProvenanceManifest,
    SCHEMA_VERSION,
    validate,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------
def _valid_payload() -> dict:
    return {
        "schema_version": SCHEMA_VERSION,
        "artifact_id": "abc123def456",
        "code_sha": "deadbeefcafe",
        "code_dirty": False,
        "dataset_sha": "0" * 64,
        "dataset_size": 0,
        "seed": 42,
        "hyperparameters": {"lr": 0.0003, "batch_size": 32},
        "parent_artifact_id": None,
        "content_registry_sha": "1" * 64,
        "onnx_opset_version": 17,
        "phase": 1,
        "step": 100,
        "created_at_ns": 1_715_692_800_000_000_000,
        "host": "trainer-host-01",
        "run_id": "01HXYZ1234567890ABCDEFGHJK",
    }


# ---------------------------------------------------------------------------
# 1. Manifest construction + to_dict produces all required fields
# ---------------------------------------------------------------------------
def test_to_dict_emits_all_required_fields() -> None:
    p = _valid_payload()
    m = ProvenanceManifest(**p)
    d = m.to_dict()
    # Every key from the payload appears in to_dict output.
    for key in p:
        assert key in d, f"to_dict missing {key}"
    # Types check out.
    assert isinstance(d["schema_version"], int)
    assert isinstance(d["artifact_id"], str)
    assert isinstance(d["code_dirty"], bool)
    assert isinstance(d["dataset_size"], int)
    assert isinstance(d["hyperparameters"], dict)
    assert d["parent_artifact_id"] is None  # nullable case
    assert d["onnx_opset_version"] == 17
    assert d["phase"] == 1


def test_to_dict_hyperparameters_is_copied() -> None:
    """``to_dict`` must return a fresh dict; mutation of result doesn't leak."""
    m = ProvenanceManifest(**_valid_payload())
    d = m.to_dict()
    d["hyperparameters"]["lr"] = 0.999
    # The frozen manifest's mapping is untouched.
    assert m.hyperparameters["lr"] == 0.0003


# ---------------------------------------------------------------------------
# 2. validate accepts a well-formed payload
# ---------------------------------------------------------------------------
def test_validate_accepts_valid_payload() -> None:
    validate(_valid_payload())  # no raise


def test_validate_accepts_string_parent_artifact_id() -> None:
    p = _valid_payload()
    p["parent_artifact_id"] = "parent-abc"
    validate(p)


# ---------------------------------------------------------------------------
# 3. Missing field → ValueError naming the missing key
# ---------------------------------------------------------------------------
@pytest.mark.parametrize(
    "field",
    [
        "schema_version",
        "artifact_id",
        "code_sha",
        "code_dirty",
        "dataset_sha",
        "dataset_size",
        "seed",
        "hyperparameters",
        "parent_artifact_id",
        "content_registry_sha",
        "onnx_opset_version",
        "phase",
        "step",
        "created_at_ns",
        "host",
        "run_id",
    ],
)
def test_validate_rejects_missing_field(field: str) -> None:
    p = _valid_payload()
    p.pop(field)
    with pytest.raises(ValueError, match=field):
        validate(p)


# ---------------------------------------------------------------------------
# 4. Wrong type → ValueError naming the field
# ---------------------------------------------------------------------------
def test_validate_rejects_wrong_type_for_int_field() -> None:
    p = _valid_payload()
    p["step"] = "100"  # str, not int
    with pytest.raises(ValueError, match="step"):
        validate(p)


def test_validate_rejects_bool_for_int_field() -> None:
    """``True`` is an int in Python; must still be rejected for ``step``."""
    p = _valid_payload()
    p["step"] = True
    with pytest.raises(ValueError, match="step"):
        validate(p)


def test_validate_rejects_wrong_type_for_str_field() -> None:
    p = _valid_payload()
    p["host"] = 1234
    with pytest.raises(ValueError, match="host"):
        validate(p)


def test_validate_rejects_wrong_type_for_dict_field() -> None:
    p = _valid_payload()
    p["hyperparameters"] = "lr=0.3"
    with pytest.raises(ValueError, match="hyperparameters"):
        validate(p)


def test_validate_rejects_wrong_schema_version() -> None:
    p = _valid_payload()
    p["schema_version"] = 2
    with pytest.raises(ValueError, match="schema_version"):
        validate(p)


def test_validate_rejects_wrong_onnx_opset() -> None:
    p = _valid_payload()
    p["onnx_opset_version"] = 13
    with pytest.raises(ValueError, match="onnx_opset_version"):
        validate(p)


def test_validate_rejects_wrong_phase() -> None:
    p = _valid_payload()
    p["phase"] = 2
    with pytest.raises(ValueError, match="phase"):
        validate(p)


# ---------------------------------------------------------------------------
# 5. Round-trip to_dict <-> from_dict preserves values
# ---------------------------------------------------------------------------
def test_round_trip_preserves_values() -> None:
    original = ProvenanceManifest(**_valid_payload())
    payload = original.to_dict()
    reloaded = ProvenanceManifest.from_dict(payload)
    assert reloaded.schema_version == original.schema_version
    assert reloaded.artifact_id == original.artifact_id
    assert reloaded.code_sha == original.code_sha
    assert reloaded.code_dirty == original.code_dirty
    assert reloaded.dataset_sha == original.dataset_sha
    assert reloaded.dataset_size == original.dataset_size
    assert reloaded.seed == original.seed
    assert dict(reloaded.hyperparameters) == dict(original.hyperparameters)
    assert reloaded.parent_artifact_id == original.parent_artifact_id
    assert reloaded.content_registry_sha == original.content_registry_sha
    assert reloaded.onnx_opset_version == original.onnx_opset_version
    assert reloaded.phase == original.phase
    assert reloaded.step == original.step
    assert reloaded.created_at_ns == original.created_at_ns
    assert reloaded.host == original.host
    assert reloaded.run_id == original.run_id

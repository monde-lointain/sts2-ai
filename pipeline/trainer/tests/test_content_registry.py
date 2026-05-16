"""Tests for ``pipeline.trainer.content_registry`` (S0.B.γ)."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

import pytest

from pipeline.trainer.content_registry import ContentRegistry

_REGISTRY_PATH = (
    Path(__file__).resolve().parents[3] / "contracts" / "registry" / "phase1-silent.json"
)


# ---------------------------------------------------------------------------
# Happy path
# ---------------------------------------------------------------------------
def test_load_returns_frozen_registry_with_valid_hash() -> None:
    reg = ContentRegistry.load(_REGISTRY_PATH)
    # SHA-256 hex is 64 chars
    assert isinstance(reg.content_hash, str) and len(reg.content_hash) == 64
    # bytes_blob equals the raw source bytes
    expected_bytes = _REGISTRY_PATH.read_bytes()
    assert reg.bytes_blob == expected_bytes
    # hash is over raw bytes (no re-serialization)
    assert reg.content_hash == hashlib.sha256(expected_bytes).hexdigest()
    # frozen (post-construction mutation rejected)
    with pytest.raises((AttributeError, Exception)):
        reg.content_hash = "tampered"  # type: ignore[misc]


def test_token_lookups_work_and_raise_on_miss() -> None:
    reg = ContentRegistry.load(_REGISTRY_PATH)
    # known: [CLS] is token_id 1 in the phase1-silent fixture
    cls_tok = reg.get_token_by_id(1)
    assert cls_tok.name == "[CLS]"
    cls_by_name = reg.get_token_by_name("[CLS]")
    assert cls_by_name.token_id == 1
    # miss: KeyError
    with pytest.raises(KeyError):
        reg.get_token_by_id(99999999)
    with pytest.raises(KeyError):
        reg.get_token_by_name("not-a-real-token")


def test_len_returns_token_count() -> None:
    reg = ContentRegistry.load(_REGISTRY_PATH)
    assert len(reg) == len(reg.tokens) > 0


def test_kind_inferred_from_token_prefix() -> None:
    reg = ContentRegistry.load(_REGISTRY_PATH)
    # walk and verify any token with a colon prefix has matching kind
    for tok in reg.tokens:
        if ":" in tok.token:
            prefix = tok.token.split(":", 1)[0]
            assert tok.kind == prefix, f"{tok.token} kind={tok.kind} prefix={prefix}"


# ---------------------------------------------------------------------------
# expected_token_count
# ---------------------------------------------------------------------------
def test_expected_token_count_match_ok() -> None:
    raw = json.loads(_REGISTRY_PATH.read_bytes())
    n = len(raw["tokens"])
    reg = ContentRegistry.load(_REGISTRY_PATH, expected_token_count=n)
    assert len(reg) == n


def test_expected_token_count_mismatch_raises() -> None:
    with pytest.raises(ValueError) as exc:
        ContentRegistry.load(_REGISTRY_PATH, expected_token_count=2)
    assert "token count mismatch" in str(exc.value).lower()


# ---------------------------------------------------------------------------
# Required keys
# ---------------------------------------------------------------------------
def test_missing_required_top_level_key_raises(tmp_path: Path) -> None:
    bad = tmp_path / "bad.json"
    bad.write_text(json.dumps({"manifest": {}, "tokens": []}))  # card_dsl missing
    with pytest.raises(ValueError) as exc:
        ContentRegistry.load(bad)
    assert "card_dsl" in str(exc.value)


def test_manifest_missing_version_raises(tmp_path: Path) -> None:
    bad = tmp_path / "no-version.json"
    bad.write_text(
        json.dumps(
            {
                "manifest": {"schema_version": {"major": 0, "minor": 0}},
                "tokens": [],
                "card_dsl": [],
            }
        )
    )
    with pytest.raises(ValueError):
        ContentRegistry.load(bad)


def test_manifest_info_parsed() -> None:
    reg = ContentRegistry.load(_REGISTRY_PATH)
    assert isinstance(reg.manifest.version, str) and reg.manifest.version
    assert reg.manifest.schema_version_major >= 0
    assert reg.manifest.schema_version_minor >= 0

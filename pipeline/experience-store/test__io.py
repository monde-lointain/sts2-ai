"""Unit tests for ``atomic_write_json`` (Stream A.1-alpha).

The module under test lives at ``pipeline/experience-store/_io.py``.
Its filename collides with the CPython builtin ``_io`` module, so we
load it by explicit file path via ``importlib`` to bypass normal
import resolution.
"""

from __future__ import annotations

import importlib.util
import json
import os
import sys
from pathlib import Path

import pytest


def _load_io_module():
    spec = importlib.util.spec_from_file_location(
        "experience_store_io", Path(__file__).parent / "_io.py"
    )
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    sys.modules["experience_store_io"] = mod
    spec.loader.exec_module(mod)
    return mod


_io_mod = _load_io_module()
atomic_write_json = _io_mod.atomic_write_json


def test_writes_correct_json_roundtrips(tmp_path: Path) -> None:
    target = tmp_path / "out.json"
    payload = {"a": 1, "b": [1, 2, 3], "c": {"nested": True}}

    atomic_write_json(target, payload)

    with target.open("r", encoding="utf-8") as fh:
        assert json.load(fh) == payload


def test_existing_file_replaced_no_stale_content(tmp_path: Path) -> None:
    target = tmp_path / "out.json"
    target.write_text(json.dumps({"stale": "value", "keep_me": 999}) + "\n", encoding="utf-8")

    atomic_write_json(target, {"fresh": True})

    with target.open("r", encoding="utf-8") as fh:
        assert json.load(fh) == {"fresh": True}


def test_fsync_true_calls_os_fsync(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    calls: list[int] = []
    real_fsync = os.fsync

    def spy(fd: int) -> None:
        calls.append(fd)
        real_fsync(fd)

    monkeypatch.setattr(_io_mod.os, "fsync", spy)

    atomic_write_json(tmp_path / "out.json", {"x": 1}, fsync=True)

    assert len(calls) == 1


def test_fsync_false_does_not_call_os_fsync(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    calls: list[int] = []

    def spy(fd: int) -> None:
        calls.append(fd)

    monkeypatch.setattr(_io_mod.os, "fsync", spy)

    atomic_write_json(tmp_path / "out.json", {"x": 1}, fsync=False)

    assert calls == []


def test_encode_failure_cleans_up_tmp(tmp_path: Path) -> None:
    target = tmp_path / "out.json"
    pre_existing = tmp_path / "other.json"
    pre_existing.write_text("{}\n", encoding="utf-8")

    with pytest.raises(TypeError):
        atomic_write_json(target, {"x": object()})

    leftovers = sorted(p.name for p in tmp_path.iterdir())
    assert leftovers == ["other.json"]
    assert not any(p.name.endswith(".tmp") for p in tmp_path.iterdir())


def test_indent_none_produces_compact_json(tmp_path: Path) -> None:
    target = tmp_path / "out.json"
    payload = {"a": 1, "b": 2}

    atomic_write_json(target, payload, indent=None)

    raw = target.read_bytes()
    # Single-line JSON: exactly one trailing newline, no embedded newlines.
    assert raw.count(b"\n") == 1
    assert raw.endswith(b"\n")
    # Matches what stdlib produces with indent=None (i.e. no pretty-print).
    expected = json.dumps(payload, indent=None, sort_keys=True) + "\n"
    assert raw.decode("utf-8") == expected
    assert json.loads(raw) == payload

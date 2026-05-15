"""Unit tests for pipeline.trainer.run_config.

Covers items 1-5 of the run-config.md Testing Strategy:
1. Missing required key raises ValueError naming the key.
2. Code-SHA captured cleanly on a clean tree.
3. Dirty-tree behavior is configurable.
4. seed_everything reproducibility (random/numpy/torch).
5. Frozen dataclass refuses mutation.
"""
from __future__ import annotations

import dataclasses
import json
import os
import random
import subprocess
from pathlib import Path

import pytest

from pipeline.trainer.run_config import (
    RunConfig,
    RunProvenance,
    seed_everything,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------
def _minimal_config() -> dict:
    """A complete, valid config payload."""
    return {
        "service": "trainer",
        "port": 18110,
        "data_dir": "data/trainer",
        "q3_url": "http://127.0.0.1:18103",
        "q5_url": "http://127.0.0.1:18105",
        "parent_artifact_id": None,
        "seed": 0,
        "refuse_on_dirty": False,
        "network": {
            "expected_token_count": None,
            "d_model": 128,
            "n_layers": 6,
            "n_heads": 4,
            "ffn_dim": 512,
            "max_seq_len": 256,
        },
        "optim": {
            "lr": 0.0003,
            "weight_decay": 0.01,
            "warmup_steps": 1000,
            "total_steps": 100000,
            "grad_clip": 1.0,
        },
        "loss_weights": {
            "policy": 1.0,
            "combat_sample": 1.0,
            "combat_summary": 1.0,
            "hp_frac_aux": 0.05,
            "kl_beta": 0.01,
        },
        "checkpoint": {"every_n_steps": 1000, "every_m_minutes": 5},
        "wandb_enabled": False,
        "sampling_mode": "uniform",
        "prefetch_queue_size": 4,
        "batch_size": 32,
    }


@pytest.fixture
def config_path(tmp_path: Path) -> Path:
    """Path to a JSON file holding a minimal valid config."""
    path = tmp_path / "local.json"
    path.write_text(json.dumps(_minimal_config()))
    return path


@pytest.fixture
def isolated_repo(tmp_path: Path, monkeypatch):
    """Initialize a tiny clean git repo and chdir into it.

    Ensures git-based provenance tests are independent of the host worktree's
    dirty state.
    """
    repo = tmp_path / "repo"
    repo.mkdir()
    monkeypatch.chdir(repo)

    def git(*args: str) -> subprocess.CompletedProcess:
        return subprocess.run(
            ["git", *args], cwd=repo, capture_output=True, text=True, check=True
        )

    git("init", "-q")
    git("config", "user.email", "test@example.com")
    git("config", "user.name", "test")
    git("config", "commit.gpgsign", "false")
    (repo / "seed.txt").write_text("hi\n")
    git("add", "seed.txt")
    git("commit", "-qm", "init")
    return repo


# ---------------------------------------------------------------------------
# 1. Missing key
# ---------------------------------------------------------------------------
def test_missing_required_key_raises(tmp_path: Path) -> None:
    raw = _minimal_config()
    del raw["q3_url"]
    path = tmp_path / "local.json"
    path.write_text(json.dumps(raw))
    with pytest.raises(ValueError) as exc:
        RunConfig.load(path)
    assert "q3_url" in str(exc.value)


def test_missing_nested_key_raises(tmp_path: Path) -> None:
    raw = _minimal_config()
    del raw["network"]["d_model"]
    path = tmp_path / "local.json"
    path.write_text(json.dumps(raw))
    with pytest.raises(ValueError) as exc:
        RunConfig.load(path)
    assert "d_model" in str(exc.value)


# ---------------------------------------------------------------------------
# 2. Clean tree captures code_sha
# ---------------------------------------------------------------------------
def test_clean_tree_captures_code_sha(
    isolated_repo: Path, config_path: Path
) -> None:
    cfg = RunConfig.load(config_path)
    prov = RunProvenance.capture(cfg, parent_artifact_id=None)
    expected = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=isolated_repo,
        capture_output=True,
        text=True,
        check=True,
    ).stdout.strip()
    assert prov.code_sha == expected
    assert prov.run_dirty is False
    assert prov.run_id == cfg.run_id
    assert len(cfg.run_id) == 26


# ---------------------------------------------------------------------------
# 3. Dirty tree behavior is configurable
# ---------------------------------------------------------------------------
def test_dirty_tree_refuse_raises(
    isolated_repo: Path, tmp_path: Path
) -> None:
    # Make tree dirty
    (isolated_repo / "scratch.txt").write_text("dirty\n")

    raw = _minimal_config()
    raw["refuse_on_dirty"] = True
    path = tmp_path / "local.json"
    path.write_text(json.dumps(raw))
    cfg = RunConfig.load(path)
    with pytest.raises(RuntimeError):
        RunProvenance.capture(cfg, parent_artifact_id=None)


def test_dirty_tree_warn_proceeds(
    isolated_repo: Path, tmp_path: Path
) -> None:
    (isolated_repo / "scratch.txt").write_text("dirty\n")

    raw = _minimal_config()
    raw["refuse_on_dirty"] = False
    path = tmp_path / "local.json"
    path.write_text(json.dumps(raw))
    cfg = RunConfig.load(path)
    prov = RunProvenance.capture(cfg, parent_artifact_id=None)
    assert prov.run_dirty is True
    assert prov.code_sha  # still captured


# ---------------------------------------------------------------------------
# 4. seed_everything reproducibility
# ---------------------------------------------------------------------------
def test_seed_everything_reproducible() -> None:
    seed_everything(1234)
    py_a = [random.random() for _ in range(100)]

    import numpy as np
    np_a = np.random.randn(100).tolist()

    import torch
    torch_a = torch.randn(100).tolist()

    seed_everything(1234)
    py_b = [random.random() for _ in range(100)]
    np_b = np.random.randn(100).tolist()
    torch_b = torch.randn(100).tolist()

    assert py_a == py_b
    assert np_a == np_b
    assert torch_a == torch_b


def test_seed_everything_sets_hashseed() -> None:
    seed_everything(7)
    assert os.environ["PYTHONHASHSEED"] == "7"


# ---------------------------------------------------------------------------
# 5. Frozen dataclass refuses mutation
# ---------------------------------------------------------------------------
def test_run_config_frozen(config_path: Path) -> None:
    cfg = RunConfig.load(config_path)
    with pytest.raises(dataclasses.FrozenInstanceError):
        cfg.q3_url = "http://other"  # type: ignore[misc]
    with pytest.raises(dataclasses.FrozenInstanceError):
        cfg.network.d_model = 999  # type: ignore[misc]


def test_run_provenance_frozen(
    isolated_repo: Path, config_path: Path
) -> None:
    cfg = RunConfig.load(config_path)
    prov = RunProvenance.capture(cfg, parent_artifact_id=None)
    with pytest.raises(dataclasses.FrozenInstanceError):
        prov.code_sha = "deadbeef"  # type: ignore[misc]


# ---------------------------------------------------------------------------
# Misc invariants
# ---------------------------------------------------------------------------
def test_run_id_stable_per_instance(config_path: Path) -> None:
    cfg = RunConfig.load(config_path)
    # Same instance always yields same run_id.
    assert cfg.run_id == cfg.run_id
    # Distinct loads mint distinct ULIDs.
    cfg2 = RunConfig.load(config_path)
    assert cfg.run_id != cfg2.run_id


def test_run_id_is_crockford(config_path: Path) -> None:
    cfg = RunConfig.load(config_path)
    alphabet = set("0123456789ABCDEFGHJKMNPQRSTVWXYZ")
    assert len(cfg.run_id) == 26
    assert set(cfg.run_id) <= alphabet

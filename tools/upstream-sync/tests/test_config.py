"""Tests for upstream_sync.config: resolution precedence + monorepo-root detection."""

from __future__ import annotations

from argparse import Namespace
from pathlib import Path

import pytest

from upstream_sync import config


@pytest.fixture
def clean_env(monkeypatch):
    """Strip relevant env vars so default-precedence is exercisable."""
    for key in ("STEAM_HOME", "GDRE_BIN", "UPSTREAM_TREE"):
        monkeypatch.delenv(key, raising=False)


def test_defaults_when_no_overrides(clean_env):
    cfg = config.resolve_config(args=None)
    assert cfg.steam_home == config.DEFAULT_STEAM_HOME.resolve()
    assert cfg.gdre_bin == config.DEFAULT_GDRE_BIN.resolve()
    assert cfg.upstream_tree == config.DEFAULT_UPSTREAM_TREE.resolve()
    assert cfg.app_id == "2868840"


def test_env_var_override(monkeypatch, tmp_path):
    fake_steam = tmp_path / "steam"
    fake_steam.mkdir()
    monkeypatch.setenv("STEAM_HOME", str(fake_steam))
    cfg = config.resolve_config(args=None)
    assert cfg.steam_home == fake_steam.resolve()


def test_flag_beats_env(monkeypatch, tmp_path):
    """Flag takes precedence over env var."""
    flag_dir = tmp_path / "flag_steam"
    flag_dir.mkdir()
    env_dir = tmp_path / "env_steam"
    env_dir.mkdir()
    monkeypatch.setenv("STEAM_HOME", str(env_dir))
    args = Namespace(steam_home=str(flag_dir), gdre_bin=None, upstream_tree=None)
    cfg = config.resolve_config(args=args)
    assert cfg.steam_home == flag_dir.resolve()


def test_tilde_expansion(clean_env, monkeypatch):
    monkeypatch.setenv("UPSTREAM_TREE", "~/somewhere")
    cfg = config.resolve_config(args=None)
    assert str(cfg.upstream_tree).startswith(str(Path.home()))


def test_appmanifest_path_uses_app_id(clean_env, tmp_path):
    args = Namespace(steam_home=str(tmp_path), gdre_bin=None, upstream_tree=None)
    cfg = config.resolve_config(args=args)
    assert cfg.appmanifest_path == tmp_path.resolve() / "steamapps" / "appmanifest_2868840.acf"


def test_monorepo_root_detected(clean_env):
    cfg = config.resolve_config(args=None)
    # Confirm it found *some* git root and that it's an ancestor of this test file
    assert (cfg.monorepo_root / ".git").exists()
    assert Path(__file__).resolve().is_relative_to(cfg.monorepo_root)


def test_args_with_none_falls_through(clean_env, monkeypatch, tmp_path):
    """A Namespace whose attrs are None must fall through to env/default, not crash."""
    monkeypatch.setenv("GDRE_BIN", str(tmp_path / "gdre"))
    args = Namespace(steam_home=None, gdre_bin=None, upstream_tree=None)
    cfg = config.resolve_config(args=args)
    assert cfg.gdre_bin == (tmp_path / "gdre").resolve()


def test_config_is_frozen():
    """Config must be immutable so callers can pass it around safely."""
    cfg = config.resolve_config(args=None)
    with pytest.raises(Exception):  # FrozenInstanceError, but be liberal
        cfg.steam_home = Path("/tmp")  # type: ignore[misc]

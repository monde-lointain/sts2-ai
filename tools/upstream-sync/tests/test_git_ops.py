"""Tests for upstream_sync.git_ops: bootstrap + tagging against the upstream tree.

These are *integration* tests: each uses pytest's tmp_path to create a real
on-disk directory, runs real git in it, and exercises the module end-to-end.
The git_ops module operates on the upstream tree (NOT the sts2-ai monorepo).
"""

from __future__ import annotations

import re
import subprocess
from pathlib import Path

import pytest

from upstream_sync import git_ops

# --------------------------------------------------------------------------- #
# Helpers                                                                     #
# --------------------------------------------------------------------------- #


SHA_RE = re.compile(r"^[0-9a-f]{40}$")


def _git(tree: Path, *args: str) -> str:
    """Run git in `tree`, return stdout stripped. Raises CalledProcessError on fail."""
    result = subprocess.run(
        ["git", *args],
        cwd=tree,
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def _seed_upstream_tree(tree: Path) -> None:
    """Populate `tree` with a minimal STS2-shaped layout: src/, scenes/, a .csproj."""
    (tree / "src").mkdir()
    (tree / "src" / "Card.cs").write_text("public class Card {}\n")
    (tree / "scenes").mkdir()
    (tree / "scenes" / "Main.tscn").write_text("[gd_scene]\n")
    (tree / "Game.csproj").write_text("<Project/>\n")
    (tree / "project.godot").write_text("config_version=5\n")


# --------------------------------------------------------------------------- #
# bootstrap                                                                   #
# --------------------------------------------------------------------------- #


class TestBootstrap:
    def test_empty_dir_creates_git_writes_gitignore_commits_tags(self, tmp_path):
        tree = tmp_path / "upstream"
        tree.mkdir()
        _seed_upstream_tree(tree)

        sha = git_ops.bootstrap(
            tree=tree,
            version="v0.103.2",
            buildid="123456",
            gdre_version="2.5.0-beta.5",
        )

        # Return value is a valid 40-char hex SHA.
        assert SHA_RE.match(sha), f"expected SHA, got {sha!r}"

        # .git/ now exists.
        assert (tree / ".git").is_dir()

        # .gitignore was written with the allowlist content.
        gitignore = (tree / ".gitignore").read_text()
        assert gitignore == git_ops.ALLOWLIST_GITIGNORE
        # Sanity: it really is the allowlist (deny-all, then re-add /src/ etc.)
        assert "/*\n" in gitignore
        assert "!/src/" in gitignore
        assert "!/scenes/" in gitignore

        # Tag was created.
        tags = _git(tree, "tag", "--list").splitlines()
        assert tags == ["v0.103.2"]

        # HEAD matches returned SHA.
        head = _git(tree, "rev-parse", "HEAD")
        assert head == sha

        # Commit message contains version, GDRE version, and buildid.
        msg = _git(tree, "log", "-1", "--pretty=%B")
        assert "v0.103.2" in msg
        assert "2.5.0-beta.5" in msg
        assert "123456" in msg

        # Author was set via -c flags (not global config).
        author = _git(tree, "log", "-1", "--pretty=%ae")
        assert author == "upstream-sync@local"
        name = _git(tree, "log", "-1", "--pretty=%an")
        assert name == "upstream-sync"

    def test_idempotent_when_already_initialized(self, tmp_path):
        """Second bootstrap on an already-init'd tree must return current HEAD, no new commit."""
        tree = tmp_path / "upstream"
        tree.mkdir()
        _seed_upstream_tree(tree)

        sha1 = git_ops.bootstrap(
            tree=tree, version="v0.103.2", buildid="123456", gdre_version="2.5.0"
        )

        # Second call with different args MUST NOT create another commit / tag.
        sha2 = git_ops.bootstrap(
            tree=tree, version="v0.999.9", buildid="999999", gdre_version="9.9.9"
        )

        assert sha1 == sha2
        # Tag list unchanged (only the original tag).
        tags = _git(tree, "tag", "--list").splitlines()
        assert tags == ["v0.103.2"]
        # Commit count is still 1.
        commits = _git(tree, "rev-list", "--count", "HEAD")
        assert commits == "1"

    def test_propagates_runtime_error_on_git_failure(self, tmp_path, monkeypatch):
        """If a git subprocess fails, bootstrap raises RuntimeError."""
        tree = tmp_path / "upstream"
        tree.mkdir()
        _seed_upstream_tree(tree)

        real_run = subprocess.run

        def flaky_run(cmd, *args, **kwargs):
            # Sabotage `git init` so we exercise the error path.
            if isinstance(cmd, list) and len(cmd) >= 2 and cmd[0] == "git" and cmd[1] == "init":
                raise subprocess.CalledProcessError(
                    returncode=1, cmd=cmd, output="", stderr="fatal: simulated init failure"
                )
            return real_run(cmd, *args, **kwargs)

        monkeypatch.setattr(git_ops.subprocess, "run", flaky_run)

        with pytest.raises(RuntimeError) as excinfo:
            git_ops.bootstrap(tree=tree, version="v0.1", buildid="1", gdre_version="0")
        # The error message should reference git.
        assert "git" in str(excinfo.value).lower()


# --------------------------------------------------------------------------- #
# get_head_sha                                                                #
# --------------------------------------------------------------------------- #


class TestGetHeadSha:
    def test_returns_valid_sha_after_bootstrap(self, tmp_path):
        tree = tmp_path / "upstream"
        tree.mkdir()
        _seed_upstream_tree(tree)
        bootstrap_sha = git_ops.bootstrap(tree=tree, version="v0.1", buildid="1", gdre_version="0")

        head = git_ops.get_head_sha(tree)
        assert head == bootstrap_sha
        assert SHA_RE.match(head)

    def test_raises_on_non_repo(self, tmp_path):
        not_a_repo = tmp_path / "blank"
        not_a_repo.mkdir()
        with pytest.raises(RuntimeError):
            git_ops.get_head_sha(not_a_repo)


# --------------------------------------------------------------------------- #
# list_tags                                                                   #
# --------------------------------------------------------------------------- #


class TestListTags:
    def test_returns_single_tag_after_bootstrap(self, tmp_path):
        tree = tmp_path / "upstream"
        tree.mkdir()
        _seed_upstream_tree(tree)
        git_ops.bootstrap(tree=tree, version="v0.103.2", buildid="1", gdre_version="0")

        assert git_ops.list_tags(tree) == ["v0.103.2"]

    def test_returns_empty_list_when_no_tags(self, tmp_path):
        tree = tmp_path / "upstream"
        tree.mkdir()
        _git(tree, "init", "-q")
        # No commits, no tags.
        assert git_ops.list_tags(tree) == []


# --------------------------------------------------------------------------- #
# assert_clean                                                                #
# --------------------------------------------------------------------------- #


class TestAssertClean:
    def test_passes_on_clean_tree(self, tmp_path):
        tree = tmp_path / "upstream"
        tree.mkdir()
        _seed_upstream_tree(tree)
        git_ops.bootstrap(tree=tree, version="v0.1", buildid="1", gdre_version="0")
        # No exception.
        git_ops.assert_clean(tree)

    def test_raises_with_offending_paths_on_modified_file(self, tmp_path):
        tree = tmp_path / "upstream"
        tree.mkdir()
        _seed_upstream_tree(tree)
        git_ops.bootstrap(tree=tree, version="v0.1", buildid="1", gdre_version="0")

        # Dirty: modify a tracked file.
        (tree / "src" / "Card.cs").write_text("public class Card { /* dirty */ }\n")

        with pytest.raises(RuntimeError) as excinfo:
            git_ops.assert_clean(tree)
        assert "Card.cs" in str(excinfo.value)

    def test_raises_with_offending_paths_on_untracked_file(self, tmp_path):
        tree = tmp_path / "upstream"
        tree.mkdir()
        _seed_upstream_tree(tree)
        git_ops.bootstrap(tree=tree, version="v0.1", buildid="1", gdre_version="0")

        # Untracked allowlisted file (so it's not ignored).
        (tree / "src" / "Newcomer.cs").write_text("public class Newcomer {}\n")

        with pytest.raises(RuntimeError) as excinfo:
            git_ops.assert_clean(tree)
        assert "Newcomer.cs" in str(excinfo.value)


# --------------------------------------------------------------------------- #
# commit_and_tag                                                              #
# --------------------------------------------------------------------------- #


class TestCommitAndTag:
    def _bootstrapped(self, tmp_path: Path, version: str = "v0.103.2") -> Path:
        tree = tmp_path / "upstream"
        tree.mkdir()
        _seed_upstream_tree(tree)
        git_ops.bootstrap(tree=tree, version=version, buildid="1000", gdre_version="2.5.0")
        return tree

    def test_normal_path_stages_commits_tags(self, tmp_path):
        tree = self._bootstrapped(tmp_path)
        # Simulate a new extract: replace a card file + add a new one.
        (tree / "src" / "Card.cs").write_text("public class Card { /* v2 */ }\n")
        (tree / "src" / "NewCard.cs").write_text("public class NewCard {}\n")

        new_sha = git_ops.commit_and_tag(
            tree=tree,
            version="v0.104.0",
            buildid="2000",
            prior_buildid="1000",
        )

        assert SHA_RE.match(new_sha)
        # Tag exists.
        tags = _git(tree, "tag", "--list").splitlines()
        assert "v0.104.0" in tags
        # New commit on top.
        commits = _git(tree, "rev-list", "--count", "HEAD")
        assert commits == "2"
        # Commit message + version.
        msg = _git(tree, "log", "-1", "--pretty=%B")
        assert "v0.104.0" in msg
        assert "2000" in msg

    def test_refuses_when_tag_exists(self, tmp_path):
        tree = self._bootstrapped(tmp_path, version="v0.103.2")
        # Make a change so there'd actually be something to commit.
        (tree / "src" / "Card.cs").write_text("changed\n")

        with pytest.raises(RuntimeError) as excinfo:
            git_ops.commit_and_tag(
                tree=tree,
                version="v0.103.2",  # already exists from bootstrap
                buildid="2000",
                prior_buildid="1000",
            )
        assert "v0.103.2" in str(excinfo.value)

    def test_refuses_when_buildid_goes_backward(self, tmp_path):
        tree = self._bootstrapped(tmp_path)
        (tree / "src" / "Card.cs").write_text("changed\n")

        with pytest.raises(RuntimeError) as excinfo:
            git_ops.commit_and_tag(
                tree=tree,
                version="v0.104.0",
                buildid="999",  # less than prior 1000
                prior_buildid="1000",
            )
        msg = str(excinfo.value).lower()
        assert "buildid" in msg or "backward" in msg

    def test_accepts_when_buildid_increases(self, tmp_path):
        tree = self._bootstrapped(tmp_path)
        (tree / "src" / "Card.cs").write_text("changed\n")

        new_sha = git_ops.commit_and_tag(
            tree=tree,
            version="v0.104.0",
            buildid="1500",
            prior_buildid="1000",
        )
        assert SHA_RE.match(new_sha)

    def test_accepts_when_prior_buildid_none(self, tmp_path):
        """First sync after bootstrap may pass prior_buildid=None."""
        tree = self._bootstrapped(tmp_path)
        (tree / "src" / "Card.cs").write_text("changed\n")

        new_sha = git_ops.commit_and_tag(
            tree=tree,
            version="v0.104.0",
            buildid="2000",
            prior_buildid=None,
        )
        assert SHA_RE.match(new_sha)

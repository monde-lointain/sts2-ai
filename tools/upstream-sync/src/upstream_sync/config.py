"""Path + constant resolver.

Resolution precedence: explicit CLI flag > env var > default. All other modules
consume `resolve_config(args)` — they MUST NOT read `os.environ` or hardcode
paths directly.
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any

# Steam app ID for Slay the Spire 2.
STS2_APP_ID = "2868840"

# Defaults (Linux Snap-installed Steam). README documents alternates.
DEFAULT_STEAM_HOME = Path("~/snap/steam/common/.local/share/Steam").expanduser()
DEFAULT_GDRE_BIN = Path(
    "~/applications/GDRE_tools-v2.5.0-beta.5-linux/gdre_tools.x86_64"
).expanduser()
DEFAULT_UPSTREAM_TREE = Path("~/development/projects/godot/sts2").expanduser()


@dataclass(frozen=True)
class Config:
    steam_home: Path
    gdre_bin: Path
    upstream_tree: Path
    monorepo_root: Path
    app_id: str = STS2_APP_ID

    @property
    def appmanifest_path(self) -> Path:
        return self.steam_home / "steamapps" / f"appmanifest_{self.app_id}.acf"


def _find_monorepo_root(start: Path) -> Path:
    """Walk up from `start` until a directory containing `.git/` is found."""
    current = start.resolve()
    for candidate in [current, *current.parents]:
        if (candidate / ".git").exists():
            return candidate
    raise RuntimeError(
        f"Could not locate monorepo root (no .git/ found above {start})"
    )


def resolve_config(args: Any | None = None) -> Config:
    """Build a Config from (in order of precedence) flags, env vars, defaults.

    `args` is an argparse-style Namespace with optional attributes
    `steam_home`, `gdre_bin`, `upstream_tree`. Missing or None attributes
    fall through to the env-var / default chain.
    """

    def pick(flag_name: str, env_name: str, default: Path) -> Path:
        flag_val = getattr(args, flag_name, None) if args is not None else None
        if flag_val:
            return Path(flag_val).expanduser().resolve()
        env_val = os.environ.get(env_name)
        if env_val:
            return Path(env_val).expanduser().resolve()
        return default.resolve()

    return Config(
        steam_home=pick("steam_home", "STEAM_HOME", DEFAULT_STEAM_HOME),
        gdre_bin=pick("gdre_bin", "GDRE_BIN", DEFAULT_GDRE_BIN),
        upstream_tree=pick("upstream_tree", "UPSTREAM_TREE", DEFAULT_UPSTREAM_TREE),
        monorepo_root=_find_monorepo_root(Path(__file__).parent),
    )

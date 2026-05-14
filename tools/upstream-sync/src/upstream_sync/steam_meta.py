"""Steam appmanifest (Valve VDF) parser.

Reads `appmanifest_<appid>.acf` files and extracts the top-level `AppState`
scalar keys we care about: `buildid`, `installdir`, `LastUpdated`. Nested
objects (`InstalledDepots`, `UserConfig`, ...) are skipped — we only consume
scalars one level deep inside `AppState`.

Pure stdlib (re, pathlib, dataclasses); no third-party deps.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

# A quoted key followed by a quoted value, separated by any whitespace
# (Steam mixes tabs and spaces).
_KV_RE = re.compile(r'"([^"]+)"[ \t]+"([^"]*)"')

# A quoted key on a line by itself — marks the start of a nested object.
_KEY_ONLY_RE = re.compile(r'^[ \t]*"([^"]+)"[ \t]*$')


@dataclass(frozen=True)
class SteamMeta:
    buildid: str
    installdir: str
    lastupdated: int  # epoch seconds, from "LastUpdated" key


def parse_appmanifest(path: Path) -> SteamMeta:
    """Parse a Valve VDF appmanifest file.

    Raises:
        FileNotFoundError: if path missing
        ValueError: if required keys (buildid, installdir, LastUpdated) absent
            or malformed
    """
    text = Path(path).read_text(encoding="utf-8")
    scalars = _extract_appstate_scalars(text)

    missing = [k for k in ("buildid", "installdir", "LastUpdated") if k not in scalars]
    if missing:
        raise ValueError(
            f"appmanifest {path}: missing required key(s): {', '.join(missing)}"
        )

    last_raw = scalars["LastUpdated"]
    try:
        lastupdated = int(last_raw)
    except ValueError as exc:
        raise ValueError(
            f"appmanifest {path}: LastUpdated must be an integer, got {last_raw!r}"
        ) from exc

    return SteamMeta(
        buildid=scalars["buildid"],
        installdir=scalars["installdir"],
        lastupdated=lastupdated,
    )


def _extract_appstate_scalars(text: str) -> dict[str, str]:
    """Return the top-level scalar key/value pairs inside the AppState block.

    Walks lines tracking brace depth. Depth 1 == inside AppState. Scalars at
    that depth go into the result; nested object headers push depth and their
    contents are ignored.

    Raises ValueError if no AppState block is found.
    """
    lines = text.splitlines()

    # Locate "AppState" header.
    i = 0
    while i < len(lines):
        m = _KEY_ONLY_RE.match(lines[i])
        if m and m.group(1) == "AppState":
            break
        i += 1
    else:
        raise ValueError("no AppState block found")

    # Expect '{' on next non-blank line.
    i += 1
    while i < len(lines) and lines[i].strip() == "":
        i += 1
    if i >= len(lines) or lines[i].strip() != "{":
        raise ValueError("AppState block not followed by '{'")

    depth = 1
    scalars: dict[str, str] = {}
    i += 1
    while i < len(lines) and depth > 0:
        stripped = lines[i].strip()
        if stripped == "{":
            depth += 1
        elif stripped == "}":
            depth -= 1
        elif depth == 1:
            kv = _KV_RE.search(lines[i])
            if kv:
                scalars[kv.group(1)] = kv.group(2)
            # A bare quoted key at depth 1 means a nested object header;
            # the next '{' will bump depth so nothing else to do here.
        # depth > 1 (or 0): inside nested object — skip everything.
        i += 1

    return scalars

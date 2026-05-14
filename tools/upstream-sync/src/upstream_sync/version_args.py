"""Version-spec resolution for the upstream-sync CLI.

`--version` is mandatory for any subcommand that commits/tags.
`--version-from-buildid <buildid>` is the synthetic fallback. STS2 does
not embed a usable version string in source, so there is no auto-detect.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime, timezone

_EXPLICIT_VERSION_RE = re.compile(r"^v\d+\.\d+\.\d+(?:\.\d+)?$")
_BUILDID_RE = re.compile(r"^\d+$")


@dataclass(frozen=True)
class VersionSpec:
    raw: str           # e.g. "v0.105.1" or "build-22823976-2026-05-14"
    is_synthetic: bool  # True if --version-from-buildid was used


def parse_version_spec(
    version: str | None,
    version_from_buildid: bool,
    buildid: str | None,
    today: datetime | None = None,
) -> VersionSpec:
    """Resolve a VersionSpec from CLI args.

    Exactly one of `version` or (`version_from_buildid=True` + `buildid`)
    must be supplied. `today` defaults to `datetime.now(timezone.utc)` and
    exists for test injection.
    """
    if version is not None and version_from_buildid:
        raise ValueError(
            "--version and --version-from-buildid are mutually exclusive"
        )
    if version is None and not version_from_buildid:
        raise ValueError(
            "--version required; use --version-from-buildid <buildid> for synthetic"
        )

    if version is not None:
        if not _EXPLICIT_VERSION_RE.match(version):
            raise ValueError(
                f"invalid --version {version!r}: expected vMAJOR.MINOR.PATCH "
                "(optionally .BUILD), e.g. v0.105.1"
            )
        return VersionSpec(raw=version, is_synthetic=False)

    # Synthetic path.
    if buildid is None:
        raise ValueError("--version-from-buildid requires a buildid argument")
    if not _BUILDID_RE.match(buildid):
        raise ValueError(f"invalid buildid {buildid!r}: must be digits only")

    when = today if today is not None else datetime.now(timezone.utc)
    raw = f"build-{buildid}-{when.strftime('%Y-%m-%d')}"
    return VersionSpec(raw=raw, is_synthetic=True)

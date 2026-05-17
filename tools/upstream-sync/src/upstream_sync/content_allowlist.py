"""Game-content allowlist via upstream-DLL ModelDb introspection (concern #2).

Produces a frozen set of short class names (e.g. ``"Untouchable"``,
``"Inky"``, ``"NightmarePower"``) derived from the upstream sts2.dll assembly.

Introspection strategy (chosen path: Option C — heuristic CamelCase fallback)
==============================================================================

**Why Option C?**

``pythonnet`` (the Python↔CLR bridge) is not guaranteed to be available in this
project's venv, and adding it creates a heavyweight optional dependency that
blocks usage on machines without the Mono/.NET runtime configured for
``pythonnet``.  The C# shell-out approach (Option B) would require maintaining
a parallel dotnet project and adds build-time friction.

Option C — heuristic CamelCase + known-suffix matching — is chosen instead.
It extracts short names directly from the DLL binary by scanning the .NET
metadata region for UTF-8 strings that look like game-content class names.
No reflection runtime is required; only the standard library.

The heuristic is:
1. Read the DLL bytes and locate the ``#Strings`` metadata heap by scanning
   for ``BSJB`` (the CLR metadata magic) and following the stream directory
   offsets.  If the heap cannot be located, fall back to a full-file UTF-8
   string scan (less precise but safe).
2. Scan the heap for null-terminated strings.
3. Filter to strings that:
   - Are 3–80 chars long.
   - Match CamelCase (start with uppercase letter, contain at least two chars).
   - End with one of the known content-model suffixes OR whose short name
     alone matches one of the known base-class short names:
     ``Power``, ``Model``, ``Card``, ``Relic``, ``Encounter``, ``Potion``,
     ``Enchantment``, ``Affliction``, ``Badge``.
   - Do NOT contain path separators, dots (namespace-qualified names are
     excluded — we want simple class names only), or spaces.
4. Additionally include any CamelCase name that appears in a namespace path
   matching ``MegaCrit.Sts2.Core.Models.*`` (detected by proximity in the
   heap: if a namespace string is found within 4 bytes of the class name).

The result is a superset (may include internal/helper classes whose names
happen to match the pattern), but that is safe for the correlator's use-case:
it is used to *filter out* obvious non-game-content strings (UI labels,
credits, generic words like "The" or "A"), not as a ground-truth registry.

Caching
=======

Cache key: ``sha256`` of the DLL bytes, read from
``engine/headless/upstream-pin.json:pinned_dll_sha256``.  Cache file:
``tools/upstream-sync/cache/content-allowlist-<sha7>.json``.

On cache hit (sha256 matches key in file), reflection is skipped.  On hash
drift (new DLL), the heuristic runs again and the cache is overwritten.

If the DLL is not present (common on CI machines), the function returns an
empty frozenset and logs a warning — the correlator treats an empty allowlist
as "disabled" (no filtering).
"""

from __future__ import annotations

import hashlib
import json
import re
import struct
import sys
from pathlib import Path

__all__ = [
    "build_allowlist",
    "load_allowlist",
]

# ---------------------------------------------------------------------------
# Known content-model suffixes (from upstream base classes).
# ---------------------------------------------------------------------------

_CONTENT_SUFFIXES: tuple[str, ...] = (
    "Power",
    "Model",
    "Card",
    "Relic",
    "Encounter",
    "Potion",
    "Enchantment",
    "Affliction",
    "Badge",
)

# Regex: CamelCase name (starts uppercase, 3–80 chars, only word chars, no dot/slash).
_CAMEL_RE = re.compile(r"^[A-Z][A-Za-z0-9]{2,79}$")

# Mega Crit namespace prefix for proximity detection.
_MEGACRIT_NS = b"MegaCrit.Sts2.Core.Models"

# Generic programming/CLR terms to exclude from the broad scan.
# These appear frequently in .NET metadata but are not game content.
_EXCLUDE_PREFIXES: tuple[str, ...] = (
    "Abstract",
    "Base",
    "System",
    "Microsoft",
    "Newtonsoft",
    "Godot",
    "Assembly",
    "Runtime",
    "Reflection",
    "Threading",
    "Collections",
    "Generic",
    "Linq",
    "Serialization",
    "Diagnostics",
    "Attribute",
    "Exception",
    "Handler",
    "Manager",
    "Controller",
    "Interface",
    "Impl",
    "Factory",
    "Builder",
    "Provider",
    "Service",
    "Repository",
    "Listener",
    "Observer",
)
# Exact names that are known non-game-content despite CamelCase form.
_EXCLUDE_EXACT: frozenset[str] = frozenset(
    {
        "True",
        "False",
        "Null",
        "Object",
        "String",
        "Int32",
        "Int64",
        "Boolean",
        "Double",
        "Single",
        "Byte",
        "Char",
        "Void",
        "Array",
        "List",
        "Dictionary",
        "HashSet",
        "Queue",
        "Stack",
        "Tuple",
        "Task",
        "Thread",
        "Action",
        "Func",
        "EventArgs",
        "Args",
        # Common 3-char CLR prefixes that appear as short tokens
        "Get",
        "Set",
        "Has",
        "Add",
        "Run",
        "Try",
        "Use",
        "New",
        "Old",
        "Can",
        "All",
        "Any",
        "Map",
        "For",
        "Out",
        "Ref",
        "Now",
        "End",
    }
)


# ---------------------------------------------------------------------------
# Internal: DLL scanning
# ---------------------------------------------------------------------------


def _sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _locate_strings_heap(data: bytes) -> bytes | None:
    """Attempt to locate the CLI metadata #Strings heap.

    Returns the heap bytes on success, None if not found (caller falls back
    to full-file scan).
    """
    # CLR metadata magic
    bsjb = data.find(b"BSJB")
    if bsjb < 0:
        return None

    try:
        # CLR 2.0 metadata header layout (offset from BSJB):
        #   +0  magic (4)
        #   +4  MajorVersion (2)
        #   +6  MinorVersion (2)
        #   +8  Reserved (4)
        #   +12 VersionLength (4)
        version_len_offset = bsjb + 12
        if version_len_offset + 4 > len(data):
            return None
        (version_len,) = struct.unpack_from("<I", data, version_len_offset)
        # Align to 4 bytes.
        version_len_aligned = (version_len + 3) & ~3

        # After version string:
        #   +0  Flags (2)
        #   +2  Streams (2)  — number of stream headers
        flags_offset = bsjb + 16 + version_len_aligned
        if flags_offset + 4 > len(data):
            return None
        (num_streams,) = struct.unpack_from("<H", data, flags_offset + 2)

        # Stream headers start at flags_offset + 4.
        cursor = flags_offset + 4
        for _ in range(num_streams):
            if cursor + 8 > len(data):
                break
            (stream_offset, stream_size) = struct.unpack_from("<II", data, cursor)
            cursor += 8
            # Name: null-terminated, padded to 4-byte alignment.
            name_start = cursor
            name_end = data.find(b"\x00", name_start)
            if name_end < 0:
                break
            name = data[name_start:name_end]
            # Advance cursor past name (null-terminated + padding to 4 bytes)
            name_field_len = name_end - name_start + 1
            name_field_len = (name_field_len + 3) & ~3
            cursor += name_field_len

            if name == b"#Strings":
                abs_offset = bsjb + stream_offset
                if abs_offset + stream_size <= len(data):
                    return data[abs_offset : abs_offset + stream_size]
    except (struct.error, IndexError):
        pass
    return None


def _is_candidate(s: str) -> bool:
    """Return True if `s` is a plausible game-content class name.

    Two-tier acceptance:
    1. Ends with a known content suffix → always accept (high confidence).
    2. No known suffix → accept if it doesn't start with an excluded prefix
       and isn't an exact excluded term and is 3–40 chars (short names are
       more likely to be leaf class names like "Untouchable" or "Inky").
    """
    if not s or not _CAMEL_RE.match(s):
        return False
    if s in _EXCLUDE_EXACT:
        return False
    if any(s.endswith(sfx) for sfx in _CONTENT_SUFFIXES):
        return True  # suffix match → always accept
    # No suffix: require short name and no excluded prefix.
    if len(s) > 40:
        return False
    return not any(s.startswith(pfx) for pfx in _EXCLUDE_PREFIXES)


def _scan_strings(raw: bytes) -> set[str]:
    """Scan null-terminated strings from a byte region and return CamelCase candidates."""
    results: set[str] = set()
    start = 0
    while start < len(raw):
        end = raw.find(b"\x00", start)
        if end < 0:
            end = len(raw)
        chunk = raw[start:end]
        start = end + 1
        if not chunk:
            continue
        try:
            s = chunk.decode("utf-8", errors="ignore")
        except Exception:  # noqa: BLE001, S112
            continue
        s = s.strip()
        if _is_candidate(s):
            results.add(s)
    return results


def _proximity_scan(data: bytes) -> set[str]:
    """Scan for CamelCase names that appear near the MegaCrit namespace string.

    Used as supplementary pass: catches simple names that don't end with a
    known suffix but are directly used as leaf class names in the Models
    namespace (e.g. ``"Untouchable"`` whose full name would be adjacent to
    ``"MegaCrit.Sts2.Core.Models.Cards"`` in the heap).

    Strategy: for each occurrence of the MegaCrit namespace prefix, scan
    ±512 bytes for null-terminated CamelCase tokens.
    """
    results: set[str] = set()
    search_start = 0
    while True:
        idx = data.find(_MEGACRIT_NS, search_start)
        if idx < 0:
            break
        search_start = idx + 1
        window_start = max(0, idx - 512)
        window_end = min(len(data), idx + len(_MEGACRIT_NS) + 512)
        window = data[window_start:window_end]
        # Scan null-separated chunks within window.
        for chunk in window.split(b"\x00"):
            if not chunk:
                continue
            try:
                s = chunk.decode("utf-8", errors="ignore").strip()
            except Exception:  # noqa: BLE001, S112
                continue
            if _is_candidate(s):
                results.add(s)
    return results


def _scan_dot_delimited(data: bytes) -> set[str]:
    """Extract CamelCase tokens from dot-delimited strings in the DLL binary.

    .NET assemblies store string constants (method names, type names, property
    names) as dot-delimited sequences (e.g. b"get_IsUpgradable.Untouchable.Reliable").
    This scan finds printable ASCII runs, splits on `.`, and extracts CamelCase
    tokens that pass ``_is_candidate``.

    This complements ``_scan_strings`` (which handles null-terminated strings in
    the metadata heap) and ``_proximity_scan`` (which handles names near the
    MegaCrit namespace prefix).
    """
    results: set[str] = set()
    # Scan in chunks to avoid enormous single-pass over the entire DLL.
    # We look for runs of printable ASCII (0x20-0x7E) of length >= 4.
    ascii_re = re.compile(rb"[ -~]{4,}")
    for m in ascii_re.finditer(data):
        segment = m.group(0).decode("ascii", errors="ignore")
        # Split on common delimiters: . / _ space
        for token in re.split(r"[./_ ]", segment):
            if _is_candidate(token):
                results.add(token)
    return results


def _build_from_dll(dll_path: Path) -> frozenset[str]:
    """Scan ``dll_path`` and return CamelCase content-class names."""
    data = dll_path.read_bytes()

    # Try precise #Strings heap extraction first.
    heap = _locate_strings_heap(data)
    if heap is not None:
        names = _scan_strings(heap)
    else:
        # Fallback: full-file scan (slower but always works).
        names = _scan_strings(data)

    # Proximity scan for short names near MegaCrit namespace.
    names |= _proximity_scan(data)

    # Dot-delimited scan: catches leaf names like "Untouchable" embedded in
    # method/property name sequences (common in .NET IL metadata).
    names |= _scan_dot_delimited(data)

    return frozenset(names)


# ---------------------------------------------------------------------------
# Cache helpers
# ---------------------------------------------------------------------------


def _cache_path_for_sha(cache_dir: Path, sha7: str) -> Path:
    return cache_dir / f"content-allowlist-{sha7}.json"


def _load_cache(cache_dir: Path, sha256: str) -> frozenset[str] | None:
    """Return cached allowlist if sha256 matches; else None."""
    sha7 = sha256[:7]
    path = _cache_path_for_sha(cache_dir, sha7)
    if not path.exists():
        return None
    try:
        obj = json.loads(path.read_bytes())
        if obj.get("dll_sha256") == sha256:
            return frozenset(obj.get("names", []))
    except (OSError, json.JSONDecodeError, KeyError):
        pass
    return None


def _save_cache(cache_dir: Path, sha256: str, names: frozenset[str]) -> None:
    sha7 = sha256[:7]
    path = _cache_path_for_sha(cache_dir, sha7)
    try:
        cache_dir.mkdir(parents=True, exist_ok=True)
        obj = {
            "schema_version": "v1",
            "dll_sha256": sha256,
            "name_count": len(names),
            "names": sorted(names),
        }
        path.write_text(json.dumps(obj, indent=2, sort_keys=False) + "\n", encoding="utf-8")
    except OSError as exc:
        _warn(f"content-allowlist cache write failed: {exc!r}")


def _warn(msg: str) -> None:
    print(f"[upstream-sync] warning: {msg}", file=sys.stderr)


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------


def build_allowlist(
    dll_path: Path | None = None,
    cache_dir: Path | None = None,
    pin_json_path: Path | None = None,
    *,
    _dll_sha256_override: str | None = None,
) -> frozenset[str]:
    """Build (or load from cache) the game-content class-name allowlist.

    Parameters
    ----------
    dll_path:
        Path to ``sts2.dll``.  If ``None``, the canonical Steam path is used:
        ``~/snap/steam/common/.local/share/Steam/steamapps/common/
        Slay the Spire 2/data_sts2_linuxbsd_x86_64/sts2.dll``.
    cache_dir:
        Directory for the JSON cache file.  Defaults to the ``cache/``
        directory adjacent to this package's ``tools/upstream-sync/cache/``.
    pin_json_path:
        Path to ``engine/headless/upstream-pin.json`` — used to read the
        ``pinned_dll_sha256`` for cache-key lookup. If omitted and
        ``_dll_sha256_override`` is not set, the sha256 is computed from the
        DLL bytes directly.
    _dll_sha256_override:
        Testing hook — skip all file I/O and DLL scanning; treat this hex
        string as the sha256.

    Returns
    -------
    frozenset[str]
        Set of short class names (e.g. ``{"Untouchable", "InkyCard", ...}``).
        Empty frozenset if the DLL is not present (CI) or unreadable.

    Notes
    -----
    An empty allowlist is treated by the correlator as "disabled" (no
    entity-name filtering applied).  This is the safe fallback for CI.
    """
    # Resolve DLL path.
    if dll_path is None:
        dll_path = (
            Path.home()
            / "snap/steam/common/.local/share/Steam"
            / "steamapps/common/Slay the Spire 2"
            / "data_sts2_linuxbsd_x86_64/sts2.dll"
        )

    # Resolve cache dir.
    if cache_dir is None:
        # Default: tools/upstream-sync/cache/ relative to this module.
        _this = Path(__file__).resolve()
        # src/upstream_sync/content_allowlist.py → tools/upstream-sync/cache/
        cache_dir = _this.parent.parent.parent.parent / "cache"

    # Determine sha256.
    sha256: str | None = _dll_sha256_override

    if sha256 is None and pin_json_path is not None and pin_json_path.exists():
        try:
            pin = json.loads(pin_json_path.read_bytes())
            sha256 = pin.get("pinned_dll_sha256")
        except (OSError, json.JSONDecodeError):
            pass

    if sha256 is None:
        # Compute from DLL bytes (also checks DLL presence).
        if not dll_path.exists():
            _warn(
                f"sts2.dll not found at {dll_path}; content allowlist disabled (empty frozenset)."
            )
            return frozenset()
        try:
            dll_bytes = dll_path.read_bytes()
            sha256 = _sha256_hex(dll_bytes)
        except OSError as exc:
            _warn(f"could not read sts2.dll: {exc!r}; content allowlist disabled.")
            return frozenset()

    # Cache hit?
    cached = _load_cache(cache_dir, sha256)
    if cached is not None:
        return cached

    # Need to scan — DLL must be present.
    if not dll_path.exists():
        _warn(f"sts2.dll not found at {dll_path}; content allowlist disabled (empty frozenset).")
        return frozenset()

    try:
        names = _build_from_dll(dll_path)
    except OSError as exc:
        _warn(f"DLL scan failed: {exc!r}; content allowlist disabled.")
        return frozenset()

    _save_cache(cache_dir, sha256, names)
    return names


def load_allowlist(
    cache_dir: Path | None = None,
    sha256: str | None = None,
) -> frozenset[str] | None:
    """Load allowlist from cache only (no DLL scan).

    Returns ``None`` if not cached yet.  Used by tests and the correlator
    when the DLL is not available but a prior cache exists.
    """
    if cache_dir is None:
        _this = Path(__file__).resolve()
        cache_dir = _this.parent.parent.parent.parent / "cache"
    if sha256 is None:
        return None
    return _load_cache(cache_dir, sha256)

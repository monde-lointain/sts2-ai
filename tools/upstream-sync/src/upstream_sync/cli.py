"""upstream-sync CLI entry point.

Subcommands compose the prior W1-W5 modules into a manual-trigger pipeline
for detecting / extracting / diffing STS2 patches and emitting a port-decision
markdown document the Q1 lead reviews.

The public surface of this module is :func:`main`. Everything else is private
glue (``_cmd_*`` per-subcommand handlers, ``_read_state`` / ``_write_state``
JSON helpers, ``_acquire_lock`` for the fcntl exclusive lock).

State file: ``<monorepo_root>/.upstream-sync-state.json`` (gitignored).
Lock file: ``/tmp/sts2-upstream-sync.lock`` (exclusive, non-blocking flock).
"""

from __future__ import annotations

import argparse
import errno
import fcntl
import importlib.metadata
import json
import logging
import os
import sys
import tempfile
import uuid
from collections.abc import Iterator
from contextlib import contextmanager, suppress
from datetime import UTC, datetime
from pathlib import Path

from upstream_sync.config import resolve_config
from upstream_sync.correlate import correlate
from upstream_sync.diff_analyze import analyze_diff
from upstream_sync.extract import extract_to_staging, rsync_with_delete
from upstream_sync.git_ops import (
    assert_clean,
    bootstrap,
    commit_and_tag,
    list_tags,
)
from upstream_sync.patch_notes import fetch_patch_notes
from upstream_sync.port_decisions import (
    RenderInputs,
    build_port_rows,
    build_q4_advisory,
    render,
    write_doc,
    write_sidecar,
)
from upstream_sync.prompt_generator import (
    PromptInputs,
    load_row_from_sidecar,
    render_prompt,
)
from upstream_sync.steam_meta import parse_appmanifest
from upstream_sync.version_args import parse_version_spec

logger = logging.getLogger("upstream_sync.cli")

STATE_FILENAME = ".upstream-sync-state.json"
LOCK_PATH = Path("/tmp/sts2-upstream-sync.lock")
TOOL_VERSION_FALLBACK = "0.1.0"
GDRE_VERSION_FALLBACK = "v2.5.0-beta.5"


# --------------------------------------------------------------------------- #
# State file helpers                                                          #
# --------------------------------------------------------------------------- #


def _state_path(monorepo_root: Path) -> Path:
    return monorepo_root / STATE_FILENAME


def _read_state(monorepo_root: Path) -> dict | None:
    """Return parsed state dict, ``None`` if file absent.

    Supports schema v0 (no ``schema_version`` field) and v1
    (``schema_version`` = "v1"). v0 files are auto-promoted to v1 shape
    on the next ``_write_state`` call; new fields default to ``None``.

    Raises ``RuntimeError`` on JSON parse failure (corrupted state demands
    operator attention; silent fallthrough would lose the prior buildid).
    """
    target = _state_path(monorepo_root)
    if not target.exists():
        return None
    try:
        data = json.loads(target.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"upstream-sync state file {target} is corrupted: {exc}") from exc
    # Promote v0 → v1 in-memory (write happens on next _write_state call).
    if "schema_version" not in data:
        data.setdefault("last_synced_dll_sha256", None)
        data.setdefault("gdre_version", None)
        data["schema_version"] = "v0"  # mark as not-yet-promoted
    return data


def _write_state(monorepo_root: Path, state: dict) -> None:
    """Atomically write state via tempfile + rename.

    Always emits schema_version=v1; promotes legacy v0 files on first write.
    New v1 fields that were absent from a v0 read default to ``None`` if
    not supplied by the caller.
    """
    target = _state_path(monorepo_root)
    target.parent.mkdir(parents=True, exist_ok=True)
    # Ensure v1 fields are present (may be None for legacy callers).
    out = dict(state)
    out.setdefault("last_synced_dll_sha256", None)
    out.setdefault("gdre_version", None)
    out["schema_version"] = "v1"
    with tempfile.NamedTemporaryFile(
        mode="w",
        dir=str(target.parent),
        prefix=".upstream-sync-state-",
        suffix=".tmp",
        delete=False,
        encoding="utf-8",
    ) as tmp:
        json.dump(out, tmp, indent=2, sort_keys=True)
        tmp.write("\n")
        tmp_path = Path(tmp.name)
    os.replace(tmp_path, target)


def _tool_version() -> str:
    try:
        return importlib.metadata.version("upstream-sync")
    except importlib.metadata.PackageNotFoundError:
        return TOOL_VERSION_FALLBACK


def _utc_now_iso() -> str:
    return datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ")


# --------------------------------------------------------------------------- #
# Lock                                                                        #
# --------------------------------------------------------------------------- #


@contextmanager
def _acquire_lock() -> Iterator[None]:
    """Acquire a non-blocking exclusive flock on LOCK_PATH.

    Yields control to the caller while the lock is held; releases on exit.
    Raises ``BlockingIOError`` if another sync is already running.
    """
    LOCK_PATH.parent.mkdir(parents=True, exist_ok=True)
    fh = open(LOCK_PATH, "w", encoding="utf-8")  # noqa: SIM115 — flock lifetime spans the context
    try:
        fcntl.flock(fh.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            yield
        finally:
            with suppress(OSError):
                fcntl.flock(fh.fileno(), fcntl.LOCK_UN)
    finally:
        fh.close()


# --------------------------------------------------------------------------- #
# Monorepo resolution (test seam)                                             #
# --------------------------------------------------------------------------- #


def _resolve_monorepo(args: argparse.Namespace) -> Path:
    """Override hook used by tests to point at a tmp_path-rooted fake monorepo.

    Production callers go through resolve_config(), which walks up from
    upstream_sync's package dir looking for .git/. Tests monkeypatch this
    function to inject their own monorepo root.
    """
    return resolve_config(args).monorepo_root


# --------------------------------------------------------------------------- #
# Steam pck path resolution                                                   #
# --------------------------------------------------------------------------- #


def _pck_path(steam_home: Path, installdir: str) -> Path:
    """Default GDRE input PCK path for an appmanifest's installdir.

    Megacrit's convention: installdir uses display name with spaces
    ("Slay the Spire 2"), but the .pck file uses CamelCase
    ("SlayTheSpire2.pck"). We glob for *.pck in the install dir to
    accommodate any future rename.
    """
    install_dir = steam_home / "steamapps" / "common" / installdir
    pcks = sorted(install_dir.glob("*.pck"))
    if not pcks:
        # Fall through to a canonical default path so error messages still
        # point somewhere meaningful when GDRE wraps in extract.py.
        return install_dir / "SlayTheSpire2.pck"
    return pcks[0]


# --------------------------------------------------------------------------- #
# Subcommand handlers                                                         #
# --------------------------------------------------------------------------- #


def _cmd_check(args: argparse.Namespace) -> int:
    cfg = resolve_config(args)
    monorepo = _resolve_monorepo(args)

    meta = parse_appmanifest(cfg.appmanifest_path)
    state = _read_state(monorepo)

    print(f"Steam buildid: {meta.buildid}")
    print(f"Steam installdir: {meta.installdir}")

    if state is None:
        print("Status: no prior sync (first run); bootstrap will run on next `sync`")
        return 0

    prior_buildid = state.get("last_synced_buildid", "")
    prior_version = state.get("last_synced_version", "?")
    prior_at = state.get("last_synced_at", "?")
    print(f"Last synced: buildid {prior_buildid} / {prior_version} at {prior_at}")

    try:
        prior_int = int(prior_buildid)
        cur_int = int(meta.buildid)
    except ValueError:
        prior_int = cur_int = 0

    if cur_int == prior_int:
        print("Status: no patch detected (current buildid == last_synced_buildid)")
        return 0

    if cur_int < prior_int:
        print(
            f"Status: WARNING: buildid {meta.buildid} < last_synced {prior_buildid}"
            " — Steam branch revert?"
        )
        return 0

    print(f"Status: NEW PATCH DETECTED: buildid {meta.buildid} > last_synced {prior_buildid}")

    cache_dir = monorepo / "tools" / "upstream-sync" / "cache" / "patch-notes"
    try:
        notes = fetch_patch_notes(app_id=cfg.app_id, count=20, cache_dir=cache_dir)
    except Exception as exc:  # noqa: BLE001 — best-effort
        print(f"  (could not fetch patch notes: {exc})")
        return 0

    if not notes:
        print("Patch notes since last sync: none retrievable")
        return 0

    print("Patch notes (most recent first):")
    for note in notes[:5]:
        ts = (
            datetime.fromtimestamp(note.date, tz=UTC).strftime("%Y-%m-%d")
            if note.date
            else "????-??-??"
        )
        print(f"  - {ts}: {note.title!r}")
    return 0


def _cmd_extract(args: argparse.Namespace) -> int:
    cfg = resolve_config(args)
    monorepo = _resolve_monorepo(args)

    meta = parse_appmanifest(cfg.appmanifest_path)
    state = _read_state(monorepo)
    prior_buildid = state.get("last_synced_buildid") if state else None

    version_spec = parse_version_spec(
        version=args.version,
        version_from_buildid=getattr(args, "version_from_buildid", False),
        buildid=meta.buildid,
    )

    # First-run bootstrap path.
    if not (cfg.upstream_tree / ".git").exists():
        sha = bootstrap(
            cfg.upstream_tree,
            version_spec.raw,
            meta.buildid,
            gdre_version=GDRE_VERSION_FALLBACK,
        )
        print(f"Bootstrap complete: HEAD {sha}, tagged {version_spec.raw}")
        _write_state(
            monorepo,
            {
                "tool_version": _tool_version(),
                "last_synced_buildid": meta.buildid,
                "last_synced_version": version_spec.raw,
                "last_synced_at": _utc_now_iso(),
                "upstream_tree_path": str(cfg.upstream_tree),
            },
        )
        return 0

    # Subsequent run: clean check, GDRE, rsync, commit + tag.
    assert_clean(cfg.upstream_tree)

    staging_dir = Path(tempfile.gettempdir()) / f"sts2-extract-{uuid.uuid4().hex[:8]}"
    pck = _pck_path(cfg.steam_home, meta.installdir)

    result = extract_to_staging(pck, staging_dir, cfg.gdre_bin)
    if result.unmatched_paths:
        print(
            f"Allowlist surveillance: {len(result.unmatched_paths)} unmatched top-level path(s):",
            file=sys.stderr,
        )
        for name, size in result.unmatched_paths:
            print(f"  - {name} ({size} bytes)", file=sys.stderr)

    rsync_with_delete(staging_dir, cfg.upstream_tree)
    sha = commit_and_tag(
        cfg.upstream_tree,
        version_spec.raw,
        meta.buildid,
        prior_buildid,
    )
    print(f"Commit + tag complete: HEAD {sha}, tagged {version_spec.raw}")

    _write_state(
        monorepo,
        {
            "tool_version": _tool_version(),
            "last_synced_buildid": meta.buildid,
            "last_synced_version": version_spec.raw,
            "last_synced_at": _utc_now_iso(),
            "upstream_tree_path": str(cfg.upstream_tree),
        },
    )
    return 0


def _default_tag_range(
    upstream_tree: Path, from_arg: str | None, to_arg: str | None
) -> tuple[str, str]:
    """Resolve --from / --to defaults to the last two tags in chronological order."""
    if from_arg and to_arg:
        return from_arg, to_arg
    tags = list_tags(upstream_tree)
    if len(tags) < 2:
        raise RuntimeError(
            f"upstream tree at {upstream_tree} has fewer than 2 tags; "
            "supply --from and --to explicitly"
        )
    from_tag = from_arg or tags[-2]
    to_tag = to_arg or tags[-1]
    return from_tag, to_tag


def _cmd_diff(args: argparse.Namespace) -> int:
    cfg = resolve_config(args)
    from_tag, to_tag = _default_tag_range(cfg.upstream_tree, args.from_tag, args.to_tag)

    report = analyze_diff(from_tag, to_tag, cfg.upstream_tree)
    print(f"Diff {from_tag}..{to_tag}")
    if not report.buckets:
        print("  (no changes)")
        return 0
    for bucket in sorted(report.buckets.keys()):
        entries = report.buckets[bucket]
        print(f"  {bucket}: {len(entries)} entries")
    return 0


def _cmd_port_decisions(args: argparse.Namespace) -> int:
    cfg = resolve_config(args)
    monorepo = _resolve_monorepo(args)

    from_tag, to_tag = _default_tag_range(cfg.upstream_tree, args.from_tag, args.to_tag)

    report = analyze_diff(from_tag, to_tag, cfg.upstream_tree)
    cache_dir = monorepo / "tools" / "upstream-sync" / "cache" / "patch-notes"
    notes = fetch_patch_notes(app_id=cfg.app_id, count=20, cache_dir=cache_dir)
    correlation = correlate(
        report,
        notes,
        discovered_characters=getattr(report, "discovered_characters", set()),
    )
    advisory = build_q4_advisory(report, cfg.upstream_tree)

    state = _read_state(monorepo)
    from_buildid = state.get("last_synced_buildid") if state else None

    generated_at = _utc_now_iso()
    tool_ver = _tool_version()

    inputs = RenderInputs(
        diff_report=report,
        correlation_map=correlation,
        q4_advisory=advisory,
        from_buildid=from_buildid,
        to_buildid="",
        generated_at=generated_at,
        tool_version=tool_ver,
        priority_character="Silent",
    )

    rendered = render(inputs)
    version_range = f"{from_tag}-to-{to_tag}"
    path = write_doc(rendered, monorepo, version_range)
    print(f"Port-decision doc written: {path}")

    # JSON sidecar — always emitted alongside the markdown.
    rows_by_bucket = build_port_rows(report, correlation, inputs.priority_character)
    sidecar_path = write_sidecar(
        rows_by_bucket=rows_by_bucket,
        monorepo_root=monorepo,
        version_range=version_range,
        generated_at=generated_at,
        tool_version=tool_ver,
    )
    print(f"Port-decision sidecar written: {sidecar_path}")
    return 0


def _cmd_sync(args: argparse.Namespace) -> int:
    cfg = resolve_config(args)
    monorepo = _resolve_monorepo(args)

    meta = parse_appmanifest(cfg.appmanifest_path)
    state = _read_state(monorepo)
    prior_buildid = state.get("last_synced_buildid") if state else None
    prior_version = state.get("last_synced_version") if state else None

    version_spec = parse_version_spec(
        version=args.version,
        version_from_buildid=getattr(args, "version_from_buildid", False),
        buildid=meta.buildid,
    )

    if args.dry_run:
        # Skip the GDRE/rsync mutations. If upstream tree is not under git yet,
        # bootstrap from current contents (no extract) — same shape as the
        # extract-mode bootstrap branch below: bootstrap, write state, return.
        if not (cfg.upstream_tree / ".git").exists():
            bootstrap(
                cfg.upstream_tree,
                version_spec.raw,
                meta.buildid,
                gdre_version=GDRE_VERSION_FALLBACK,
            )
            _write_state(
                monorepo,
                {
                    "tool_version": _tool_version(),
                    "last_synced_buildid": meta.buildid,
                    "last_synced_version": version_spec.raw,
                    "last_synced_at": _utc_now_iso(),
                    "upstream_tree_path": str(cfg.upstream_tree),
                },
            )
            print(
                f"[dry-run] Bootstrap complete for {version_spec.raw}; skipping diff (no prior tag)"
            )
            return 0
        print(f"[dry-run] skipping GDRE extract for {version_spec.raw}")
    else:
        # Full extract flow.
        if not (cfg.upstream_tree / ".git").exists():
            bootstrap(
                cfg.upstream_tree,
                version_spec.raw,
                meta.buildid,
                gdre_version=GDRE_VERSION_FALLBACK,
            )
            _write_state(
                monorepo,
                {
                    "tool_version": _tool_version(),
                    "last_synced_buildid": meta.buildid,
                    "last_synced_version": version_spec.raw,
                    "last_synced_at": _utc_now_iso(),
                    "upstream_tree_path": str(cfg.upstream_tree),
                },
            )
            print("Bootstrap complete (first sync); skipping diff (no prior tag)")
            return 0
        assert_clean(cfg.upstream_tree)
        staging_dir = Path(tempfile.gettempdir()) / f"sts2-extract-{uuid.uuid4().hex[:8]}"
        pck = _pck_path(cfg.steam_home, meta.installdir)
        result = extract_to_staging(pck, staging_dir, cfg.gdre_bin)
        if result.unmatched_paths:
            print(
                f"Allowlist surveillance: {len(result.unmatched_paths)} unmatched "
                "top-level path(s):",
                file=sys.stderr,
            )
        rsync_with_delete(staging_dir, cfg.upstream_tree)
        commit_and_tag(
            cfg.upstream_tree,
            version_spec.raw,
            meta.buildid,
            prior_buildid,
        )
        _write_state(
            monorepo,
            {
                "tool_version": _tool_version(),
                "last_synced_buildid": meta.buildid,
                "last_synced_version": version_spec.raw,
                "last_synced_at": _utc_now_iso(),
                "upstream_tree_path": str(cfg.upstream_tree),
            },
        )

    # Diff + port-decisions phase.
    from_tag = prior_version or list_tags(cfg.upstream_tree)[-2]
    to_tag = version_spec.raw

    report = analyze_diff(from_tag, to_tag, cfg.upstream_tree)
    cache_dir = monorepo / "tools" / "upstream-sync" / "cache" / "patch-notes"
    notes = fetch_patch_notes(app_id=cfg.app_id, count=20, cache_dir=cache_dir)
    correlation = correlate(
        report,
        notes,
        discovered_characters=getattr(report, "discovered_characters", set()),
    )
    advisory = build_q4_advisory(report, cfg.upstream_tree)
    inputs = RenderInputs(
        diff_report=report,
        correlation_map=correlation,
        q4_advisory=advisory,
        from_buildid=prior_buildid,
        to_buildid=meta.buildid,
        generated_at=_utc_now_iso(),
        tool_version=_tool_version(),
        priority_character="Silent",
    )
    rendered = render(inputs)
    version_range = f"{from_tag}-to-{to_tag}"
    path = write_doc(rendered, monorepo, version_range)
    print(f"Sync complete: tagged {version_spec.raw}")
    print(f"Port-decision doc written: {path}")
    return 0


# --------------------------------------------------------------------------- #
# prompt-for / dispatch-quantum-lead                                          #
# --------------------------------------------------------------------------- #

# NOTE: These commands emit prompt TEXT to stdout.  They do NOT spawn
# subagents.  The output is a prompt artifact the user pastes into a
# Claude session to dispatch the actual engineer subagent.


def _cmd_prompt_for(args: argparse.Namespace) -> int:
    """Emit an engineer-dispatch prompt for a single port-decision row.

    Reads the row from the JSON sidecar at ``--sidecar`` (or auto-discovers
    the most recent sidecar in engine/headless/docs/specs/).  Writes the
    prompt to stdout (or ``--out`` path).

    Output is a prompt artifact; paste into a Claude session to dispatch
    the actual subagent.  This command does NOT spawn subagents.
    """
    monorepo = _resolve_monorepo(args)
    specs_dir = monorepo / "engine" / "headless" / "docs" / "specs"

    # Resolve sidecar path.
    sidecar_path: Path
    if getattr(args, "sidecar", None):
        sidecar_path = Path(args.sidecar)
    else:
        # Auto-discover: newest JSON sidecar in specs_dir.
        candidates = sorted(
            (f for f in specs_dir.glob("*-port-decisions.json") if f.is_file()),
            key=lambda f: f.name,
        )
        if not candidates:
            print(
                "No port-decisions JSON sidecar found in "
                f"{specs_dir}. Run `port-decisions` first or supply --sidecar.",
                file=sys.stderr,
            )
            return 1
        sidecar_path = candidates[-1]

    row = load_row_from_sidecar(sidecar_path, args.row_path)

    prompt_inputs = PromptInputs(
        row=row,
        version=args.version or "vUNKNOWN",
        wave=getattr(args, "wave", "?"),
        stream_id=getattr(args, "stream_id", "?"),
        expected_sha=getattr(args, "expected_sha", "<SHA>"),
    )
    prompt_text = render_prompt(prompt_inputs)

    out_path = getattr(args, "out", None)
    if out_path:
        Path(out_path).write_text(prompt_text, encoding="utf-8")
        print(f"Prompt written to: {out_path}")
    else:
        print(prompt_text)
    return 0


def _cmd_dispatch_quantum_lead(args: argparse.Namespace) -> int:
    """Emit a quantum-lead briefing prompt for the current port-decision doc.

    Reads the most recent JSON sidecar and renders a summary briefing prompt
    the quantum-lead pastes into a Claude session to orchestrate engineer
    dispatch.  This command does NOT spawn subagents.

    Output is a prompt artifact; paste into Claude session to dispatch the
    quantum-lead subagent.
    """
    monorepo = _resolve_monorepo(args)
    specs_dir = monorepo / "engine" / "headless" / "docs" / "specs"

    sidecar_path: Path
    if getattr(args, "sidecar", None):
        sidecar_path = Path(args.sidecar)
    else:
        candidates = sorted(
            (f for f in specs_dir.glob("*-port-decisions.json") if f.is_file()),
            key=lambda f: f.name,
        )
        if not candidates:
            print(
                "No port-decisions JSON sidecar found. Run `port-decisions` first.",
                file=sys.stderr,
            )
            return 1
        sidecar_path = candidates[-1]

    import json as _json

    data = _json.loads(sidecar_path.read_text(encoding="utf-8"))
    rows = data.get("rows", [])
    version_range = data.get("version_range", "unknown")
    version = args.version or "vUNKNOWN"

    # Summarise by workflow status.
    by_status: dict[str, list[dict]] = {}
    for row in rows:
        s = row.get("status", "UNKNOWN")
        by_status.setdefault(s, []).append(row)

    pending = by_status.get("PENDING", [])
    deferred = by_status.get("DEFERRED", [])
    no_action = by_status.get("NO_ACTION_NEEDED", [])

    lines = [
        f"# Quantum-Lead Briefing — {version} ({version_range})",
        "",
        "This is a prompt artifact. Paste into a Claude session to dispatch "
        "the quantum-lead subagent. This command does NOT spawn subagents.",
        "",
        f"Sidecar: {sidecar_path}",
        f"Total rows: {len(rows)}",
        f"  PENDING: {len(pending)}",
        f"  DEFERRED: {len(deferred)}",
        f"  NO_ACTION_NEEDED: {len(no_action)}",
        "",
        "## PENDING rows (requires engineer dispatch)",
        "",
    ]
    for row in pending:
        lines.append(
            f"  - [{row.get('decision')}] {row.get('bucket', '?')} | {row.get('path', '?')}"
        )
    lines += [
        "",
        "## Suggested dispatch strategy",
        "",
        "Group PENDING rows by bucket. Dispatch one engineer subagent per",
        "file (or per logical group if files are trivially small). Each",
        "subagent must:",
        "  1. Run preflight SHA check.",
        "  2. Use `upstream-sync prompt-for <row-path> --version=<v>` to",
        "     generate the per-row dispatch prompt.",
        "  3. Paste that prompt into a new Claude session to dispatch the",
        "     engineer.",
        "",
        "Preflight expected SHA: <FILL IN from .claude/state/current-wave.json>",
        f"Version: {version}",
    ]

    briefing = "\n".join(lines) + "\n"

    out_path = getattr(args, "out", None)
    if out_path:
        Path(out_path).write_text(briefing, encoding="utf-8")
        print(f"Briefing written to: {out_path}")
    else:
        print(briefing)
    return 0


# --------------------------------------------------------------------------- #
# argparse                                                                    #
# --------------------------------------------------------------------------- #


def _add_global_args(parser: argparse.ArgumentParser) -> None:
    """Global args available on every subcommand."""
    parser.add_argument("--steam-home", help="Override Steam install root")
    parser.add_argument("--gdre-bin", help="Override GDRE binary path")
    parser.add_argument("--upstream-tree", help="Override decompiled tree path")
    parser.add_argument("--verbose", action="store_true", help="Verbose logging to stdout")


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="upstream-sync",
        description="Detect / extract / diff STS2 Steam patches and emit port-decision docs.",
    )
    _add_global_args(parser)
    sub = parser.add_subparsers(dest="subcommand", metavar="<command>")

    p_check = sub.add_parser("check", help="Report sync status; no mutations")
    _add_global_args(p_check)

    p_extract = sub.add_parser("extract", help="Run GDRE + commit + tag (no doc)")
    _add_global_args(p_extract)
    p_extract.add_argument("--version", help="Explicit version, e.g. v0.105.1")
    p_extract.add_argument(
        "--version-from-buildid",
        action="store_true",
        help="Synthetic version from buildid (build-XXX-YYYY-MM-DD)",
    )

    p_diff = sub.add_parser("diff", help="Print diff summary between two tags")
    _add_global_args(p_diff)
    p_diff.add_argument("--from", dest="from_tag", help="Source tag (default: penultimate)")
    p_diff.add_argument("--to", dest="to_tag", help="Destination tag (default: latest)")

    p_pd = sub.add_parser("port-decisions", help="Render port-decision doc from cached data")
    _add_global_args(p_pd)
    p_pd.add_argument("--from", dest="from_tag", help="Source tag (default: penultimate)")
    p_pd.add_argument("--to", dest="to_tag", help="Destination tag (default: latest)")

    p_sync = sub.add_parser("sync", help="Full extract -> diff -> doc pipeline")
    _add_global_args(p_sync)
    p_sync.add_argument("--version", help="Explicit version, e.g. v0.105.1")
    p_sync.add_argument(
        "--version-from-buildid",
        action="store_true",
        help="Synthetic version from buildid",
    )
    p_sync.add_argument(
        "--dry-run",
        action="store_true",
        help="Skip GDRE/rsync; still produce diff + doc",
    )

    # prompt-for — emit engineer-dispatch prompt for a single port-decision row.
    # Output is a prompt artifact; paste into a Claude session. Does NOT spawn
    # subagents.
    p_pf = sub.add_parser(
        "prompt-for",
        help=(
            "Emit an engineer-dispatch prompt for a port-decision row to stdout. "
            "Output is a prompt artifact; paste into Claude session to dispatch "
            "the actual subagent. Does NOT spawn subagents."
        ),
    )
    _add_global_args(p_pf)
    p_pf.add_argument(
        "row_path",
        metavar="<port-decision-row-path>",
        help=(
            "The 'path' value of the row in the JSON sidecar (e.g. src/Core/Models/Monsters/Foo.cs)"
        ),
    )
    p_pf.add_argument("--version", required=True, help="Upstream version, e.g. v0.105.1")
    p_pf.add_argument("--sidecar", help="Path to JSON sidecar (default: auto-discover)")
    p_pf.add_argument(
        "--out",
        metavar="FILE",
        help="Write prompt to FILE instead of stdout (e.g. /tmp/upstream-sync-prompt-<id>.txt)",
    )
    p_pf.add_argument("--wave", default="?", help="Wave number for prompt header")
    p_pf.add_argument(
        "--stream-id", dest="stream_id", default="?", help="Stream ID for prompt header"
    )
    p_pf.add_argument(
        "--expected-sha",
        dest="expected_sha",
        default="<SHA>",
        help="Expected HEAD SHA for preflight",
    )

    # dispatch-quantum-lead — emit a briefing prompt for the quantum-lead.
    # Output is a prompt artifact; paste into a Claude session. Does NOT spawn
    # subagents.
    p_dql = sub.add_parser(
        "dispatch-quantum-lead",
        help=(
            "Emit a quantum-lead briefing prompt summarising PENDING rows to stdout. "
            "Output is a prompt artifact; paste into Claude session to dispatch "
            "the quantum-lead. Does NOT spawn subagents."
        ),
    )
    _add_global_args(p_dql)
    p_dql.add_argument("--version", required=True, help="Upstream version, e.g. v0.105.1")
    p_dql.add_argument("--sidecar", help="Path to JSON sidecar (default: auto-discover)")
    p_dql.add_argument(
        "--out",
        metavar="FILE",
        help="Write briefing to FILE instead of stdout",
    )

    return parser


_DISPATCH_NAMES = {
    "check": "_cmd_check",
    "extract": "_cmd_extract",
    "diff": "_cmd_diff",
    "port-decisions": "_cmd_port_decisions",
    "sync": "_cmd_sync",
    "prompt-for": "_cmd_prompt_for",
    "dispatch-quantum-lead": "_cmd_dispatch_quantum_lead",
}


def _dispatch_handler(name: str):
    """Resolve handler by name at call time (so monkeypatched handlers win)."""
    attr = _DISPATCH_NAMES.get(name)
    if attr is None:
        return None
    return globals().get(attr)


# --------------------------------------------------------------------------- #
# Entry point                                                                 #
# --------------------------------------------------------------------------- #


def main(argv: list[str] | None = None) -> int:
    """Top-level CLI entry: parse args, acquire lock, dispatch.

    Returns int exit code (0 = success; 1 = lock contention or other failure;
    130 = SIGINT). The lock is released in a ``finally`` block; KeyboardInterrupt
    is caught and converted to a 130 return.
    """
    parser = _build_parser()
    args = parser.parse_args(argv)

    if not getattr(args, "subcommand", None):
        parser.print_help(sys.stderr)
        return 2

    if args.verbose:
        logging.basicConfig(level=logging.INFO, format="%(message)s")

    handler = _dispatch_handler(args.subcommand)
    if handler is None:
        print(f"unknown subcommand: {args.subcommand}", file=sys.stderr)
        return 2

    try:
        with _acquire_lock():
            try:
                return handler(args)
            except KeyboardInterrupt:
                print(
                    "Aborted; upstream tree git state preserved.",
                    file=sys.stderr,
                )
                return 130
    except BlockingIOError as exc:
        if exc.errno not in (errno.EAGAIN, errno.EWOULDBLOCK, 0):
            raise
        print(
            f"another sync is in progress (lock {LOCK_PATH}); aborting.",
            file=sys.stderr,
        )
        return 1
    except RuntimeError as exc:
        print(f"upstream-sync: {exc}", file=sys.stderr)
        return 1
    except FileNotFoundError as exc:
        print(f"upstream-sync: {exc}", file=sys.stderr)
        return 1
    except ValueError as exc:
        print(f"upstream-sync: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())

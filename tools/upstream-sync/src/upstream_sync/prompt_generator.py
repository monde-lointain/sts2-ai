"""Engineer-dispatch prompt generator for upstream-sync port decisions.

Given a port-decision row (from the JSON sidecar) and a version string, renders
a structured engineer-dispatch prompt via Jinja2.  Output is plain text
intended to be pasted into a Claude session — this module does NOT spawn
subagents.

Bucket → template mapping:
    monsters        → monster.j2
    <all others>    → generic-port.j2

Templates live in :data:`TEMPLATES_DIR`.

Public surface:
    :class:`PromptInputs` — typed inputs for :func:`render_prompt`.
    :func:`render_prompt` — render a prompt for a single port-decision row.
    :func:`template_for_bucket` — resolve template name from bucket string.

Imports: stdlib + ``jinja2`` + ``upstream_sync.diff_analyze`` (bucket constants).
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import jinja2

from upstream_sync.diff_analyze import BUCKET_MONSTERS

__all__ = [
    "TEMPLATES_DIR",
    "PromptInputs",
    "render_prompt",
    "template_for_bucket",
]

# ---------------------------------------------------------------------------
# Template directory
# ---------------------------------------------------------------------------

TEMPLATES_DIR: Path = Path(__file__).resolve().parent / "prompt_templates"

# ---------------------------------------------------------------------------
# Bucket → template mapping
# ---------------------------------------------------------------------------

_BUCKET_TEMPLATE: dict[str, str] = {
    BUCKET_MONSTERS: "monster.j2",
}
_FALLBACK_TEMPLATE = "generic-port.j2"


def template_for_bucket(bucket: str) -> str:
    """Return the template filename for *bucket*.

    Returns the per-bucket template if one exists; otherwise the generic
    fallback.
    """
    return _BUCKET_TEMPLATE.get(bucket, _FALLBACK_TEMPLATE)


# ---------------------------------------------------------------------------
# Inputs dataclass
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class PromptInputs:
    """All inputs needed to render an engineer-dispatch prompt.

    Attributes:
        row: A port-decision row dict (keys: path, status, decision,
            rationale, bucket, and optional line_delta, patch_notes_hint,
            re_eval_trigger, character_tag).
        version: Upstream version string, e.g. ``"v0.105.1"``.
        wave: Wave number string, e.g. ``"5"``.
        stream_id: Stream identifier, e.g. ``"B.3"``.
        expected_sha: Expected git HEAD SHA for the preflight check.
        extra: Additional template variables (merged last; override allowed).
    """

    row: dict[str, Any]
    version: str
    wave: str
    stream_id: str
    expected_sha: str
    extra: dict[str, Any] | None = None


# ---------------------------------------------------------------------------
# Jinja2 env
# ---------------------------------------------------------------------------


def _build_env() -> jinja2.Environment:
    env = jinja2.Environment(
        loader=jinja2.FileSystemLoader(str(TEMPLATES_DIR)),
        autoescape=False,
        keep_trailing_newline=True,
        trim_blocks=False,
        lstrip_blocks=False,
        undefined=jinja2.Undefined,
    )
    # Add a basename filter (mirrors Python's Path.name)
    env.filters["basename"] = lambda p: p.rsplit("/", 1)[-1]
    return env


_ENV: jinja2.Environment | None = None


def _env() -> jinja2.Environment:
    global _ENV  # noqa: PLW0603
    if _ENV is None:
        _ENV = _build_env()
    return _ENV


# ---------------------------------------------------------------------------
# render_prompt
# ---------------------------------------------------------------------------


def render_prompt(inputs: PromptInputs) -> str:
    """Render an engineer-dispatch prompt for a single port-decision row.

    The template is selected by ``inputs.row["bucket"]`` via
    :func:`template_for_bucket`.  All :class:`PromptInputs` fields are
    forwarded to the template context; ``inputs.extra`` keys are merged in
    last (they may override defaults).

    Returns:
        Rendered prompt string (UTF-8 text; ends with newline).
    """
    row = inputs.row
    bucket = row.get("bucket", "")
    template_name = template_for_bucket(bucket)
    template = _env().get_template(template_name)

    ctx: dict[str, Any] = {
        "row": _RowView(row),
        "version": inputs.version,
        "wave": inputs.wave,
        "stream_id": inputs.stream_id,
        "expected_sha": inputs.expected_sha,
        "bucket": bucket,
        "q4_advisory_hint": row.get("q4_advisory_hint"),
    }
    if inputs.extra:
        ctx.update(inputs.extra)

    return template.render(**ctx)


# ---------------------------------------------------------------------------
# _RowView — attribute-access wrapper for row dicts
# ---------------------------------------------------------------------------


class _RowView:
    """Wrap a port-decision row dict for attribute-style access in templates."""

    __slots__ = ("_d",)

    def __init__(self, d: dict[str, Any]) -> None:
        object.__setattr__(self, "_d", d)

    def __getattr__(self, name: str) -> Any:
        d = object.__getattribute__(self, "_d")
        try:
            return d[name]
        except KeyError:
            return None

    def __repr__(self) -> str:
        return f"_RowView({object.__getattribute__(self, '_d')!r})"


# ---------------------------------------------------------------------------
# load_row_from_sidecar
# ---------------------------------------------------------------------------


def load_row_from_sidecar(sidecar_path: Path, row_path: str) -> dict[str, Any]:
    """Load a single row from a JSON sidecar by its ``path`` field.

    Args:
        sidecar_path: Path to the JSON sidecar file.
        row_path: The ``path`` value of the desired row.

    Returns:
        The matching row dict.

    Raises:
        FileNotFoundError: If *sidecar_path* does not exist.
        ValueError: If no row with *row_path* is found.
    """
    data = json.loads(sidecar_path.read_text(encoding="utf-8"))
    for row in data.get("rows", []):
        if row.get("path") == row_path:
            return row
    raise ValueError(f"No row with path={row_path!r} in {sidecar_path}")

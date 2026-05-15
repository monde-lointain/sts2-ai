"""Content-registry submodule: frozen loader for the Q4 token bundle.

Loads ``contracts/registry/phase1-silent.json`` (or any compatible bundle),
validates required top-level keys, computes the SHA-256 of the raw file
bytes (per Q10-ADR-008 the content_registry_sha must match the bytes that
will be re-attached, unmodified, to the next published Q5 artifact), and
returns a frozen :class:`ContentRegistry` consumed by ``tensor_encoder``.

Constraints:

- Immutable post-construction (``@dataclass(frozen=True)``).
- Pure: only side effect is reading the file at ``path``.
- ``content_hash`` is computed over the *exact* file bytes — never a
  re-serialized canonical form. This mirrors the Q10 boot directive's
  ``content_registry_sha`` stamping requirement.
- The raw file bytes are exposed as ``bytes_blob`` and passed through
  unchanged when ``artifact_publisher`` writes the next artifact.

See ``pipeline/trainer/docs/specs/modules/tensor-encoder.md`` and Q10-ADR-008.
"""
from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional


# ---------------------------------------------------------------------------
# Frozen records
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class Token:
    """One row of the registry token table.

    Mirrors the JSON's per-token shape. ``kind`` is taken from the JSON if
    present; otherwise inferred from the ``token`` prefix (``"card:Foo"``
    → ``"card"``). ``references`` is a tuple for frozen-equality.
    """

    token_id: int
    token: str
    name: str
    kind: str
    content_hash: str
    since_version: str
    deprecated_in: Optional[str]
    references: tuple[str, ...]


@dataclass(frozen=True)
class CardDslEntry:
    """One row of the card_dsl table — Phase-1 stub structure preserved as-is."""

    token: str
    type: str
    cost: str
    target: str
    effects: tuple[dict, ...]  # nested dict shape varies; preserved literally


@dataclass(frozen=True)
class ManifestInfo:
    version: str
    schema_version_major: int
    schema_version_minor: int
    parent_version: Optional[str]


# ---------------------------------------------------------------------------
# ContentRegistry
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class ContentRegistry:
    """Frozen view of a Q4 content-registry bundle.

    ``content_hash`` is SHA-256 hex over the raw file bytes — passed
    through unchanged to the next published Q5 artifact (Q10-ADR-008).
    """

    tokens: tuple[Token, ...]
    card_dsl: tuple[CardDslEntry, ...]
    manifest: ManifestInfo
    content_hash: str
    bytes_blob: bytes
    # Pre-built lookup indices (frozen via the dataclass; built once at load).
    _by_id: dict[int, Token] = field(repr=False, compare=False)
    _by_name: dict[str, Token] = field(repr=False, compare=False)

    # ---- factory ----------------------------------------------------------
    @classmethod
    def load(
        cls,
        path: Path,
        *,
        expected_token_count: Optional[int] = None,
    ) -> "ContentRegistry":
        """Read JSON at ``path``, validate, return frozen ContentRegistry.

        ``content_hash`` is computed from the raw file bytes (no
        re-serialization). If ``expected_token_count`` is supplied, raises
        ``ValueError`` on mismatch.
        """
        raw_bytes = Path(path).read_bytes()
        content_hash = hashlib.sha256(raw_bytes).hexdigest()
        data = json.loads(raw_bytes)

        for required in ("manifest", "tokens", "card_dsl"):
            if required not in data:
                raise ValueError(f"{path}: missing required key {required!r}")

        manifest = _build_manifest(data["manifest"], path)
        tokens = _build_tokens(data["tokens"], path)
        card_dsl = _build_card_dsl(data["card_dsl"])

        if expected_token_count is not None and len(tokens) != expected_token_count:
            raise ValueError(
                f"{path}: token count mismatch — expected "
                f"{expected_token_count}, found {len(tokens)}"
            )

        by_id = {t.token_id: t for t in tokens}
        by_name = {t.name: t for t in tokens}

        return cls(
            tokens=tokens,
            card_dsl=card_dsl,
            manifest=manifest,
            content_hash=content_hash,
            bytes_blob=raw_bytes,
            _by_id=by_id,
            _by_name=by_name,
        )

    # ---- lookups ----------------------------------------------------------
    def get_token_by_id(self, token_id: int) -> Token:
        """Return the Token with the given numeric id. ``KeyError`` on miss."""
        try:
            return self._by_id[int(token_id)]
        except KeyError as exc:
            raise KeyError(f"unknown token_id {token_id}") from exc

    def get_token_by_name(self, name: str) -> Token:
        """Return the Token with the given name. ``KeyError`` on miss."""
        try:
            return self._by_name[name]
        except KeyError as exc:
            raise KeyError(f"unknown token name {name!r}") from exc

    def has_token_id(self, token_id: int) -> bool:
        """Cheap membership test (avoids exception cost in hot paths)."""
        return int(token_id) in self._by_id

    def __len__(self) -> int:
        return len(self.tokens)


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------
def _build_manifest(block: dict, path: Path) -> ManifestInfo:
    if "version" not in block or "schema_version" not in block:
        raise ValueError(f"{path}: manifest missing version/schema_version")
    sv = block["schema_version"]
    if not isinstance(sv, dict) or "major" not in sv or "minor" not in sv:
        raise ValueError(f"{path}: manifest.schema_version must be {{major,minor}}")
    return ManifestInfo(
        version=str(block["version"]),
        schema_version_major=int(sv["major"]),
        schema_version_minor=int(sv["minor"]),
        parent_version=(
            None if block.get("parent_version") is None else str(block["parent_version"])
        ),
    )


def _build_tokens(rows: list[dict], path: Path) -> tuple[Token, ...]:
    if not isinstance(rows, list):
        raise ValueError(f"{path}: tokens must be a list")
    out: list[Token] = []
    for i, row in enumerate(rows):
        if not isinstance(row, dict):
            raise ValueError(f"{path}: tokens[{i}] not an object")
        for required in ("token_id", "token", "name"):
            if required not in row:
                raise ValueError(f"{path}: tokens[{i}] missing {required!r}")
        kind = row.get("kind")
        if not kind:
            kind = _infer_kind(str(row["token"]))
        out.append(
            Token(
                token_id=int(row["token_id"]),
                token=str(row["token"]),
                name=str(row["name"]),
                kind=str(kind),
                content_hash=str(row.get("content_hash", "")),
                since_version=str(row.get("since_version", "")),
                deprecated_in=(
                    None
                    if row.get("deprecated_in") is None
                    else str(row["deprecated_in"])
                ),
                references=tuple(str(r) for r in row.get("references", [])),
            )
        )
    return tuple(out)


def _build_card_dsl(rows: list[dict]) -> tuple[CardDslEntry, ...]:
    if not isinstance(rows, list):
        return tuple()
    out: list[CardDslEntry] = []
    for row in rows:
        if not isinstance(row, dict):
            continue
        effects = row.get("effects", []) or []
        # Effects entries stay as plain dicts (Phase-1 stub shape).
        eff_tuple = tuple(dict(e) for e in effects if isinstance(e, dict))
        out.append(
            CardDslEntry(
                token=str(row.get("token", "")),
                type=str(row.get("type", "")),
                cost=str(row.get("cost", "")),
                target=str(row.get("target", "")),
                effects=eff_tuple,
            )
        )
    return tuple(out)


def _infer_kind(token: str) -> str:
    """Fallback ``kind`` derivation from a colon-prefixed token (``"card:Foo"``).

    Returns ``"special"`` for non-prefixed tokens like ``"[CLS]"``.
    """
    if ":" in token:
        return token.split(":", 1)[0]
    return "special"


__all__ = [
    "ContentRegistry",
    "Token",
    "CardDslEntry",
    "ManifestInfo",
]

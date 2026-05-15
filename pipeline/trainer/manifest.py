"""Provenance manifest schema (v1) — owned by Q10 ``artifact_publisher``.

This is the **only** schema Q10 owns end-to-end. v1 is locked for Phase-1;
future bumps require a new Q10 ADR and a coordinated Q5 substrate update.

Schema (every field required; ``parent_artifact_id`` may be JSON ``null``):

==========================  ==============  ====================================
field                       type            notes
==========================  ==============  ====================================
``schema_version``          ``int``         always ``1`` (Phase-1)
``artifact_id``             ``str``         uuid4 hex, minted at publish time
``code_sha``                ``str``         from :class:`RunProvenance`
``code_dirty``              ``bool``        from :class:`RunProvenance.run_dirty`
``dataset_sha``             ``str``         sha256 hex (Q10-ADR-003)
``dataset_size``            ``int``         ``len(consumed_ids)``
``seed``                    ``int``         from :class:`RunProvenance.seed`
``hyperparameters``         ``dict``        JSON-serializable; from RunConfig
``parent_artifact_id``      ``str | None``  may be null for from-scratch runs
``content_registry_sha``    ``str``         sha256 hex from :class:`ContentRegistry`
``onnx_opset_version``      ``int``         always ``17`` Phase-1 (Q10-ADR-006)
``phase``                   ``int``         always ``1``
``step``                    ``int``         training step at publish
``created_at_ns``           ``int``         wall-clock ns at publish
``host``                    ``str``         hostname
``run_id``                  ``str``         ULID from :class:`RunConfig`
==========================  ==============  ====================================

See ``pipeline/trainer/docs/specs/modules/artifact-publisher.md`` and
Q10-ADR-006 / Q10-ADR-009 for the rationale.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass
from types import MappingProxyType
from typing import Any, Mapping


# Required schema-v1 fields. Order matches the JSON layout written to disk
# (``sort_keys=True`` in ``atomic_write_json`` re-sorts alphabetically; this
# ordering is only used for ``to_dict`` insertion + ``validate`` errors).
_V1_REQUIRED_FIELDS: tuple[tuple[str, type | tuple[type, ...]], ...] = (
    ("schema_version", int),
    ("artifact_id", str),
    ("code_sha", str),
    ("code_dirty", bool),
    ("dataset_sha", str),
    ("dataset_size", int),
    ("seed", int),
    ("hyperparameters", dict),
    # ``parent_artifact_id`` may be None — handled specially in validate().
    ("parent_artifact_id", (str, type(None))),
    ("content_registry_sha", str),
    ("onnx_opset_version", int),
    ("phase", int),
    ("step", int),
    ("created_at_ns", int),
    ("host", str),
    ("run_id", str),
)


SCHEMA_VERSION: int = 1


@dataclass(frozen=True)
class ProvenanceManifest:
    """v1 provenance manifest written into every Q5-stub bundle (Phase-1).

    Frozen post-construction. Serialize via :meth:`to_dict`; deserialize via
    :meth:`from_dict`. The schema-v1 invariants are enforced by
    :func:`validate` on every read path.
    """

    schema_version: int
    artifact_id: str
    code_sha: str
    code_dirty: bool
    dataset_sha: str
    dataset_size: int
    seed: int
    hyperparameters: Mapping[str, Any]
    parent_artifact_id: str | None
    content_registry_sha: str
    onnx_opset_version: int
    phase: int
    step: int
    created_at_ns: int
    host: str
    run_id: str

    # ------------------------------------------------------------------
    # Serialization
    # ------------------------------------------------------------------
    def to_dict(self) -> dict[str, Any]:
        """Return a JSON-serializable dict view of the manifest.

        ``hyperparameters`` is deep-copied to a plain dict so callers cannot
        mutate the frozen manifest's mapping by aliasing.
        """
        d = asdict(self)
        # ``asdict`` already deep-copies dict/list values; explicit cast for
        # MappingProxyType (which asdict leaves as-is).
        d["hyperparameters"] = dict(self.hyperparameters)
        return d

    @classmethod
    def from_dict(cls, payload: Mapping[str, Any]) -> "ProvenanceManifest":
        """Reverse of :meth:`to_dict` with schema validation."""
        validate(payload)
        return cls(
            schema_version=int(payload["schema_version"]),
            artifact_id=str(payload["artifact_id"]),
            code_sha=str(payload["code_sha"]),
            code_dirty=bool(payload["code_dirty"]),
            dataset_sha=str(payload["dataset_sha"]),
            dataset_size=int(payload["dataset_size"]),
            seed=int(payload["seed"]),
            hyperparameters=MappingProxyType(dict(payload["hyperparameters"])),
            parent_artifact_id=(
                None if payload["parent_artifact_id"] is None
                else str(payload["parent_artifact_id"])
            ),
            content_registry_sha=str(payload["content_registry_sha"]),
            onnx_opset_version=int(payload["onnx_opset_version"]),
            phase=int(payload["phase"]),
            step=int(payload["step"]),
            created_at_ns=int(payload["created_at_ns"]),
            host=str(payload["host"]),
            run_id=str(payload["run_id"]),
        )


def validate(payload: Mapping[str, Any]) -> None:
    """Raise ``ValueError`` if ``payload`` is not a valid v1 manifest.

    Checks:

    - All required fields are present.
    - Each field's runtime type matches the schema.
    - ``schema_version == 1`` (rejects future versions until a coordinated
      bump lands).
    - ``onnx_opset_version == 17`` (Q10-ADR-006 lock).
    - ``phase == 1`` (Q10-ADR-009 lock).
    """
    if not isinstance(payload, Mapping):
        raise ValueError(
            f"manifest payload must be a Mapping; got {type(payload).__name__}"
        )

    missing = [name for name, _ in _V1_REQUIRED_FIELDS if name not in payload]
    if missing:
        raise ValueError(
            f"manifest missing required field(s): {', '.join(missing)}"
        )

    for name, expected in _V1_REQUIRED_FIELDS:
        value = payload[name]
        # Special case: ``parent_artifact_id`` is ``str | None``.
        if name == "parent_artifact_id":
            if value is not None and not isinstance(value, str):
                raise ValueError(
                    f"manifest field {name!r}: expected str or None, "
                    f"got {type(value).__name__}"
                )
            continue
        # ``bool`` is a subclass of ``int`` in Python; reject the int-expected
        # field receiving a bool, but allow the bool-expected field.
        if expected is int and isinstance(value, bool):
            raise ValueError(
                f"manifest field {name!r}: expected int, got bool"
            )
        if not isinstance(value, expected):  # type: ignore[arg-type]
            type_name = (
                expected.__name__ if isinstance(expected, type)
                else " | ".join(t.__name__ for t in expected)
            )
            raise ValueError(
                f"manifest field {name!r}: expected {type_name}, "
                f"got {type(value).__name__}"
            )

    if int(payload["schema_version"]) != SCHEMA_VERSION:
        raise ValueError(
            f"manifest schema_version: expected {SCHEMA_VERSION}, "
            f"got {payload['schema_version']}"
        )
    if int(payload["onnx_opset_version"]) != 17:
        raise ValueError(
            "manifest onnx_opset_version: Phase-1 locked to 17 per "
            f"Q10-ADR-006; got {payload['onnx_opset_version']}"
        )
    if int(payload["phase"]) != 1:
        raise ValueError(
            f"manifest phase: Phase-1 locked to 1; got {payload['phase']}"
        )


__all__ = [
    "ProvenanceManifest",
    "SCHEMA_VERSION",
    "validate",
]

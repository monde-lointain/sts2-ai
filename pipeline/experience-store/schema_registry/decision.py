"""Validate-decision dataclasses for SchemaRegistry (S0.B.beta).

Returned by `SchemaRegistry.validate(version, op)` per spec
`modules/schema-registry.md` section "Internal communication":

- `Accept` — request proceeds.
- `Reject(reason, http_status, retry_after_sec=None, accepted=None)` —
  caller (IngestAPI/Sampler at W3) maps to HTTP error.

These dataclasses are imported across submodule boundaries: IngestAPI and
Sampler will `from schema_registry import Accept, Reject` to dispatch on
the decision type. This is normal in-process use of the spec-canonical
boundary; the no-shared-tables rule applies to persistent data, not to
typed-dataclass call boundaries (Q3-ADR-001).
"""

from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(frozen=True)
class Accept:
    """Validation passed; caller proceeds."""


@dataclass(frozen=True)
class Reject:
    """Validation refused; caller maps to HTTP error.

    Fields:
    - reason: machine-readable token from the spec's rule set
      ("schema_unknown", "schema_drain_stale", "schema_flip").
    - http_status: HTTP status code per Q3-ADR-006 (400/423/503).
    - retry_after_sec: hint for the Retry-After header on 503 (Sampler);
      None when not applicable.
    - accepted: list of currently-accepted (major, minor) tuples, for
      diagnostic feedback to the writer on a 400 schema_unknown. None on
      non-400 rejections.
    """

    reason: str
    http_status: int
    retry_after_sec: int | None = None
    accepted: list[tuple[int, int]] | None = None


# Public type alias for `validate(...) -> Decision`.
Decision = Accept | Reject

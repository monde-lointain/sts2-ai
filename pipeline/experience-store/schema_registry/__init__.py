"""Q3 SchemaRegistry submodule (S0.B.beta).

Single source-of-truth for accepted `(major, minor)` wire-schema versions.
Owns the drain<->flip migration FSM (Phase-1A: degenerate `open`-always).
See `pipeline/experience-store/docs/specs/modules/schema-registry.md`.
"""

from .decision import Accept, Decision, Reject
from .registry import SchemaRegistry

__all__ = ["Accept", "Decision", "Reject", "SchemaRegistry"]

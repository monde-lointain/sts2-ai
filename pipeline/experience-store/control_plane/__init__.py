"""Q3 ControlPlane submodule (S0.B.gamma).

Consolidates four cross-cutting concerns into one submodule per
Q3-internal spec section 4 number 8:

- ProvenanceLog: append-only NDJSON audit log per Q3-ADR-001
  single-writer invariant.
- RetentionPolicy: Phase-1 hot-tier thresholds (Q3-ADR-007) and
  sustained-pressure dual time-windowed predicate (Q3-ADR-008).
- MetricsEmitter: Prometheus text v0.0.4 emitter matching the line
  shape produced by pipeline/common/service_host.py:28-34.
- SidebandRouter: Phase-1 write-and-store stub for Q2 oracle-agreement
  payloads (Q3-ADR-004 mirror).

See pipeline/experience-store/docs/specs/modules/control-plane.md.
"""

from .observability import MetricsEmitter
from .provenance import ProvenanceLog
from .retention import RetentionPolicy
from .sideband import SidebandRouter

__all__ = [
    "MetricsEmitter",
    "ProvenanceLog",
    "RetentionPolicy",
    "SidebandRouter",
]

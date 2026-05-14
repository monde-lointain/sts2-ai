"""Q3 ControlPlane submodule (S0.B.gamma).

Consolidates four cross-cutting concerns into one submodule per
Q3-internal spec modules/control-plane.md:

- ProvenanceLog: append-only NDJSON audit log per Q3-ADR-001
  single-writer invariant. Spec name: ProvenanceIndex.
- RetentionController: Phase-1 hot-tier thresholds (Q3-ADR-007),
  sustained-pressure dual time-windowed predicate (Q3-ADR-008), and
  four-state pressure classification (Pressure enum). `RetentionPolicy`
  re-exported as a one-wave backward-compat alias.
- MetricsEmitter: Prometheus text v0.0.4 emitter matching the line
  shape produced by pipeline/common/service_host.py:28-34.
- SidebandRouter: Phase-1 write-and-store stub for Q2 oracle-agreement
  payloads (Q3-ADR-004 mirror).

See pipeline/experience-store/docs/specs/modules/control-plane.md.
"""

from .observability import MetricsEmitter
from .provenance import ProvenanceLog
from .retention import Pressure, RetentionController, RetentionPolicy
from .sideband import SidebandRouter

__all__ = [
    "MetricsEmitter",
    "Pressure",
    "ProvenanceLog",
    "RetentionController",
    "RetentionPolicy",
    "SidebandRouter",
]

"""Q3 trajectory.proto v1 (sts2.q3.v1) Python bindings.

Generated via `protoc --python_out=` from
`contracts/schemas/trajectory/trajectory.proto`. Hand-rolled here per
Q3 S0.A directive (Q2-ADR-001 §4 LOCATION precedent): bindings live
inside the quantum surface, not in `contracts/generated/python/...`,
to avoid coupling Q3 boot to the empty-stub codegen pipeline.
"""

from .trajectory_pb2 import (
    CombatOutcomeSample,
    CombatOutcomeSummary,
    DecisionType,
    MacroContext,
    ObservabilityRegime,
    ResourceDeltas,
    RewardContext,
    SchemaVersion,
    Trajectory,
    TrajectoryStep,
)

__all__ = [
    "CombatOutcomeSample",
    "CombatOutcomeSummary",
    "DecisionType",
    "MacroContext",
    "ObservabilityRegime",
    "ResourceDeltas",
    "RewardContext",
    "SchemaVersion",
    "Trajectory",
    "TrajectoryStep",
]

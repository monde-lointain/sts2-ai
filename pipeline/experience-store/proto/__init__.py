"""Q3 trajectory.proto v1 (sts2.q3.v1) Python bindings — re-export shim.

Backward-compat shim: canonical bindings live at
`pipeline.common.trajectory_proto` (lifted per Q10-ADR-005 to enable
cross-quantum reuse without coupling on Q3 surface). Q3 consumers keep
importing from this module unchanged; class identities are preserved
(same class objects as the canonical module).
"""

import sys as _sys
from pathlib import Path as _Path

# Q3's experience-store conftest adds experience-store/ to sys.path so this
# package imports as a top-level `proto`. To reach the canonical lift at
# `pipeline.common.trajectory_proto`, ensure the project root (3 levels up:
# proto/ -> experience-store/ -> pipeline/ -> root) is on sys.path.
_PROJECT_ROOT = str(_Path(__file__).resolve().parents[3])
if _PROJECT_ROOT not in _sys.path:
    _sys.path.insert(0, _PROJECT_ROOT)

from pipeline.common.trajectory_proto import (  # noqa: E402
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

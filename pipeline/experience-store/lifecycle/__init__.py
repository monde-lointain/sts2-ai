"""Q3 Lifecycle submodule (S0.C.gamma).

Tiering engine; Phase-1A scope is age-only drop on Overflow / Sustained
pressure (cold tier disabled). Background thread runs `lifecycle_tick`
on a fixed interval; spec-driven semantics live in
``pipeline/experience-store/docs/specs/modules/lifecycle.md``.

Phase-2+ promote-then-drop is deliberately stubbed (``force_promote``
raises NotImplementedError; ``promoted_rows_total`` stays 0).
"""

from .audit import AuditLog
from .lifecycle import Lifecycle, TickResult
from .policy import LifecyclePolicy

__all__ = [
    "AuditLog",
    "Lifecycle",
    "LifecyclePolicy",
    "TickResult",
]

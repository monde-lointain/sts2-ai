"""Q3 Sampler submodule (S0.C.beta, Phase-1A).

Read-path for Q10 trainer / Q12 evaluation-harness. Phase-1A scope:
uniform mode only; prioritized + stratified + cold-tier reads are
Phase-2+. See `pipeline/experience-store/docs/specs/modules/sampler.md`.

Owns no persistent state; transient cursor LRU per Phase-1 cap of 1024.
"""

from .api import Sampler
from .cursor import CursorCache, CursorState
from .framing import is_degenerate_combat_sample

__all__ = [
    "CursorCache",
    "CursorState",
    "Sampler",
    "is_degenerate_combat_sample",
]

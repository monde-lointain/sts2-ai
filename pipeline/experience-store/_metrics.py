"""Re-export shim — canonical location is pipeline/common/prometheus.py.

Ensures the project root is on `sys.path` so `pipeline.common.prometheus`
resolves under test environments that only inject `pipeline/experience-store/`
(via `conftest.py`).
"""

import sys as _sys
from pathlib import Path as _Path

_PROJECT_ROOT = str(_Path(__file__).resolve().parents[2])
if _PROJECT_ROOT not in _sys.path:
    _sys.path.insert(0, _PROJECT_ROOT)

from pipeline.common.prometheus import PrometheusLineBuilder  # noqa: E402

__all__ = ["PrometheusLineBuilder"]

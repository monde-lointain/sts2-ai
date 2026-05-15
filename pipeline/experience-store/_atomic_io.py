"""Re-export shim — canonical location is pipeline/common/atomic_io.py.

Ensures the project root is on `sys.path` so `pipeline.common.atomic_io`
resolves under test environments that only inject `pipeline/experience-store/`
(via `conftest.py`). `os` is re-exported so tests that monkey-patch
`_atomic_io.os.fsync` keep working (they patch the shared `os` module
object).
"""
import os  # noqa: F401  # re-exported for monkey-patching by tests
import sys as _sys
from pathlib import Path as _Path

_PROJECT_ROOT = str(_Path(__file__).resolve().parents[2])
if _PROJECT_ROOT not in _sys.path:
    _sys.path.insert(0, _PROJECT_ROOT)

from pipeline.common.atomic_io import atomic_write_json  # noqa: E402,F401

__all__ = ["atomic_write_json"]

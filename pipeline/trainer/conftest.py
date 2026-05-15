"""Test bootstrap: ensure repo root is on sys.path for package imports.

pytest's rootdir does not auto-add project root; this adds it explicitly so
`pipeline.trainer.*` and `pipeline.common.*` imports resolve in tests.
"""
import sys
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

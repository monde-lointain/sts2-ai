"""Re-export shim — canonical location is pipeline/common/framing.py.

Ensures the project root is on `sys.path` so `pipeline.common.framing`
resolves under test environments that only inject `pipeline/experience-store/`
(via `conftest.py`).
"""
import sys as _sys
from pathlib import Path as _Path

_PROJECT_ROOT = str(_Path(__file__).resolve().parents[2])
if _PROJECT_ROOT not in _sys.path:
    _sys.path.insert(0, _PROJECT_ROOT)

from pipeline.common.framing import (  # noqa: E402,F401
    FramingError,
    decode_varint,
    encode_frame,
    encode_frames,
    encode_varint,
    frame_payload,
    iter_frames,
    parse_frames,
)

__all__ = [
    "FramingError",
    "decode_varint",
    "encode_frame",
    "encode_frames",
    "encode_varint",
    "frame_payload",
    "iter_frames",
    "parse_frames",
]

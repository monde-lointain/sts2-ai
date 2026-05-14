"""Q3 IngestAPI submodule (S0.C.alpha).

Write-path HTTP front door per
`pipeline/experience-store/docs/specs/modules/ingest-api.md`.
"""

from .api import IngestAPI
from .framing import FramingError, encode_frame, encode_frames, parse_frames

__all__ = [
    "FramingError",
    "IngestAPI",
    "encode_frame",
    "encode_frames",
    "parse_frames",
]

"""Length-delimited protobuf frame parser for the IngestAPI write path.

The canonical implementation lives in ``pipeline/experience-store/_framing.py``
(shared with the Sampler). This module re-exports the IngestAPI-facing
names for backward-compatibility with ``ingest_api/__init__.py`` and
existing call sites in ``ingest_api/api.py``.
"""

from __future__ import annotations

from _framing import (
    FramingError,
    encode_frame,
    encode_frames,
    encode_varint,
    decode_varint,
    iter_frames,
    parse_frames,
)

__all__ = [
    "FramingError",
    "encode_frame",
    "encode_frames",
    "encode_varint",
    "decode_varint",
    "iter_frames",
    "parse_frames",
]

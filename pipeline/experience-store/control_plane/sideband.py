"""Phase-1 oracle-agreement sideband router stub (Q3-ADR-004).

Phase-1: receive payloads from Q2 and persist as NDJSON for audit.
Phase-2+: forward into PriorityIndex (Q3-ADR-010). Forwarding is out
of scope at S0; the stub keeps the durability surface owned by Q3 so
Q2 has a stable target to integrate against.

See pipeline/experience-store/docs/specs/modules/control-plane.md
(SidebandRouter section).
"""

from __future__ import annotations

import json
import os
import pathlib

SIDEBAND_DIR = "sideband"
SIDEBAND_FILE = "oracle.ndjson"


class SidebandRouter:
    """Append-only NDJSON landing for oracle-agreement payloads.

    Caller (IngestAPI / HTTP handler at W4) is responsible for payload
    shape validation; this stub records bytes-faithfully so audit can
    reconstruct the Q2 stream exactly.
    """

    def __init__(self, data_dir: pathlib.Path) -> None:
        self._data_dir = pathlib.Path(data_dir)
        self._path = self._data_dir / SIDEBAND_DIR / SIDEBAND_FILE
        self._path.parent.mkdir(parents=True, exist_ok=True)
        self._path.touch(exist_ok=True)

    @property
    def path(self) -> pathlib.Path:
        return self._path

    def record_oracle_agreement(self, payload: dict) -> None:
        """Append `payload` as one NDJSON line; fsync before returning."""
        line = json.dumps(payload, ensure_ascii=False, sort_keys=True) + "\n"
        encoded = line.encode("utf-8")
        fd = os.open(self._path, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o644)
        try:
            os.write(fd, encoded)
            os.fsync(fd)
        finally:
            os.close(fd)

    def count(self) -> int:
        """Return the number of records persisted (line count)."""
        n = 0
        with self._path.open("r", encoding="utf-8") as handle:
            for line in handle:
                if line.strip():
                    n += 1
        return n

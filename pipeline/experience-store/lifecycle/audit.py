"""Append-only NDJSON lifecycle audit log with 10 MiB rotation.

Per ``modules/lifecycle.md`` lines 44-49 + 49: record shape is
``{"ts_ns", "action", "until_ts_ns", "rows", "bytes", "reason"}``;
rotation fires at 10 MiB and retains the 10 most-recent rotated files.

Rotation scheme: ``audit.ndjson`` is the live file; when its size meets
or exceeds the rotation threshold, it is renamed to
``audit.ndjson.<unix_ts_ns>`` and a fresh empty file is created. The 10
newest rotated files are kept (by mtime); older ones are deleted.
"""

from __future__ import annotations

import json
import os
import pathlib
import time
from typing import Any

AUDIT_FILE = "audit.ndjson"

# Spec line 49.
DEFAULT_ROTATE_AT_BYTES = 10 * 1024 * 1024
DEFAULT_MAX_ROTATED_FILES = 10


class AuditLog:
    """Append-only NDJSON audit log; rotates at 10 MiB, keeps last 10 files.

    Single-process callers only (Q3 monolith). Each append opens the file,
    writes one line, fsyncs, and closes; rotation is checked synchronously
    after the write so the live file never exceeds the threshold by more
    than one record.
    """

    def __init__(
        self,
        data_dir: pathlib.Path,
        rotate_at_bytes: int = DEFAULT_ROTATE_AT_BYTES,
        max_rotated_files: int = DEFAULT_MAX_ROTATED_FILES,
    ) -> None:
        self._data_dir = pathlib.Path(data_dir)
        self._log_dir = self._data_dir / "lifecycle"
        self._log_dir.mkdir(parents=True, exist_ok=True)
        self._path = self._log_dir / AUDIT_FILE
        # Touch so a "read with zero ticks" returns []/empty.
        self._path.touch(exist_ok=True)
        if rotate_at_bytes <= 0:
            raise ValueError("rotate_at_bytes must be > 0")
        if max_rotated_files <= 0:
            raise ValueError("max_rotated_files must be > 0")
        self._rotate_at_bytes = int(rotate_at_bytes)
        self._max_rotated_files = int(max_rotated_files)

    @property
    def path(self) -> pathlib.Path:
        return self._path

    @property
    def directory(self) -> pathlib.Path:
        return self._log_dir

    def append(self, record: dict[str, Any]) -> None:
        """Serialize + append one record; fsync before returning; rotate if oversized."""
        line = json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n"
        encoded = line.encode("utf-8")
        fd = os.open(self._path, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o644)
        try:
            os.write(fd, encoded)
            os.fsync(fd)
        finally:
            os.close(fd)
        # Check after write so the live file is bounded post-write.
        try:
            if self._path.stat().st_size >= self._rotate_at_bytes:
                self._rotate()
        except FileNotFoundError:
            # Defensive: someone deleted the live file between write+stat.
            return

    def _rotate(self) -> None:
        """Move ``audit.ndjson`` to a timestamped sibling, prune oldest survivors."""
        stamp = time.time_ns()
        rotated = self._log_dir / f"{AUDIT_FILE}.{stamp}"
        # If two appends rotate within the same nanosecond (vanishingly
        # rare; tests force this by mocking time), bump until unique.
        while rotated.exists():
            stamp += 1
            rotated = self._log_dir / f"{AUDIT_FILE}.{stamp}"
        try:
            self._path.replace(rotated)
        except FileNotFoundError:
            # Concurrent prune raced us; nothing to do.
            return
        # Re-create the live file so subsequent appends find it.
        self._path.touch(exist_ok=True)
        self._prune_old_rotated()

    def _prune_old_rotated(self) -> None:
        rotated = sorted(
            self._log_dir.glob(f"{AUDIT_FILE}.*"),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )
        for stale in rotated[self._max_rotated_files :]:
            try:
                stale.unlink()
            except FileNotFoundError:
                continue

    def rotated_files(self) -> list[pathlib.Path]:
        """Newest-first list of currently-retained rotated files."""
        return sorted(
            self._log_dir.glob(f"{AUDIT_FILE}.*"),
            key=lambda p: p.stat().st_mtime,
            reverse=True,
        )

    def read_all(self) -> list[dict[str, Any]]:
        """Return all records from the live file in append order. Phase-1A only."""
        records: list[dict[str, Any]] = []
        with self._path.open("r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                records.append(json.loads(line))
        return records

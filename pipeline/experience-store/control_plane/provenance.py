"""Append-only NDJSON provenance log (Q3-ADR-001 single-writer invariant).

One record per accepted trajectory; fsync per write so an unclean shutdown
loses at most the in-flight write. Caller (IngestAPI in W3) holds the
single-writer responsibility; this module performs no in-process locking.

The class is named `ProvenanceLog` (implementation name); the Q3-internal
spec uses the conceptual name `ProvenanceIndex` for the same responsibility
(see modules/control-plane.md, ProvenanceIndex section).
"""

from __future__ import annotations

import json
import os
import pathlib

PROVENANCE_FILE = "provenance.ndjson"


class ProvenanceLog:
    """Append-only NDJSON log of trajectory provenance.

    Each line is a UTF-8 JSON object with shape (per
    docs/specs/modules/control-plane.md lines 38-42):
        {"trajectory_id": str, "model_version": str, "sampling_mode": str,
         "generator": str, "ingest_ts_ns": int,
         "schema_major": int, "schema_minor": int}

    `schema_major` and `schema_minor` are emitted as flat fields, NOT as
    a nested `schema_version` object. The `append(...)` API takes a tuple
    `schema_version=(major, minor)` for callsite ergonomics, then
    serializes it flat.
    """

    def __init__(self, data_dir: pathlib.Path) -> None:
        self._data_dir = pathlib.Path(data_dir)
        self._data_dir.mkdir(parents=True, exist_ok=True)
        self._path = self._data_dir / PROVENANCE_FILE
        # Touch the file so query_recent on an empty log returns [].
        self._path.touch(exist_ok=True)

    @property
    def path(self) -> pathlib.Path:
        return self._path

    def append(
        self,
        trajectory_id: str,
        model_version: str,
        sampling_mode: str,
        generator: str,
        ingest_ts_ns: int,
        schema_version: tuple[int, int],
    ) -> None:
        """Append one provenance record; fsync before returning.

        Signature matches spec lines 93-95. `schema_version` is a
        `(major, minor)` tuple serialized as flat `schema_major` and
        `schema_minor` fields per the spec NDJSON shape (lines 38-42).

        Raises ValueError if any of model_version, sampling_mode, or
        generator is empty (Q3-ADR-001 mandates non-droppable provenance;
        an empty critical field would silently poison the audit log).
        """
        if not model_version:
            raise ValueError("model_version must not be empty")
        if not sampling_mode:
            raise ValueError("sampling_mode must not be empty")
        if not generator:
            raise ValueError("generator must not be empty")

        schema_major, schema_minor = schema_version
        payload = {
            "trajectory_id": str(trajectory_id),
            "model_version": str(model_version),
            "sampling_mode": str(sampling_mode),
            "generator": str(generator),
            "ingest_ts_ns": int(ingest_ts_ns),
            "schema_major": int(schema_major),
            "schema_minor": int(schema_minor),
        }
        line = json.dumps(payload, ensure_ascii=False, sort_keys=True) + "\n"
        encoded = line.encode("utf-8")
        # Open-append-fsync-close per write keeps the audit log durable
        # under crash without holding an FD across calls.
        fd = os.open(self._path, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o644)
        try:
            os.write(fd, encoded)
            os.fsync(fd)
        finally:
            os.close(fd)

    def lookup(self, trajectory_id: str) -> dict | None:
        """Return the first matching provenance row for `trajectory_id`, or None.

        Phase-1 implementation: linear scan of provenance.ndjson. Phase-2+
        will replace with the Bloom-filter accelerated lookup per spec
        line 46. Sampler enrichment (P2+) is the primary caller.
        """
        target = str(trajectory_id)
        with self._path.open("r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                row = json.loads(line)
                if row.get("trajectory_id") == target:
                    return row
        return None

    def query_recent(self, n: int) -> list[dict]:
        """Return the last `n` records in append order, deserialized.

        Phase-1 implementation: full-file scan. Acceptable up to ~100k
        records per the spec's testing strategy (#2); rotation comes
        later.
        """
        if n <= 0:
            return []
        records: list[dict] = []
        with self._path.open("r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                records.append(json.loads(line))
        return records[-n:]

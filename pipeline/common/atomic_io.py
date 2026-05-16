"""Shared atomic JSON write helper.

Centralizes the tempfile + fsync + os.replace pattern previously
hand-rolled in multiple service modules. Exposes a single public
helper.

Callers MUST ensure ``path.parent`` already exists; this helper does
not mkdir.
"""

from __future__ import annotations

import contextlib
import json
import os
import pathlib
import tempfile


def atomic_write_json(
    path: pathlib.Path,
    payload: dict,
    *,
    indent: int | None = 2,
    sort_keys: bool = True,
    fsync: bool = True,
    ensure_ascii: bool = False,
) -> None:
    """Atomically write ``payload`` as JSON to ``path``.

    Uses a same-directory ``NamedTemporaryFile`` so the final
    ``os.replace`` is atomic on POSIX. Optionally fsyncs the file
    contents before rename. On any ``BaseException`` (including
    encode failures), the temp file is best-effort unlinked before
    re-raising.

    ``path.parent`` is assumed to exist; the caller is responsible
    for mkdir.
    """
    tmp = tempfile.NamedTemporaryFile(  # noqa: SIM115 — manual close for atomic rename
        mode="w",
        encoding="utf-8",
        dir=path.parent,
        prefix=f".{path.stem}.",
        suffix=".tmp",
        delete=False,
    )
    try:
        json.dump(
            payload,
            tmp,
            indent=indent,
            sort_keys=sort_keys,
            ensure_ascii=ensure_ascii,
        )
        tmp.write("\n")
        if fsync:
            tmp.flush()
            os.fsync(tmp.fileno())
        tmp.close()
        os.replace(tmp.name, path)
    except BaseException:
        with contextlib.suppress(FileNotFoundError):
            os.unlink(tmp.name)
        raise

"""Q3 HotStore submodule.

Phase-1 owner of the RocksDB hot-tier instance; single-shard, in-process.
See `pipeline/experience-store/docs/specs/modules/hot-store.md`.
"""

from .store import HotStore

__all__ = ["HotStore"]

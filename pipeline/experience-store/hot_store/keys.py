"""HotStore key encoding/decoding (Q3-ADR-002 traj/by_id CFs).

Per `docs/specs/modules/hot-store.md`:
- `traj` CF key: 24-byte fixed
  `(ingest_ts_ns: uint64 big-endian, trajectory_id: bytes16 random)`.
  Big-endian on ts makes lex-order = time-order, so RocksDB iteration
  yields chronological order (supports age-drop range scans and
  ts-bucketed uniform sampling).
- `by_id` CF key: 16-byte `trajectory_id`. Value: 8-byte big-endian
  `ingest_ts_ns`.

Everything is `bytes`; never `str` and never int directly. Encoding is
fixed-width so range bounds are constructible without ambiguity.
"""

from __future__ import annotations

TRAJ_ID_LEN = 16
TS_LEN = 8
TRAJ_KEY_LEN = TS_LEN + TRAJ_ID_LEN  # 24


def encode_traj_key(ingest_ts_ns: int, trajectory_id: bytes) -> bytes:
    """Encode the `traj` CF key as (u64be ts, 16-byte id)."""
    if not isinstance(trajectory_id, (bytes, bytearray)) or len(trajectory_id) != TRAJ_ID_LEN:
        raise ValueError(
            f"trajectory_id must be exactly {TRAJ_ID_LEN} bytes; got "
            f"{type(trajectory_id).__name__} of len "
            f"{len(trajectory_id) if hasattr(trajectory_id, '__len__') else 'N/A'}"
        )
    if ingest_ts_ns < 0 or ingest_ts_ns >= 1 << 64:
        raise ValueError(f"ingest_ts_ns out of uint64 range: {ingest_ts_ns}")
    return ingest_ts_ns.to_bytes(TS_LEN, "big") + bytes(trajectory_id)


def decode_traj_key(key: bytes) -> tuple[int, bytes]:
    """Inverse of `encode_traj_key`."""
    if len(key) != TRAJ_KEY_LEN:
        raise ValueError(f"traj key must be {TRAJ_KEY_LEN} bytes; got {len(key)}")
    ts = int.from_bytes(key[:TS_LEN], "big")
    tid = bytes(key[TS_LEN:])
    return ts, tid


def encode_ts(ingest_ts_ns: int) -> bytes:
    """Encode `ingest_ts_ns` as u64 big-endian (used as `by_id` value)."""
    if ingest_ts_ns < 0 or ingest_ts_ns >= 1 << 64:
        raise ValueError(f"ingest_ts_ns out of uint64 range: {ingest_ts_ns}")
    return ingest_ts_ns.to_bytes(TS_LEN, "big")


def decode_ts(b: bytes) -> int:
    if len(b) != TS_LEN:
        raise ValueError(f"ts blob must be {TS_LEN} bytes; got {len(b)}")
    return int.from_bytes(b, "big")


def traj_range_lo(ts_ns: int) -> bytes:
    """Lower bound (inclusive) of `traj` keys whose ts >= `ts_ns`."""
    return encode_ts(ts_ns) + b"\x00" * TRAJ_ID_LEN


def traj_range_hi(ts_ns: int) -> bytes:
    """Upper bound (exclusive) of `traj` keys whose ts < `ts_ns`."""
    return encode_ts(ts_ns) + b"\x00" * TRAJ_ID_LEN

"""HotStore module-local tests (S0.B.α verification step 2)."""

from __future__ import annotations

import os
import random

import pytest

from hot_store import HotStore
from hot_store.keys import TRAJ_ID_LEN


def _payload(rng: random.Random, lo_b: int = 1024, hi_b: int = 10240) -> bytes:
    n = rng.randint(lo_b, hi_b)
    return rng.randbytes(n)


@pytest.fixture
def store(tmp_path):
    db_dir = tmp_path / "hot" / "rocksdb"
    s = HotStore(db_dir)
    try:
        yield s
    finally:
        s.close()


def _seed_n(store: HotStore, n: int, payload_seed: int = 0xC0FFEE) -> list[tuple[bytes, bytes]]:
    """Append n trajectories with varied 1KB-10KB payloads. Returns [(id, bytes)]."""
    rng = random.Random(payload_seed)
    out: list[tuple[bytes, bytes]] = []
    for _ in range(n):
        blob = _payload(rng)
        tid = store.append_new(blob)
        out.append((tid, blob))
    return out


# ---------- count + size ----------


def test_append_100_count_and_size(store: HotStore) -> None:
    _seed_n(store, 100)
    assert store.count() == 100
    assert store.size_bytes() > 0
    # crude lower bound: 100 payloads * 1024 bytes minimum
    assert store.size_bytes() >= 100 * 1024 / 2  # account for compression


def test_count_empty(store: HotStore) -> None:
    assert store.count() == 0
    assert store.size_bytes() >= 0


# ---------- sample_uniform ----------


def test_sample_uniform_returns_n_members(store: HotStore) -> None:
    rows = _seed_n(store, 100)
    blobs = {b for _, b in rows}
    sample = store.sample_uniform(20, seed=42)
    assert len(sample) == 20
    assert all(isinstance(b, bytes) for b in sample)
    assert all(b in blobs for b in sample)


def test_sample_uniform_is_deterministic_same_state(store: HotStore) -> None:
    _seed_n(store, 100)
    s1 = store.sample_uniform(20, seed=42)
    s2 = store.sample_uniform(20, seed=42)
    assert s1 == s2


def test_sample_uniform_state_change_invalidates_determinism(store: HotStore) -> None:
    rows = _seed_n(store, 100)
    store.sample_uniform(20, seed=42)
    # mutate store (101st append)
    new_blob = b"\xff" * 5000
    store.append_new(new_blob)
    s2 = store.sample_uniform(20, seed=42)
    # second result remains a 20-member subset of current 101
    current_blobs = {b for _, b in rows} | {new_blob}
    assert len(s2) == 20
    assert all(b in current_blobs for b in s2)
    # NOTE: s1 may or may not equal s2 — the key set changed, so the sort
    # order may shift one extra element in; we only assert membership.


def test_sample_uniform_different_seeds_differ(store: HotStore) -> None:
    _seed_n(store, 100)
    s1 = store.sample_uniform(20, seed=1)
    s2 = store.sample_uniform(20, seed=2)
    assert s1 != s2  # extremely high probability


def test_sample_uniform_empty_store_returns_empty(store: HotStore) -> None:
    assert store.sample_uniform(10, seed=0) == []


def test_sample_uniform_zero_returns_empty(store: HotStore) -> None:
    _seed_n(store, 10)
    assert store.sample_uniform(0, seed=0) == []


def test_sample_uniform_more_than_count_returns_all(store: HotStore) -> None:
    _seed_n(store, 5)
    out = store.sample_uniform(100, seed=0)
    assert len(out) == 5


# ---------- drop_oldest_age_first ----------


def test_drop_oldest_age_first_drops_oldest(store: HotStore) -> None:
    rows = _seed_n(store, 100)
    ids_in_insert_order = [tid for tid, _ in rows]
    before = store.count()
    sz_before = store.size_bytes()
    assert before == 100
    target = sz_before // 2
    dropped = store.drop_oldest_age_first(target_bytes=target)
    assert dropped > 0
    after = store.count()
    assert after == before - dropped
    # The dropped rows MUST be the oldest by insert order (== ts order).
    # Verify by checking that the first `dropped` ids are gone and the
    # remaining `after` ids are still present.
    for tid in ids_in_insert_order[:dropped]:
        assert store.read(tid) is None
    for tid in ids_in_insert_order[dropped:]:
        assert store.read(tid) is not None


def test_drop_oldest_age_first_zero_target_no_op(store: HotStore) -> None:
    _seed_n(store, 10)
    assert store.drop_oldest_age_first(0) == 0
    assert store.count() == 10


def test_drop_oldest_age_first_huge_target_drops_all(store: HotStore) -> None:
    _seed_n(store, 10)
    dropped = store.drop_oldest_age_first(target_bytes=10**12)
    assert dropped == 10
    assert store.count() == 0


# ---------- reopen persistence ----------


def test_reopen_preserves_data(tmp_path) -> None:
    db_dir = tmp_path / "hot" / "rocksdb"
    s1 = HotStore(db_dir)
    rows = _seed_n(s1, 50)
    s1.close()
    s2 = HotStore(db_dir)
    try:
        assert s2.count() == 50
        for tid, blob in rows:
            assert s2.read(tid) == blob
    finally:
        s2.close()


# ---------- column-family invariants ----------


def test_all_four_cfs_created_at_open(tmp_path) -> None:
    """Q3-ADR-002 + Q3-ADR-010: traj, by_id, step_idx, priority all exist."""
    from rocksdict import Rdict

    db_dir = tmp_path / "hot" / "rocksdb"
    s = HotStore(db_dir)
    s.close()
    cf_names = set(Rdict.list_cf(str(db_dir)))
    assert {"default", "traj", "by_id", "step_idx", "priority"}.issubset(cf_names)


def test_step_idx_and_priority_remain_empty_after_appends(tmp_path) -> None:
    """HotStore code never writes to step_idx/priority CFs."""
    from rocksdict import Options, Rdict

    db_dir = tmp_path / "hot" / "rocksdb"
    s = HotStore(db_dir)
    _seed_n(s, 25)
    s.close()
    # reopen with all CFs to inspect step_idx + priority
    opts = Options(raw_mode=True)
    opts.create_if_missing(False)
    cf_opts = {n: Options(raw_mode=True) for n in ("traj", "by_id", "step_idx", "priority")}
    db = Rdict(str(db_dir), opts, column_families=cf_opts)
    try:
        for cf_name in ("step_idx", "priority"):
            cf = db.get_column_family(cf_name)
            it = cf.iter()
            it.seek_to_first()
            assert not it.valid(), f"CF {cf_name} expected empty; has entries"
    finally:
        db.close()


# ---------- spec API smoke ----------


def test_spec_api_append_returns_ts_and_scan_orders(store: HotStore) -> None:
    tids: list[bytes] = []
    ts_list: list[int] = []
    for i in range(20):
        tid = os.urandom(TRAJ_ID_LEN)
        ts = store.append(b"row-" + str(i).encode(), tid)
        tids.append(tid)
        ts_list.append(ts)
    # ts strictly monotonic
    assert ts_list == sorted(ts_list)
    assert len(set(ts_list)) == len(ts_list)
    # scan from 0 returns all in insert order
    out = list(store.scan(after_ts_ns=0, limit=1000))
    assert len(out) == 20
    seen_ts = [ts for ts, _, _ in out]
    assert seen_ts == sorted(seen_ts)
    # scan honors limit
    out2 = list(store.scan(after_ts_ns=0, limit=5))
    assert len(out2) == 5


def test_read_missing_returns_none(store: HotStore) -> None:
    assert store.read(b"\x00" * TRAJ_ID_LEN) is None
    # also bad-length input returns None (per spec "never raises on miss")
    assert store.read(b"too-short") is None


def test_delete_range_atomic_across_cfs(store: HotStore) -> None:
    _seed_n(store, 30)
    # find the ts of the 10th row to use as cutoff
    out = list(store.scan(after_ts_ns=0, limit=15))
    cutoff_ts = out[10][0]
    # delete [0, cutoff_ts)
    deleted = store.delete_range(cutoff_ts)
    assert deleted == 10
    assert store.count() == 20
    # the deleted ids' by_id entries must be gone too
    deleted_tids = {tid for _, tid, _ in out[:10]}
    for tid in deleted_tids:
        assert store.read(tid) is None

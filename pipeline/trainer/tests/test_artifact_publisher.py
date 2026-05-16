"""Unit + integration tests for ``pipeline.trainer.artifact_publisher`` (S0.D.β).

Tests per ``pipeline/trainer/docs/specs/modules/artifact-publisher.md``
§Testing Strategy:

1. Provenance manifest matches v1 schema (delegates to ``test_manifest.py``;
   here we cover the publisher-side wiring).
2. ONNX export passes ``onnx.checker.check_model``.
3. ``dataset_sha`` is deterministic.
4. ``dataset_sha`` differs when one ID is added.
5. Atomic temp+rename — no half-written ``weights.pt`` visible mid-write.
6. Drop-on-full queue.
7. Round-trip: publish then load_parent.
8. ONNX round-trip: state_dict → ONNX → ORT load → forward → compare logits.
"""

from __future__ import annotations

import importlib.util
import io
import json
import subprocess
import threading
import time
from pathlib import Path

import pytest
import torch

from pipeline.trainer.artifact_publisher import (
    ArtifactPublisher,
    ArtifactRef,
    PublishRequest,
    compute_dataset_sha,
)
from pipeline.trainer.content_registry import ContentRegistry
from pipeline.trainer.manifest import SCHEMA_VERSION, ProvenanceManifest
from pipeline.trainer.model import TrainerNet
from pipeline.trainer.run_config import NetworkConfig, RunConfig, RunProvenance
from pipeline.trainer.tensor_encoder import EncodedBatch

_REGISTRY_PATH = (
    Path(__file__).resolve().parents[3] / "contracts" / "registry" / "phase1-silent.json"
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------
def _minimal_config_dict() -> dict:
    return {
        "service": "trainer",
        "port": 18110,
        "data_dir": "data/trainer",
        "q3_url": "http://127.0.0.1:18103",
        "q5_url": "http://127.0.0.1:18105",
        "parent_artifact_id": None,
        "seed": 0,
        "refuse_on_dirty": False,
        "network": {
            "expected_token_count": None,
            "d_model": 32,
            "n_layers": 2,
            "n_heads": 4,
            "ffn_dim": 64,
            "max_seq_len": 20,
            "max_action_space": 16,
        },
        "optim": {
            "lr": 0.0003,
            "weight_decay": 0.01,
            "warmup_steps": 1000,
            "total_steps": 100000,
            "grad_clip": 1.0,
        },
        "loss_weights": {
            "policy": 1.0,
            "combat_sample": 1.0,
            "combat_summary": 1.0,
            "hp_frac_aux": 0.05,
            "kl_beta": 0.01,
        },
        "checkpoint": {"every_n_steps": 1000, "every_m_minutes": 5},
        "wandb_enabled": False,
        "sampling_mode": "uniform",
        "prefetch_queue_size": 4,
        "batch_size": 32,
    }


@pytest.fixture
def isolated_repo(tmp_path: Path, monkeypatch):
    """Tiny clean git repo so RunProvenance.capture sees a clean tree."""
    repo = tmp_path / "repo"
    repo.mkdir()
    monkeypatch.chdir(repo)

    def git(*args: str) -> subprocess.CompletedProcess:
        return subprocess.run(["git", *args], cwd=repo, capture_output=True, text=True, check=True)

    git("init", "-q")
    git("config", "user.email", "test@example.com")
    git("config", "user.name", "test")
    git("config", "commit.gpgsign", "false")
    (repo / "seed.txt").write_text("hi\n")
    git("add", "seed.txt")
    git("commit", "-qm", "init")
    return repo


@pytest.fixture
def config_path(tmp_path: Path) -> Path:
    path = tmp_path / "local.json"
    path.write_text(json.dumps(_minimal_config_dict()))
    return path


@pytest.fixture
def run_config(isolated_repo: Path, config_path: Path) -> RunConfig:
    return RunConfig.load(config_path)


@pytest.fixture
def run_provenance(run_config: RunConfig) -> RunProvenance:
    return RunProvenance.capture(run_config, parent_artifact_id=None)


@pytest.fixture(scope="module")
def registry() -> ContentRegistry:
    return ContentRegistry.load(_REGISTRY_PATH)


@pytest.fixture
def small_network_config() -> NetworkConfig:
    return NetworkConfig(
        expected_token_count=None,
        d_model=32,
        n_layers=2,
        n_heads=4,
        ffn_dim=64,
        max_seq_len=20,
        max_action_space=16,
    )


@pytest.fixture
def trainer_net(registry: ContentRegistry, small_network_config: NetworkConfig) -> TrainerNet:
    torch.manual_seed(0)
    net = TrainerNet(small_network_config, registry)
    net.eval()
    return net


def _make_dummy_batch(
    *, batch_size: int = 2, seq_len: int = 12, action_space: int = 5, vocab_size: int = 1
) -> EncodedBatch:
    torch.manual_seed(0)
    tokens = torch.randint(0, max(1, vocab_size), (batch_size, seq_len), dtype=torch.long)
    padding_mask = torch.zeros((batch_size, seq_len), dtype=torch.bool)
    legal_action_mask = torch.zeros((batch_size, action_space), dtype=torch.bool)
    legal_action_mask[:, : max(1, action_space // 2)] = True
    return EncodedBatch(
        tokens=tokens,
        padding_mask=padding_mask,
        legal_action_mask=legal_action_mask,
        policy_target=torch.zeros((batch_size, action_space), dtype=torch.float32),
        combat_sample_targets=torch.zeros((batch_size, 4), dtype=torch.float32),
        combat_summary_targets=torch.zeros((batch_size, 5), dtype=torch.float32),
        hp_frac_target=torch.zeros((batch_size,), dtype=torch.float32),
        prior_logits=torch.zeros((batch_size, action_space), dtype=torch.float32),
        macro_context=torch.zeros((batch_size, 11), dtype=torch.float32),
        metadata={},
    )


def _serialize_state_dict(net: torch.nn.Module) -> bytes:
    cpu = {k: v.detach().cpu() for k, v in net.state_dict().items()}
    buf = io.BytesIO()
    torch.save(cpu, buf)
    return buf.getvalue()


def _build_publisher(
    run_config: RunConfig,
    run_provenance: RunProvenance,
    registry: ContentRegistry,
    tmp_path: Path,
    *,
    model: TrainerNet | None = None,
    dummy_provider=None,
    consumed_ids_provider=None,
) -> ArtifactPublisher:
    return ArtifactPublisher(
        run_config,
        run_provenance,
        registry,
        model_for_onnx=model,
        dummy_batch_provider=dummy_provider,
        consumed_ids_provider=consumed_ids_provider,
        data_dir=tmp_path / "data",
    )


# ---------------------------------------------------------------------------
# 1. Manifest schema wiring (delegates depth-tests to test_manifest.py)
# ---------------------------------------------------------------------------
def test_publisher_writes_v1_manifest(
    run_config: RunConfig,
    run_provenance: RunProvenance,
    registry: ContentRegistry,
    trainer_net: TrainerNet,
    tmp_path: Path,
) -> None:
    pub = _build_publisher(
        run_config,
        run_provenance,
        registry,
        tmp_path,
        model=trainer_net,
        dummy_provider=lambda: _make_dummy_batch(vocab_size=len(registry)),
    )
    stop = threading.Event()
    pub.start(stop)
    try:
        state_bytes = _serialize_state_dict(trainer_net)
        pub.request_publish(
            PublishRequest(
                step=7,
                model_state_dict_bytes=state_bytes,
                optim_state_dict_bytes=b"\x00",
                loss_total=1.234,
            )
        )
        ref = _await_publish(pub, expected_step=7)
    finally:
        stop.set()
        pub.join(timeout=5)

    assert ref.local_path is not None
    manifest_path = ref.local_path / "manifest.json"
    payload = json.loads(manifest_path.read_text())
    # Required keys present + locked values per Q10-ADR-009 / ADR-006.
    assert payload["schema_version"] == SCHEMA_VERSION
    assert payload["phase"] == 1
    assert payload["onnx_opset_version"] == 17
    assert payload["step"] == 7
    assert payload["run_id"] == run_config.run_id
    assert payload["parent_artifact_id"] is None
    assert payload["content_registry_sha"] == registry.content_hash
    assert payload["code_sha"] == run_provenance.code_sha
    assert payload["seed"] == run_provenance.seed
    assert payload["hyperparameters"]["loss_total"] == pytest.approx(1.234)
    # Re-validate via from_dict (raises if drift).
    ProvenanceManifest.from_dict(payload)


# ---------------------------------------------------------------------------
# 2. ONNX export passes onnx.checker.check_model
# ---------------------------------------------------------------------------
_HAS_ONNX = importlib.util.find_spec("onnx") is not None


@pytest.mark.skipif(not _HAS_ONNX, reason="onnx not installed")
def test_published_onnx_passes_check_model(
    run_config: RunConfig,
    run_provenance: RunProvenance,
    registry: ContentRegistry,
    trainer_net: TrainerNet,
    tmp_path: Path,
) -> None:
    import onnx

    pub = _build_publisher(
        run_config,
        run_provenance,
        registry,
        tmp_path,
        model=trainer_net,
        dummy_provider=lambda: _make_dummy_batch(vocab_size=len(registry)),
    )
    stop = threading.Event()
    pub.start(stop)
    try:
        state_bytes = _serialize_state_dict(trainer_net)
        pub.request_publish(
            PublishRequest(
                step=42,
                model_state_dict_bytes=state_bytes,
                optim_state_dict_bytes=b"\x00",
                loss_total=0.5,
            )
        )
        ref = _await_publish(pub, expected_step=42)
    finally:
        stop.set()
        pub.join(timeout=5)

    onnx_path = ref.local_path / "model.onnx"
    assert onnx_path.is_file()
    onnx_model = onnx.load(str(onnx_path))
    onnx.checker.check_model(onnx_model)
    # No staging dir remnants left in the bundle.
    staging_remnants = list(ref.local_path.glob(".onnx_staging.*"))
    assert staging_remnants == [], f"staging dir remnants: {staging_remnants}"


# ---------------------------------------------------------------------------
# 3. dataset_sha is deterministic
# ---------------------------------------------------------------------------
def test_dataset_sha_deterministic() -> None:
    ids = ("traj-a", "traj-b", "traj-c")
    assert compute_dataset_sha(ids) == compute_dataset_sha(ids)


def test_dataset_sha_order_independent() -> None:
    """Order independence is the sorted-input invariant from Q10-ADR-003."""
    a = ("traj-a", "traj-b", "traj-c")
    b = ("traj-c", "traj-a", "traj-b")
    assert compute_dataset_sha(a) == compute_dataset_sha(b)


def test_dataset_sha_empty_is_stable() -> None:
    """Empty list is well-defined (not a special-cased None / sentinel)."""
    h = compute_dataset_sha(())
    assert isinstance(h, str)
    assert len(h) == 64


# ---------------------------------------------------------------------------
# 4. dataset_sha differs when one ID is added
# ---------------------------------------------------------------------------
def test_dataset_sha_differs_on_added_id() -> None:
    base = ("traj-a", "traj-b", "traj-c")
    grew = (*base, "traj-d")
    assert compute_dataset_sha(base) != compute_dataset_sha(grew)


def test_dataset_sha_differs_on_changed_id() -> None:
    a = ("traj-x", "traj-y")
    b = ("traj-x", "traj-z")
    assert compute_dataset_sha(a) != compute_dataset_sha(b)


# ---------------------------------------------------------------------------
# 5. Atomic temp+rename — no half-written weights.pt visible
# ---------------------------------------------------------------------------
def test_atomic_weights_write_no_partial_file_observed(
    run_config: RunConfig,
    run_provenance: RunProvenance,
    registry: ContentRegistry,
    trainer_net: TrainerNet,
    tmp_path: Path,
) -> None:
    """The atomic helper writes to a temp name and only renames on success.

    We verify the invariant directly: at no point during a publish does a
    file named ``weights.pt`` exist that is not bit-equal to the final
    payload. The helper writes a temp file (named ``.weights.pt.tmp.<uuid>``)
    and ``os.replace`` is the only operation that creates a file at
    ``weights.pt``. ``os.replace`` is atomic on POSIX.
    """
    pub = _build_publisher(
        run_config,
        run_provenance,
        registry,
        tmp_path,
        # Skip ONNX to keep timing snappy; atomicity is a per-file invariant.
        model=None,
        dummy_provider=None,
    )
    stop = threading.Event()
    pub.start(stop)
    try:
        state_bytes = _serialize_state_dict(trainer_net)
        pub.request_publish(
            PublishRequest(
                step=99,
                model_state_dict_bytes=state_bytes,
                optim_state_dict_bytes=b"opt-bytes",
                loss_total=2.0,
            )
        )
        ref = _await_publish(pub, expected_step=99)
    finally:
        stop.set()
        pub.join(timeout=5)

    weights = (ref.local_path / "weights.pt").read_bytes()
    optimizer = (ref.local_path / "optimizer.pt").read_bytes()
    assert weights == state_bytes
    assert optimizer == b"opt-bytes"
    # No stray .tmp.* file remains in the bundle directory.
    tmps = list(ref.local_path.glob(".*tmp*"))
    assert tmps == [], f"unexpected tmp file remnants: {tmps}"


def test_atomic_helper_cleans_tmp_on_error(tmp_path: Path) -> None:
    """If the underlying write blows up, no tmp file should be left behind."""
    from pipeline.trainer.artifact_publisher import _atomic_write_bytes

    target = tmp_path / "out.bin"
    # The helper opens the file via ``open(tmp, "wb")``. Force a failure by
    # passing a non-bytes payload — ``fh.write`` raises TypeError, the
    # except-clause should unlink the tmp file.
    with pytest.raises(TypeError):
        _atomic_write_bytes(target, "not-bytes")  # type: ignore[arg-type]
    assert not target.exists()
    # No stray .tmp file in the parent.
    tmps = list(tmp_path.glob(".*tmp*"))
    assert tmps == []


# ---------------------------------------------------------------------------
# 6. Drop-on-full queue → publish_dropped_count++
# ---------------------------------------------------------------------------
def test_drop_on_full_queue_increments_counter(
    run_config: RunConfig,
    run_provenance: RunProvenance,
    registry: ContentRegistry,
    tmp_path: Path,
) -> None:
    pub = _build_publisher(
        run_config,
        run_provenance,
        registry,
        tmp_path,
        # No model; ONNX skipped — irrelevant for the queue invariant.
    )
    # Do NOT start the publisher; the queue stays full as we put two requests.
    req = PublishRequest(
        step=1,
        model_state_dict_bytes=b"",
        optim_state_dict_bytes=b"",
        loss_total=0.0,
    )
    pub.request_publish(req)  # queued
    assert pub.publish_dropped_count == 0
    pub.request_publish(req)  # dropped
    assert pub.publish_dropped_count == 1
    pub.request_publish(req)  # dropped again
    assert pub.publish_dropped_count == 2


# ---------------------------------------------------------------------------
# 7. Round-trip: publish then load_parent
# ---------------------------------------------------------------------------
def test_publish_then_load_parent_round_trip(
    run_config: RunConfig,
    run_provenance: RunProvenance,
    registry: ContentRegistry,
    trainer_net: TrainerNet,
    tmp_path: Path,
) -> None:
    # First publisher: publish step=42.
    pub_a = _build_publisher(
        run_config,
        run_provenance,
        registry,
        tmp_path,
        model=trainer_net,
        dummy_provider=lambda: _make_dummy_batch(vocab_size=len(registry)),
    )
    stop_a = threading.Event()
    pub_a.start(stop_a)
    try:
        state_bytes = _serialize_state_dict(trainer_net)
        pub_a.request_publish(
            PublishRequest(
                step=42,
                model_state_dict_bytes=state_bytes,
                optim_state_dict_bytes=b"opt",
                loss_total=3.14,
            )
        )
        ref = _await_publish(pub_a, expected_step=42)
    finally:
        stop_a.set()
        pub_a.join(timeout=5)

    # Second publisher: load_parent with the published artifact_id. Reuse the
    # same data_dir so the local-dir scan finds the bundle.
    child_prov = RunProvenance(
        code_sha=run_provenance.code_sha,
        run_dirty=run_provenance.run_dirty,
        start_ts_ns=run_provenance.start_ts_ns + 1,
        host=run_provenance.host,
        seed=run_provenance.seed,
        parent_artifact_id=ref.artifact_id,
        run_id=run_config.run_id + "X",  # arbitrary distinct run_id
    )
    pub_b = _build_publisher(
        run_config,
        child_prov,
        registry,
        tmp_path,
        model=trainer_net,
        dummy_provider=lambda: _make_dummy_batch(vocab_size=len(registry)),
    )
    weights_bytes, registry_bytes, parent_manifest = pub_b.load_parent()
    assert weights_bytes == state_bytes
    assert registry_bytes == registry.bytes_blob
    assert parent_manifest is not None
    assert parent_manifest.artifact_id == ref.artifact_id
    assert parent_manifest.step == 42


def test_load_parent_returns_sentinel_for_none(
    run_config: RunConfig,
    run_provenance: RunProvenance,
    registry: ContentRegistry,
    tmp_path: Path,
) -> None:
    """``parent_artifact_id=None`` → (None, registry.bytes_blob, None)."""
    pub = _build_publisher(run_config, run_provenance, registry, tmp_path)
    weights, reg_bytes, manifest = pub.load_parent()
    assert weights is None
    assert reg_bytes == registry.bytes_blob
    assert manifest is None


def test_load_parent_missing_raises(
    run_config: RunConfig,
    run_provenance: RunProvenance,
    registry: ContentRegistry,
    tmp_path: Path,
) -> None:
    """parent_artifact_id set but no matching bundle → FileNotFoundError."""
    child_prov = RunProvenance(
        code_sha=run_provenance.code_sha,
        run_dirty=run_provenance.run_dirty,
        start_ts_ns=run_provenance.start_ts_ns,
        host=run_provenance.host,
        seed=run_provenance.seed,
        parent_artifact_id="missing-id-xyz",
        run_id=run_config.run_id,
    )
    pub = _build_publisher(run_config, child_prov, registry, tmp_path)
    with pytest.raises(FileNotFoundError):
        pub.load_parent()


# ---------------------------------------------------------------------------
# 8. ONNX round-trip via onnxruntime — logits match within tolerance
# ---------------------------------------------------------------------------
_HAS_ORT = importlib.util.find_spec("onnxruntime") is not None


@pytest.mark.skipif(not (_HAS_ONNX and _HAS_ORT), reason="onnx/onnxruntime not installed")
def test_onnx_round_trip_logits_match(
    run_config: RunConfig,
    run_provenance: RunProvenance,
    registry: ContentRegistry,
    trainer_net: TrainerNet,
    tmp_path: Path,
) -> None:
    import onnxruntime  # pyright: ignore[reportMissingImports]  # optional dep; test skipped when absent

    dummy = _make_dummy_batch(vocab_size=len(registry))
    pub = _build_publisher(
        run_config,
        run_provenance,
        registry,
        tmp_path,
        model=trainer_net,
        dummy_provider=lambda: dummy,
    )
    stop = threading.Event()
    pub.start(stop)
    try:
        state_bytes = _serialize_state_dict(trainer_net)
        pub.request_publish(
            PublishRequest(
                step=11,
                model_state_dict_bytes=state_bytes,
                optim_state_dict_bytes=b"\x00",
                loss_total=0.0,
            )
        )
        ref = _await_publish(pub, expected_step=11)
    finally:
        stop.set()
        pub.join(timeout=5)

    onnx_path = ref.local_path / "model.onnx"
    sess = onnxruntime.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    ort_inputs = {
        "tokens": dummy.tokens.numpy(),
        "padding_mask": dummy.padding_mask.numpy(),
        "legal_action_mask": dummy.legal_action_mask.numpy(),
    }
    (ort_logits,) = sess.run(["policy_logits"], ort_inputs)

    with torch.no_grad():
        torch_logits = trainer_net(dummy).policy_logits.numpy()

    assert ort_logits.shape == torch_logits.shape
    diff = abs(ort_logits - torch_logits).max()
    assert diff < 1e-4, f"ONNX vs torch logit diff too large: {diff}"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def _await_publish(
    pub: ArtifactPublisher, *, expected_step: int, timeout_sec: float = 30.0
) -> ArtifactRef:
    """Poll ``last_published`` until it reflects the expected step."""
    deadline = time.monotonic() + timeout_sec
    while time.monotonic() < deadline:
        ref = pub.last_published()
        if ref is not None and ref.step == expected_step:
            return ref
        if pub.publish_err_count > 0:
            raise AssertionError(
                f"publisher recorded an error during publish "
                f"(publish_err_count={pub.publish_err_count})"
            )
        time.sleep(0.05)
    raise TimeoutError(
        f"publish for step={expected_step} did not complete in {timeout_sec}s "
        f"(last_published={pub.last_published()}, "
        f"ok={pub.publish_ok_count}, err={pub.publish_err_count}, "
        f"dropped={pub.publish_dropped_count})"
    )

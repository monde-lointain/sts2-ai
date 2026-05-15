"""Artifact-publisher submodule: Phase-1 Q5-stub local-directory publisher.

Per Q10-ADR-009, Phase-1 publishes write the full bundle
(``weights.pt``, ``optimizer.pt``, ``model.onnx``, ``content_registry.json``,
``manifest.json``) to ``<data_dir>/runs/<run_id>/checkpoints/<step>/`` via
atomic ``temp + os.replace`` per file. When Q5 ships ``POST /artifacts``,
the local-write call swaps for a Q5 client wire call — single-method swap.

Threading & GPU-safety
======================
The publisher runs on a dedicated daemon thread to keep the GPU step off
the publish wall-clock. **The train_driver pre-serializes** the model and
optimizer state_dicts to bytes on its own thread (where GPU tensors live)
before posting a :class:`PublishRequest`. Concretely, on train_driver:

.. code:: python

    cpu_state = {k: v.detach().cpu() for k, v in net.state_dict().items()}
    buf = io.BytesIO()
    torch.save(cpu_state, buf)
    request_publish(PublishRequest(
        step=..., model_state_dict_bytes=buf.getvalue(), ...))

This avoids the GPU-tensor-cross-thread footgun that ``torch.save`` on a
background thread would otherwise hit, at the cost of one extra copy on
the GPU thread (acceptable; ~10 ms for 10M params vs. ~150 ms GPU step).

ONNX export, by contrast, runs on the publisher thread (~2 s) because
:class:`TrainerNet` is shared across threads. The publisher uses
``net.eval()`` + ``torch.no_grad()`` for the export and only reads
parameters; this is safe because (a) train_driver freezes its forward
pass on the main thread while waiting on its loss-update cycle, and (b)
the published weights come from ``model_state_dict_bytes`` which were
already snapshotted on the train_driver thread. Phase-2+ may switch to
exporting from a sibling :class:`TrainerNet` loaded from
``model_state_dict_bytes`` for stricter isolation; Phase-1 accepts the
narrower contract.

Cadence + back-pressure
=======================
The internal queue is a ``queue.Queue(maxsize=1)``. ``request_publish`` is
non-blocking; on :class:`queue.Full`, it drops the request and increments
:attr:`publish_dropped_count` (back-pressure policy: a single in-flight
publish wins over a backlog — matches Q10-ADR-006).

SIGTERM
=======
On ``stop_event``, the thread drains the queue, completes any in-flight
publish (best-effort), then exits. The bounded :meth:`join` from the
caller enforces wall-clock budget.

See ``pipeline/trainer/docs/specs/modules/artifact-publisher.md`` and
Q10-ADR-009 (Phase-1 local-directory publish).
"""
from __future__ import annotations

import hashlib
import io
import logging
import os
import pathlib
import queue
import socket
import threading
import time
import uuid
from dataclasses import dataclass, field
from typing import Any, Callable, Optional

import torch

from pipeline.common.atomic_io import atomic_write_json
from pipeline.trainer.content_registry import ContentRegistry
from pipeline.trainer.manifest import ProvenanceManifest, SCHEMA_VERSION, validate
from pipeline.trainer.run_config import RunConfig, RunProvenance
from pipeline.trainer.tensor_encoder import EncodedBatch


_LOG = logging.getLogger(__name__)

# ONNX op-set version is locked at 17 per Q10-ADR-006.
_ONNX_OPSET_VERSION: int = 17
# Phase identifier embedded in every manifest (Q10-ADR-009).
_PHASE: int = 1


# ---------------------------------------------------------------------------
# Public records
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class PublishRequest:
    """One checkpoint publish request posted by ``train_driver``.

    Train_driver is responsible for pre-serializing the state_dicts to
    CPU bytes; see the module docstring for the rationale. ``loss_total``
    is logged into the manifest's ``hyperparameters`` block so a published
    artifact carries its loss at publish time without a separate audit log.
    """

    step: int
    model_state_dict_bytes: bytes
    optim_state_dict_bytes: bytes
    loss_total: float


@dataclass(frozen=True)
class ArtifactRef:
    """Pointer to the most recently published artifact.

    ``local_path`` is the Phase-1 local-directory path; when Q5 boots and
    the wire call swaps in, it will gain ``q5_artifact_id`` and the
    ``local_path`` field will become ``None`` for wire-only publishes.
    """

    artifact_id: str
    step: int
    wall_clock_ns: int
    local_path: pathlib.Path | None


# ---------------------------------------------------------------------------
# ArtifactPublisher
# ---------------------------------------------------------------------------
class ArtifactPublisher:
    """Publisher daemon thread + bundle writer.

    Single-thread producer (train_driver) → single-slot bounded queue →
    single-thread consumer (this publisher daemon).

    Parameters
    ----------
    config
        Frozen :class:`RunConfig`. Read for ``data_dir`` and
        ``hyperparameters`` capture.
    run_provenance
        Frozen :class:`RunProvenance`. Read for code_sha / dirty / host /
        seed / parent_artifact_id / run_id.
    content_registry
        Frozen :class:`ContentRegistry`. Its ``bytes_blob`` is passed
        through unmodified to ``content_registry.json``; its
        ``content_hash`` lands in the manifest.
    model_for_onnx
        Optional handle to the training :class:`TrainerNet`. When set
        plus ``dummy_batch_provider`` is also set, the publisher exports
        ONNX on every publish. When either is ``None``, ONNX export is
        skipped with a warn-level log (Phase-1 stub behavior).
    dummy_batch_provider
        Optional callable returning a sample :class:`EncodedBatch` used
        as the ``torch.onnx.export`` dummy input. The publisher calls it
        once per publish to avoid stashing a stale batch.
    consumed_ids_provider
        Optional callable returning the current trajectory-id tuple.
        Wired in by ``train_driver`` to ``data_ingest.snapshot_consumed_ids``.
        Phase-1 stub returns ``()`` when ``None``; the dataset_sha will
        match the sha256 of the empty list (still well-formed).
    data_dir
        Optional ``pathlib.Path`` override for ``config.data_dir``. Used by
        tests to redirect publishes to a temp directory without rewriting
        the frozen config. Production should leave this ``None``.
    """

    # Queue-get poll interval; honors stop_event responsively.
    _QUEUE_GET_TIMEOUT_SEC: float = 1.0

    def __init__(
        self,
        config: RunConfig,
        run_provenance: RunProvenance,
        content_registry: ContentRegistry,
        *,
        model_for_onnx: Optional[torch.nn.Module] = None,
        dummy_batch_provider: Optional[Callable[[], EncodedBatch]] = None,
        consumed_ids_provider: Optional[Callable[[], tuple[str, ...]]] = None,
        data_dir: Optional[pathlib.Path] = None,
    ) -> None:
        self._config = config
        self._provenance = run_provenance
        self._content_registry = content_registry
        self._model_for_onnx = model_for_onnx
        self._dummy_batch_provider = dummy_batch_provider
        self._consumed_ids_provider = consumed_ids_provider
        self._data_dir: pathlib.Path = pathlib.Path(
            data_dir if data_dir is not None else config.data_dir
        )

        # Publish queue: single in-flight; drop excess.
        self._publish_queue: queue.Queue[PublishRequest] = queue.Queue(maxsize=1)
        self._publish_dropped_count: int = 0
        self._publish_ok_count: int = 0
        self._publish_err_count: int = 0

        # Last-published gauge — read by train_driver / metrics_emitter.
        self._last_published_lock = threading.Lock()
        self._last_published: Optional[ArtifactRef] = None

        # Daemon thread + stop event (set in start()).
        self._thread: Optional[threading.Thread] = None
        self._stop_event: Optional[threading.Event] = None

    # ------------------------------------------------------------------
    # Public surface
    # ------------------------------------------------------------------
    @property
    def publish_dropped_count(self) -> int:
        """Counter of dropped publishes (queue.Full at request_publish)."""
        return self._publish_dropped_count

    @property
    def publish_ok_count(self) -> int:
        """Counter of successful publishes."""
        return self._publish_ok_count

    @property
    def publish_err_count(self) -> int:
        """Counter of failed publishes (raised in the publisher thread)."""
        return self._publish_err_count

    def last_published(self) -> Optional[ArtifactRef]:
        """Thread-safe getter for the most recently published artifact."""
        with self._last_published_lock:
            return self._last_published

    def start(self, stop_event: threading.Event) -> None:
        """Spawn the publisher daemon thread."""
        if self._thread is not None:
            raise RuntimeError("ArtifactPublisher.start() called twice")
        self._stop_event = stop_event
        self._thread = threading.Thread(
            target=self._run,
            name="q10-artifact-publisher",
            daemon=True,
        )
        self._thread.start()

    def request_publish(self, req: PublishRequest) -> None:
        """Non-blocking publish request.

        On :class:`queue.Full`, increments :attr:`publish_dropped_count`
        and returns. Never raises.
        """
        try:
            self._publish_queue.put_nowait(req)
        except queue.Full:
            self._publish_dropped_count += 1
            _LOG.warning(
                "artifact_publisher: queue full at step=%d; dropping "
                "request (publish_dropped_count=%d)",
                req.step,
                self._publish_dropped_count,
            )

    def join(self, timeout: float = 5.0) -> None:
        """Bounded join on the publisher thread. Safe before :meth:`start`."""
        if self._thread is not None:
            self._thread.join(timeout=timeout)

    # ------------------------------------------------------------------
    # Bootstrap path: load_parent
    # ------------------------------------------------------------------
    def load_parent(
        self,
    ) -> tuple[bytes | None, bytes | None, ProvenanceManifest | None]:
        """Bootstrap path: read the parent artifact bundle.

        For ``parent_artifact_id is None`` (from-scratch), returns
        ``(None, content_registry.bytes_blob, None)`` — train_driver
        starts with the freshly-loaded registry and no parent manifest.

        For ``parent_artifact_id`` set, scans
        ``<data_dir>/runs/*/checkpoints/*/manifest.json`` to find the
        bundle with the matching artifact_id, then returns
        ``(weights.pt bytes, content_registry.json bytes, manifest)``.
        Raises :class:`FileNotFoundError` if no match is found.

        Phase-2 swap target: replace the local-dir scan with a Q5
        ``GET /artifact/<id>`` HTTP call.
        """
        parent_id = self._provenance.parent_artifact_id
        if parent_id is None:
            return (None, self._content_registry.bytes_blob, None)

        runs_root = self._data_dir / "runs"
        if runs_root.is_dir():
            for manifest_path in runs_root.glob("*/checkpoints/*/manifest.json"):
                try:
                    import json
                    payload = json.loads(manifest_path.read_bytes())
                except (OSError, ValueError):
                    continue
                if payload.get("artifact_id") != parent_id:
                    continue
                bundle_dir = manifest_path.parent
                weights = (bundle_dir / "weights.pt").read_bytes()
                registry_bytes = (bundle_dir / "content_registry.json").read_bytes()
                manifest = ProvenanceManifest.from_dict(payload)
                return (weights, registry_bytes, manifest)

        raise FileNotFoundError(
            f"artifact_publisher.load_parent: no bundle matching "
            f"parent_artifact_id={parent_id!r} under {runs_root}"
        )

    # ------------------------------------------------------------------
    # Publisher thread
    # ------------------------------------------------------------------
    def _run(self) -> None:
        """Publisher daemon loop. Drains the queue on stop_event."""
        assert self._stop_event is not None
        while not self._stop_event.is_set():
            try:
                req = self._publish_queue.get(
                    timeout=self._QUEUE_GET_TIMEOUT_SEC
                )
            except queue.Empty:
                continue
            self._do_publish_safely(req)
        # Stop signaled: drain anything queued for a best-effort final
        # publish. Bounded by the caller's join timeout.
        while True:
            try:
                req = self._publish_queue.get_nowait()
            except queue.Empty:
                return
            self._do_publish_safely(req)

    def _do_publish_safely(self, req: PublishRequest) -> None:
        """Wrap :meth:`_do_publish` with error capture + metrics update."""
        try:
            ref = self._do_publish(req)
        except BaseException as exc:  # noqa: BLE001 — never let the thread die
            self._publish_err_count += 1
            _LOG.exception(
                "artifact_publisher: publish failed at step=%d: %r",
                req.step,
                exc,
            )
            return
        self._publish_ok_count += 1
        with self._last_published_lock:
            self._last_published = ref

    def _do_publish(self, req: PublishRequest) -> ArtifactRef:
        """Stage + commit one bundle. Returns the fresh :class:`ArtifactRef`."""
        artifact_id = uuid.uuid4().hex
        run_id = self._provenance.run_id
        bundle_dir = (
            self._data_dir / "runs" / run_id / "checkpoints" / str(req.step)
        )
        bundle_dir.mkdir(parents=True, exist_ok=True)

        # 1) weights.pt
        _atomic_write_bytes(bundle_dir / "weights.pt", req.model_state_dict_bytes)
        # 2) optimizer.pt
        _atomic_write_bytes(
            bundle_dir / "optimizer.pt", req.optim_state_dict_bytes
        )
        # 3) model.onnx — best-effort; skip if no model/dummy provider.
        if (
            self._model_for_onnx is not None
            and self._dummy_batch_provider is not None
        ):
            self._export_onnx(bundle_dir / "model.onnx")
        else:
            _LOG.info(
                "artifact_publisher: skipping ONNX export "
                "(model=%s dummy_provider=%s)",
                self._model_for_onnx is not None,
                self._dummy_batch_provider is not None,
            )
        # 4) content_registry.json — pass through unchanged.
        _atomic_write_bytes(
            bundle_dir / "content_registry.json",
            self._content_registry.bytes_blob,
        )
        # 5) manifest.json — last, so a partial publish never leaves a
        #    manifest claiming bundle files that don't exist.
        manifest = self._build_manifest(req, artifact_id)
        atomic_write_json(bundle_dir / "manifest.json", manifest.to_dict())

        wall_clock_ns = time.time_ns()
        return ArtifactRef(
            artifact_id=artifact_id,
            step=int(req.step),
            wall_clock_ns=wall_clock_ns,
            local_path=bundle_dir,
        )

    def _build_manifest(
        self, req: PublishRequest, artifact_id: str
    ) -> ProvenanceManifest:
        """Snapshot the per-publish manifest record."""
        consumed_ids: tuple[str, ...] = (
            self._consumed_ids_provider()
            if self._consumed_ids_provider is not None
            else tuple()
        )
        return ProvenanceManifest(
            schema_version=SCHEMA_VERSION,
            artifact_id=artifact_id,
            code_sha=self._provenance.code_sha,
            code_dirty=bool(self._provenance.run_dirty),
            dataset_sha=compute_dataset_sha(consumed_ids),
            dataset_size=len(consumed_ids),
            seed=int(self._provenance.seed),
            hyperparameters=_hyperparameters_from_config(
                self._config, loss_total=req.loss_total
            ),
            parent_artifact_id=self._provenance.parent_artifact_id,
            content_registry_sha=self._content_registry.content_hash,
            onnx_opset_version=_ONNX_OPSET_VERSION,
            phase=_PHASE,
            step=int(req.step),
            created_at_ns=time.time_ns(),
            host=self._provenance.host,
            run_id=self._provenance.run_id,
        )

    def _export_onnx(self, path: pathlib.Path) -> None:
        """Export the model to ONNX at ``path`` (atomic via staging directory).

        ``torch.onnx.export`` may write an external-data sidecar
        (``<path>.data``) for large tensors. To keep the publish atomic we
        export into a sibling staging directory, validate via
        :func:`onnx.checker.check_model`, then ``os.replace`` each file
        into the bundle and remove the staging directory. On any failure
        the staging directory is best-effort wiped.

        Raises if ``check_model`` rejects the export; the wrapping
        :meth:`_do_publish_safely` increments :attr:`publish_err_count`.
        """
        import shutil
        import onnx  # local import: keeps module import path cheap

        model = self._model_for_onnx
        assert model is not None and self._dummy_batch_provider is not None
        dummy = self._dummy_batch_provider()

        # Same wrapper shape as ``tests/test_model.py::test_onnx_export_check_model``:
        # plain-tensor inputs so the ONNX graph has named tensor inputs.
        class _ExportWrap(torch.nn.Module):
            def __init__(self, inner: torch.nn.Module) -> None:
                super().__init__()
                self.inner = inner

            def forward(  # type: ignore[override]
                self,
                tokens: torch.Tensor,
                padding_mask: torch.Tensor,
                legal_action_mask: torch.Tensor,
            ) -> torch.Tensor:
                wrapped = EncodedBatch(
                    tokens=tokens,
                    padding_mask=padding_mask,
                    legal_action_mask=legal_action_mask,
                    policy_target=torch.zeros_like(
                        legal_action_mask, dtype=torch.float32
                    ),
                    combat_sample_targets=torch.zeros((tokens.shape[0], 4)),
                    combat_summary_targets=torch.zeros((tokens.shape[0], 5)),
                    hp_frac_target=torch.zeros((tokens.shape[0],)),
                    prior_logits=torch.zeros_like(
                        legal_action_mask, dtype=torch.float32
                    ),
                    macro_context=torch.zeros((tokens.shape[0], 9)),
                    metadata={},
                )
                return self.inner(wrapped).policy_logits

        wrap = _ExportWrap(model)
        wrap.eval()

        # Sibling staging dir for the export + its possible .data sidecar.
        staging_dir = path.parent / f".onnx_staging.{uuid.uuid4().hex}"
        staging_dir.mkdir(parents=True, exist_ok=True)
        staging_onnx = staging_dir / path.name
        try:
            with torch.no_grad():
                torch.onnx.export(
                    wrap,
                    (
                        dummy.tokens,
                        dummy.padding_mask,
                        dummy.legal_action_mask,
                    ),
                    str(staging_onnx),
                    input_names=[
                        "tokens",
                        "padding_mask",
                        "legal_action_mask",
                    ],
                    output_names=["policy_logits"],
                    opset_version=_ONNX_OPSET_VERSION,
                )
            # Validate from the staging dir so the external-data sidecar
            # (next to staging_onnx) is resolvable by the checker.
            loaded = onnx.load(str(staging_onnx))
            onnx.checker.check_model(loaded)
            # Move each staged file into the bundle. The .data sidecar
            # (if present) must keep its name relative to the .onnx file
            # so the external-data reference still resolves at load time.
            for src in sorted(staging_dir.iterdir()):
                dst = path.parent / src.name
                os.replace(src, dst)
        except BaseException:
            raise
        finally:
            # Best-effort wipe of the staging dir (empty after a successful
            # move; populated only on failure).
            try:
                shutil.rmtree(staging_dir, ignore_errors=True)
            except OSError:
                pass


# ---------------------------------------------------------------------------
# Module-level helpers
# ---------------------------------------------------------------------------
def compute_dataset_sha(consumed_ids: tuple[str, ...]) -> str:
    """sha256 hex of the sorted, newline-joined trajectory-id list.

    Q10-ADR-003 specifies ``sha256(sorted(trajectory_ids))``; we hash the
    canonical ``"\\n".join(sorted(ids))`` byte form so an empty list still
    produces a well-formed hash. Order-independent by construction
    (``sorted`` discards arrival order).
    """
    payload = "\n".join(sorted(consumed_ids)).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _atomic_write_bytes(path: pathlib.Path, data: bytes) -> None:
    """Atomic temp + ``os.replace`` writer for binary blobs.

    Mirrors :func:`pipeline.common.atomic_io.atomic_write_json` for non-JSON
    payloads. ``path.parent`` must already exist.
    """
    tmp = path.with_name(f".{path.name}.tmp.{uuid.uuid4().hex}")
    try:
        with open(tmp, "wb") as fh:
            fh.write(data)
            fh.flush()
            os.fsync(fh.fileno())
        os.replace(tmp, path)
    except BaseException:
        try:
            tmp.unlink()
        except FileNotFoundError:
            pass
        raise


def _hyperparameters_from_config(
    config: RunConfig, *, loss_total: float
) -> dict[str, Any]:
    """Project :class:`RunConfig` into a JSON-serializable hyperparameter dict.

    Includes the network / optim / loss / checkpoint / sampling blocks
    plus the publish-time ``loss_total`` for downstream audit.
    """
    return {
        "loss_total": float(loss_total),
        "seed": int(config.seed),
        "batch_size": int(config.batch_size),
        "sampling_mode": str(config.sampling_mode),
        "prefetch_queue_size": int(config.prefetch_queue_size),
        "network": {
            "d_model": int(config.network.d_model),
            "n_layers": int(config.network.n_layers),
            "n_heads": int(config.network.n_heads),
            "ffn_dim": int(config.network.ffn_dim),
            "max_seq_len": int(config.network.max_seq_len),
            "max_action_space": int(config.network.max_action_space),
            "expected_token_count": (
                None if config.network.expected_token_count is None
                else int(config.network.expected_token_count)
            ),
        },
        "optim": {
            "lr": float(config.optim.lr),
            "weight_decay": float(config.optim.weight_decay),
            "warmup_steps": int(config.optim.warmup_steps),
            "total_steps": int(config.optim.total_steps),
            "grad_clip": float(config.optim.grad_clip),
        },
        "loss_weights": {
            "policy": float(config.loss_weights.policy),
            "combat_sample": float(config.loss_weights.combat_sample),
            "combat_summary": float(config.loss_weights.combat_summary),
            "hp_frac_aux": float(config.loss_weights.hp_frac_aux),
            "kl_beta": float(config.loss_weights.kl_beta),
        },
        "checkpoint": {
            "every_n_steps": int(config.checkpoint.every_n_steps),
            "every_m_minutes": int(config.checkpoint.every_m_minutes),
        },
    }


__all__ = [
    "ArtifactPublisher",
    "ArtifactRef",
    "PublishRequest",
    "compute_dataset_sha",
]

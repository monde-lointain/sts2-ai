"""Train-driver submodule: orchestration of the Q10 training loop.

Per ``pipeline/trainer/docs/specs/modules/train-driver.md``. The
:class:`TrainDriver` is the only Q10 submodule that orchestrates the
others; every other submodule is a capability called from here.

Loop body (single daemon thread spawned in :meth:`start`):

1. ``batch = data_ingest.get_batch(timeout=30)`` — block briefly; ``None``
   from stop event or terminal exhaustion exits the loop cleanly.
2. ``encoded = tensor_encoder.encode_batch(batch.steps)`` — pure CPU tensors.
3. Move ``encoded`` tensors to ``device`` via :func:`_encoded_to_device`
   (the dataclass is frozen so we rebuild it).
4. ``model_output = model.forward(encoded)``.
5. ``loss_result = loss_engine.compute(model_output, encoded)``.
6. NaN / Inf guard: increments ``sts2_q10_nan_loss_total`` and aborts
   per Q10-ADR-004.
7. ``step_stats = optim.step(loss_result.total)``.
8. ``metrics.record_step(step, loss_result.components, step_stats)`` +
   defensive direct ``set()`` of ``sts2_q10_lr``, ``sts2_q10_grad_norm``,
   ``sts2_q10_weight_norm``, ``sts2_q10_loss_total``.
9. Step counter increment (atomic, under :pyattr:`_step_lock`).
10. Checkpoint cadence: every ``every_n_steps`` OR ``every_m_minutes``
    (whichever fires first; reset BOTH counters when either fires).
11. Prior snapshot cadence: every ``kl_prior_refresh_steps`` steps call
    ``model.snapshot_prior()``. Phase-1 default: 1000.

Shutdown semantics:

- On ``stop_event.is_set()`` between steps, exit cleanly.
- A step that has already started runs to completion (don't abort
  mid-``optim.step``); a best-effort final ``PublishRequest`` is then
  posted (drops if the publisher queue is full).
- We DO NOT call ``metrics.shutdown()`` here — that is ``service.py``'s
  responsibility on full SIGTERM.
"""

from __future__ import annotations

import io
import logging
import threading
import time
from typing import Any

import torch

from pipeline.trainer.artifact_publisher import PublishRequest
from pipeline.trainer.run_config import RunConfig, RunProvenance
from pipeline.trainer.tensor_encoder import EncodedBatch

_LOG = logging.getLogger(__name__)


# Phase-1 default KL prior refresh cadence. Spec calls out: this is not in
# RunConfig today; hard-code here and expose as an instance attribute so
# unit tests can override.
_DEFAULT_KL_PRIOR_REFRESH_STEPS: int = 1000

# How long get_batch will block on each call. 30 s matches the spec.
_GET_BATCH_TIMEOUT_SEC: float = 30.0


def _encoded_to_device(eb: EncodedBatch, device: torch.device) -> EncodedBatch:
    """Return a new :class:`EncodedBatch` with every tensor moved to ``device``.

    ``EncodedBatch`` is a frozen dataclass; we rebuild it. ``metadata`` is
    a plain ``dict[str, str]`` and is shared (not copied) — strings are
    immutable.
    """
    return EncodedBatch(
        tokens=eb.tokens.to(device),
        padding_mask=eb.padding_mask.to(device),
        legal_action_mask=eb.legal_action_mask.to(device),
        policy_target=eb.policy_target.to(device),
        combat_sample_targets=eb.combat_sample_targets.to(device),
        combat_summary_targets=eb.combat_summary_targets.to(device),
        hp_frac_target=eb.hp_frac_target.to(device),
        prior_logits=eb.prior_logits.to(device),
        macro_context=eb.macro_context.to(device),
        metadata=eb.metadata,
    )


class TrainDriver:
    """Training-loop orchestrator. Constructed once with all 7 dependencies.

    Parameters
    ----------
    config:
        Frozen :class:`RunConfig` — checkpoint cadence + per-call defaults.
    run_provenance:
        Frozen :class:`RunProvenance` — pass-through metadata; the driver
        itself does not read it but holds the reference so service.py can
        wire one constructor call.
    model:
        Duck-types to ``TrainerNet``. Must expose ``forward``,
        ``state_dict``, ``snapshot_prior``.
    encoder:
        Duck-types to ``TensorEncoder``. Must expose ``encode_batch``.
    ingest:
        Duck-types to ``DataIngest``. Must expose
        ``get_batch(timeout) -> Batch | None``.
    loss_engine:
        Duck-types to ``LossEngine``. Must expose
        ``compute(model_output, encoded_batch) -> LossResult``.
    optim:
        Duck-types to ``OptimController``. Must expose
        ``step(loss) -> StepStats``, ``state_dict()``.
    publisher:
        Duck-types to ``ArtifactPublisher``. Must expose
        ``request_publish(PublishRequest) -> None``.
    metrics:
        Duck-types to ``MetricsEmitter``. Must expose ``inc``, ``set``,
        ``record_step``.
    device:
        Optional override; default is ``cuda:0`` when available else ``cpu``.
    """

    def __init__(
        self,
        config: RunConfig,
        run_provenance: RunProvenance,
        model: Any,
        encoder: Any,
        ingest: Any,
        loss_engine: Any,
        optim: Any,
        publisher: Any,
        metrics: Any,
        device: torch.device | None = None,
    ) -> None:
        self._config = config
        self._run_provenance = run_provenance
        self._model = model
        self._encoder = encoder
        self._ingest = ingest
        self._loss_engine = loss_engine
        self._optim = optim
        self._publisher = publisher
        self._metrics = metrics
        self._device = device if device is not None else _default_device()

        # Step counter — atomic via lock; metrics/HTTP poll via current_step.
        self._step: int = 0
        self._step_lock = threading.Lock()

        # Cadence counters
        ckpt = config.checkpoint
        self._every_n_steps = int(ckpt.every_n_steps)
        self._every_m_seconds = float(ckpt.every_m_minutes) * 60.0
        self._next_checkpoint_at_step: int = self._every_n_steps
        # _next_checkpoint_at_time initialized on start() so unit tests can
        # patch ``time.monotonic`` before the loop spins up.
        self._next_checkpoint_at_time: float = 0.0

        # Phase-1 prior snapshot cadence. Not currently in RunConfig; allow
        # tests to override via direct attribute write.
        self.kl_prior_refresh_steps: int = _DEFAULT_KL_PRIOR_REFRESH_STEPS
        self._next_prior_snapshot_at_step: int = self.kl_prior_refresh_steps

        # Phase-2 schedule event log (intentionally inert in Phase-1).
        self._schedule_events: list[str] = []

        # Thread + stop event handles (set in start()).
        self._thread: threading.Thread | None = None
        self._stop_event: threading.Event | None = None

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------
    def start(self, stop_event: threading.Event) -> None:
        """Spawn the daemon training thread. Returns immediately."""
        if self._thread is not None:
            raise RuntimeError("TrainDriver.start() called twice")
        self._stop_event = stop_event
        # Anchor the time-cadence relative to start. Done here (not in
        # __init__) so that tests patching ``time.monotonic`` see the
        # patched clock at thread launch.
        self._next_checkpoint_at_time = time.monotonic() + self._every_m_seconds
        self._thread = threading.Thread(
            target=self._run,
            name="q10-train-driver",
            daemon=True,
        )
        self._thread.start()

    def current_step(self) -> int:
        """Thread-safe atomic read of the step counter."""
        with self._step_lock:
            return self._step

    def join(self, timeout: float = 30.0) -> None:
        """Bounded join on the training thread. Safe to call before start()."""
        if self._thread is not None:
            self._thread.join(timeout=timeout)

    def apply_schedule_event(self, name: str) -> None:
        """Phase-2+ freeze-unfreeze hook.

        Phase-1: records the event for audit and otherwise does nothing.
        Phase-2 will route into ``optim.set_requires_grad(group, value)``
        keyed off a schedule config block.
        """
        self._schedule_events.append(str(name))

    # ------------------------------------------------------------------
    # Loop body
    # ------------------------------------------------------------------
    def _run(self) -> None:
        """Daemon thread body. Loops until ``stop_event`` set or fatal error."""
        assert self._stop_event is not None
        stop_event = self._stop_event
        while not stop_event.is_set():
            try:
                ran = self._one_step(stop_event)
            except BaseException:
                _LOG.exception("train_driver: fatal error in step")
                stop_event.set()
                raise
            if not ran:
                # No batch / clean exhaustion — exit loop.
                stop_event.set()
                return

        # Stop signaled: best-effort final publish for the current step.
        # ``_one_step`` was not interrupted mid-backward (we only check
        # stop_event between iterations), so the publish reflects a
        # consistent state.
        self._final_publish_best_effort()

    def _one_step(self, stop_event: threading.Event) -> bool:
        """Run one training step. Returns False on clean shutdown signal.

        Returns
        -------
        bool
            ``True`` when a step ran (loop should continue). ``False`` when
            the ingest signaled stop or terminal exhaustion (loop exits).
        """
        batch = self._ingest.get_batch(timeout=_GET_BATCH_TIMEOUT_SEC)
        if batch is None:
            return False

        encoded_cpu = self._encoder.encode_batch(batch.steps)
        encoded = _encoded_to_device(encoded_cpu, self._device)

        model_output = self._model.forward(encoded)
        loss_result = self._loss_engine.compute(model_output, encoded)
        total = loss_result.total

        if _is_nan_or_inf(total):
            step_snapshot = self.current_step()
            self._metrics.inc("sts2_q10_nan_loss_total")
            _LOG.error(
                "train_driver: NaN/Inf loss detected at step=%d; aborting",
                step_snapshot,
            )
            stop_event.set()
            raise RuntimeError(f"NaN loss detected at step {step_snapshot}")

        step_stats = self._optim.step(total)

        # Per-spec: record_step covers steps_total / loss_total /
        # loss_component / grad_norm / lr. We then defensively set the
        # remaining gauges (weight_norm, loss_total via .item() — already
        # set by record_step, but the explicit set is harmless and
        # documents intent).
        self._metrics.record_step(
            self._next_step_number_for_metrics(),
            dict(loss_result.components),
            step_stats,
        )
        self._update_gauges(loss_result, step_stats)

        with self._step_lock:
            self._step += 1
            step_now = self._step

        self._maybe_publish_checkpoint(step_now, total)
        self._maybe_snapshot_prior(step_now)
        return True

    def _next_step_number_for_metrics(self) -> int:
        """Step number passed into ``metrics.record_step``.

        The spec's ``record_step`` increments ``sts2_q10_steps_total`` and
        carries the W&B payload's ``step`` key. We report the step number
        of the step we just completed (i.e. ``_step + 1``, since the
        counter is bumped immediately afterwards). The exact integer is
        only consumed by W&B / dashboards; the counter is what matters for
        Prometheus.
        """
        with self._step_lock:
            return self._step + 1

    def _update_gauges(self, loss_result: Any, step_stats: Any) -> None:
        """Defensive direct ``metrics.set`` for fields ``record_step`` may skip.

        ``record_step`` already sets ``sts2_q10_loss_total``,
        ``sts2_q10_loss_component``, ``sts2_q10_lr``, ``sts2_q10_grad_norm``
        (when ``step_stats`` is provided). We additionally set:

        - ``sts2_q10_weight_norm`` — not covered by ``record_step``.
        - ``sts2_q10_loss_total`` (re-set with ``.item()``) — harmless
          re-write; tolerates ``record_step`` being a no-op in some test
          mocks.
        """
        weight_norm = getattr(step_stats, "weight_norm", None)
        if weight_norm is not None:
            self._metrics.set("sts2_q10_weight_norm", float(weight_norm))
        lr = getattr(step_stats, "lr", None)
        if lr is not None:
            self._metrics.set("sts2_q10_lr", float(lr))
        grad_post = getattr(step_stats, "grad_norm_post_clip", None)
        if grad_post is not None:
            self._metrics.set("sts2_q10_grad_norm", float(grad_post))
        total = getattr(loss_result, "total", None)
        if isinstance(total, torch.Tensor):
            self._metrics.set("sts2_q10_loss_total", float(total.item()))

    # ------------------------------------------------------------------
    # Cadence
    # ------------------------------------------------------------------
    def _maybe_publish_checkpoint(self, step: int, loss_total: torch.Tensor) -> None:
        """Fire a publish request when EITHER cadence threshold is met.

        Reset BOTH counters after firing (otherwise the time cadence would
        fire continuously after its threshold).
        """
        now = time.monotonic()
        step_due = step >= self._next_checkpoint_at_step
        time_due = now >= self._next_checkpoint_at_time
        if not (step_due or time_due):
            return
        self._fire_publish(step, loss_total)
        self._next_checkpoint_at_step = step + self._every_n_steps
        self._next_checkpoint_at_time = now + self._every_m_seconds

    def _fire_publish(self, step: int, loss_total: torch.Tensor) -> None:
        """Pre-serialize state_dicts on this thread and post the request."""
        try:
            model_sd_bytes = _serialize_model_state_dict(self._model)
            optim_sd_bytes = _serialize_optim_state_dict(self._optim)
        except BaseException:
            _LOG.exception(
                "train_driver: state_dict serialization failed at step=%d; skipping publish",
                step,
            )
            return
        try:
            loss_scalar = (
                float(loss_total.detach().item())
                if isinstance(loss_total, torch.Tensor)
                else float(loss_total)
            )
        except BaseException:
            loss_scalar = float("nan")
        req = PublishRequest(
            step=int(step),
            model_state_dict_bytes=model_sd_bytes,
            optim_state_dict_bytes=optim_sd_bytes,
            loss_total=loss_scalar,
        )
        self._publisher.request_publish(req)

    def _maybe_snapshot_prior(self, step: int) -> None:
        """Refresh the KL prior at the configured cadence."""
        if self.kl_prior_refresh_steps <= 0:
            return
        if step >= self._next_prior_snapshot_at_step:
            try:
                self._model.snapshot_prior()
            except BaseException:
                _LOG.exception(
                    "train_driver: model.snapshot_prior() failed at step=%d; continuing",
                    step,
                )
            self._next_prior_snapshot_at_step = step + self.kl_prior_refresh_steps

    # ------------------------------------------------------------------
    # Shutdown
    # ------------------------------------------------------------------
    def _final_publish_best_effort(self) -> None:
        """On clean shutdown, post one final ``PublishRequest`` if possible.

        Drops on full queue (per ``artifact_publisher`` semantics).
        Failures in serialization are swallowed: the goal is to leave a
        checkpoint when feasible, not to block exit.
        """
        with self._step_lock:
            step_now = self._step
        if step_now == 0:
            # Never ran a step; nothing to checkpoint.
            return
        try:
            model_sd_bytes = _serialize_model_state_dict(self._model)
            optim_sd_bytes = _serialize_optim_state_dict(self._optim)
        except BaseException:
            _LOG.warning(
                "train_driver: final-publish serialization failed at step=%d; skipping",
                step_now,
            )
            return
        req = PublishRequest(
            step=int(step_now),
            model_state_dict_bytes=model_sd_bytes,
            optim_state_dict_bytes=optim_sd_bytes,
            loss_total=float("nan"),
        )
        try:
            self._publisher.request_publish(req)
        except BaseException:
            _LOG.warning("train_driver: final-publish post failed; ignoring")


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def _default_device() -> torch.device:
    """Phase-1: cuda:0 when available, else cpu."""
    if torch.cuda.is_available():
        return torch.device("cuda:0")
    return torch.device("cpu")


def _is_nan_or_inf(t: Any) -> bool:
    """Return True iff ``t`` is a tensor (or scalar) containing NaN/Inf.

    Tolerant of plain Python floats (used in test mocks that return
    pre-detached scalars).
    """
    if isinstance(t, torch.Tensor):
        return bool(torch.isnan(t).any().item() or torch.isinf(t).any().item())
    try:
        f = float(t)
    except (TypeError, ValueError):
        return False
    return f != f or f in (float("inf"), float("-inf"))  # noqa: PLR0124 — NaN check


def _serialize_model_state_dict(model: Any) -> bytes:
    """Snapshot ``model.state_dict()`` to CPU and serialize via ``torch.save``.

    The CPU copy is required because the publisher daemon thread cannot
    safely touch GPU tensors (see ``artifact_publisher`` docstring).
    """
    sd = model.state_dict()
    cpu_sd: dict[str, Any] = {}
    for k, v in sd.items():
        if isinstance(v, torch.Tensor):
            cpu_sd[k] = v.detach().cpu()
        else:
            cpu_sd[k] = v
    buf = io.BytesIO()
    torch.save(cpu_sd, buf)
    return buf.getvalue()


def _serialize_optim_state_dict(optim: Any) -> bytes:
    """Serialize ``optim.state_dict()`` to bytes via ``torch.save``."""
    sd = optim.state_dict()
    buf = io.BytesIO()
    torch.save(sd, buf)
    return buf.getvalue()


__all__ = ["TrainDriver"]

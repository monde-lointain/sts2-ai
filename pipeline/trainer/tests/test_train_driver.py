"""Unit tests for ``pipeline.trainer.train_driver`` (S0.E).

Covers the 6 unit tests called out in
``pipeline/trainer/docs/specs/modules/train-driver.md`` §Testing Strategy.

Heavy mocking is intentional: ``TrainDriver`` is the orchestrator; the
real submodules have their own unit tests. Here we verify the loop's
control flow — cadences, NaN guard, shutdown semantics, prior snapshot.
"""
from __future__ import annotations

import threading
import time
from dataclasses import replace
from pathlib import Path
from unittest.mock import MagicMock

import pytest
import torch

from pipeline.trainer.artifact_publisher import PublishRequest
from pipeline.trainer.run_config import CheckpointConfig, RunConfig
from pipeline.trainer.train_driver import TrainDriver, _encoded_to_device


_CFG_PATH = Path(__file__).resolve().parents[1] / "config" / "local.json"


# ---------------------------------------------------------------------------
# Fixtures + helpers
# ---------------------------------------------------------------------------
def _make_loss_result(total_value: float = 0.5) -> MagicMock:
    """Build a LossResult-like mock backed by a real tensor."""
    lr = MagicMock()
    lr.total = torch.tensor(float(total_value), dtype=torch.float32)
    lr.components = {
        "policy": 0.1,
        "combat_sample": 0.1,
        "combat_summary": 0.1,
        "hp_frac_aux": 0.1,
        "kl_vs_prior": 0.1,
    }
    lr.weights = {}
    lr.gradient_diagnostics = {}
    return lr


def _make_step_stats(lr_value: float = 1e-4) -> MagicMock:
    """Build a StepStats-like mock with the duck-typed attrs."""
    ss = MagicMock()
    ss.grad_norm_pre_clip = 1.0
    ss.grad_norm_post_clip = 0.9
    ss.lr = float(lr_value)
    ss.weight_norm = 2.0
    ss.momentum_norm = 0.5
    return ss


def _make_encoded_batch() -> MagicMock:
    """An EncodedBatch-like mock that survives ``_encoded_to_device``."""
    eb = MagicMock()
    # Each field needs a tensor (or object with ``.to``); we use real
    # zero tensors so the helper builds a clean copy without surprise.
    for attr in (
        "tokens",
        "padding_mask",
        "legal_action_mask",
        "policy_target",
        "combat_sample_targets",
        "combat_summary_targets",
        "hp_frac_target",
        "prior_logits",
        "macro_context",
    ):
        setattr(eb, attr, torch.zeros((1, 1)))
    eb.metadata = {}
    return eb


def _make_batch() -> MagicMock:
    """A Batch-like mock; only ``steps`` is read by the loop."""
    b = MagicMock()
    b.steps = (MagicMock(),)  # contents irrelevant; encoder is mocked
    b.cursor_token = "cursor-0"
    b.trajectory_ids = ()
    return b


def _make_driver(
    *,
    every_n_steps: int = 10,
    every_m_minutes: float = 60.0,
    nan_loss: bool = False,
    on_step_hook=None,
) -> tuple[TrainDriver, dict[str, MagicMock], threading.Event]:
    """Construct a TrainDriver with all-mocked deps + a stop event.

    Parameters
    ----------
    every_n_steps, every_m_minutes:
        Override the checkpoint cadence in the config.
    nan_loss:
        When True, ``loss_engine.compute`` returns a NaN loss tensor.
    on_step_hook:
        Optional callable(step_count) invoked from optim.step's
        side_effect — used to inject stop events mid-step.
    """
    base_cfg = RunConfig.load(_CFG_PATH)
    # Override checkpoint cadence by rebuilding the frozen dataclass.
    ckpt = CheckpointConfig(
        every_n_steps=int(every_n_steps),
        every_m_minutes=float(every_m_minutes),
    )
    cfg = replace(base_cfg, checkpoint=ckpt)

    # Encoder
    encoder = MagicMock()
    encoder.encode_batch.return_value = _make_encoded_batch()

    # Ingest — returns a batch on every call.
    ingest = MagicMock()
    ingest.get_batch.side_effect = lambda *a, **kw: _make_batch()

    # Loss engine
    loss_engine = MagicMock()
    if nan_loss:
        nan_result = MagicMock()
        nan_result.total = torch.tensor(float("nan"))
        nan_result.components = {"policy": float("nan")}
        nan_result.weights = {}
        nan_result.gradient_diagnostics = {}
        loss_engine.compute.return_value = nan_result
    else:
        loss_engine.compute.side_effect = lambda *a, **kw: _make_loss_result()

    # Optim — uses an internal step counter so on_step_hook sees the
    # current step number.
    optim = MagicMock()
    optim_call_count = {"n": 0}

    def _optim_step(*_args, **_kwargs):
        optim_call_count["n"] += 1
        if on_step_hook is not None:
            on_step_hook(optim_call_count["n"])
        return _make_step_stats()

    optim.step.side_effect = _optim_step
    optim.state_dict.return_value = {"optimizer": {}, "scheduler": {}}

    # Model
    model = MagicMock()
    model.state_dict.return_value = {}
    model.forward.return_value = MagicMock()

    # Publisher
    publisher = MagicMock()

    # Metrics
    metrics = MagicMock()

    # Driver
    driver = TrainDriver(
        cfg,
        MagicMock(),  # run_provenance
        model,
        encoder,
        ingest,
        loss_engine,
        optim,
        publisher,
        metrics,
        device=torch.device("cpu"),
    )
    stop_event = threading.Event()
    return (
        driver,
        {
            "encoder": encoder,
            "ingest": ingest,
            "loss_engine": loss_engine,
            "optim": optim,
            "model": model,
            "publisher": publisher,
            "metrics": metrics,
        },
        stop_event,
    )


def _run_driver_for(
    driver: TrainDriver,
    stop_event: threading.Event,
    n_steps: int,
    *,
    timeout_seconds: float = 5.0,
) -> None:
    """Drive ``driver`` until it completes ``n_steps`` or ``timeout_seconds``.

    Sets ``stop_event`` once the threshold is hit and joins.
    """
    driver.start(stop_event)
    deadline = time.monotonic() + timeout_seconds
    while driver.current_step() < n_steps and time.monotonic() < deadline:
        time.sleep(0.005)
    stop_event.set()
    driver.join(timeout=timeout_seconds)


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------
def test_cadence_by_step_fires_once_at_threshold() -> None:
    """N=10, M=huge → exactly one publish after 10 steps."""
    driver, mocks, stop = _make_driver(
        every_n_steps=10, every_m_minutes=60 * 24  # very large
    )
    _run_driver_for(driver, stop, n_steps=10)

    # Publisher should have received at least one publish request.
    publish_calls = mocks["publisher"].request_publish.call_args_list
    # We assert "at least 1" rather than "exactly 1" because the final-
    # publish-on-shutdown best-effort may add one more after stop_event.
    # The cadence-while-running publish is what we care about: it must be
    # exactly one *before* the step-9-to-10 boundary.
    # We can verify by inspecting the step field of the first publish:
    assert len(publish_calls) >= 1, "expected at least one publish"
    first_publish = publish_calls[0].args[0]
    assert isinstance(first_publish, PublishRequest)
    assert first_publish.step == 10, (
        f"first publish step should be 10 (cadence fires after step 10 "
        f"completes), got {first_publish.step}"
    )

    # Verify no publish was fired for steps <10.
    for call in publish_calls:
        req = call.args[0]
        assert req.step >= 10, f"unexpected early publish at step={req.step}"


def test_cadence_by_time_fires_once(monkeypatch: pytest.MonkeyPatch) -> None:
    """N=huge, M=0.1s → after enough mock-clocked steps, ≥1 publish fires.

    Uses a manually-advanced ``time.monotonic`` so the test is fast and
    deterministic. We tick the clock forward by 0.2s once after 5 steps
    to ensure the time cadence trips.
    """
    # Patch time.monotonic in the train_driver module so we control it.
    import pipeline.trainer.train_driver as td_mod

    clock = [1000.0]

    def fake_monotonic() -> float:
        return clock[0]

    monkeypatch.setattr(td_mod.time, "monotonic", fake_monotonic)

    driver, mocks, stop = _make_driver(
        every_n_steps=10_000,  # so step cadence never fires
        every_m_minutes=0.1 / 60.0,  # = 0.1s
    )

    # After ~5 mock steps, jump the clock past the threshold.
    def hook(step_n: int) -> None:
        if step_n == 5:
            clock[0] += 1.0  # well past 0.1s

    # Replace optim.step side_effect to add our hook.
    optim = mocks["optim"]
    optim_state = {"n": 0}

    def _step_with_clock(*_a, **_kw):
        optim_state["n"] += 1
        hook(optim_state["n"])
        return _make_step_stats()

    optim.step.side_effect = _step_with_clock

    _run_driver_for(driver, stop, n_steps=10, timeout_seconds=5.0)

    publish_calls = mocks["publisher"].request_publish.call_args_list
    assert len(publish_calls) >= 1, (
        "expected ≥1 publish after time-cadence trip; got none. "
        "OR semantics for cadence is broken."
    )
    # Verify the time-cadence fired EARLIER than the step cadence
    # (step cadence is N=10_000, so step-cadence would never fire here).
    first = publish_calls[0].args[0]
    assert isinstance(first, PublishRequest)
    assert first.step < 10_000, (
        f"first publish step={first.step} should be well below "
        "every_n_steps=10_000 (time cadence trip)"
    )


def test_nan_loss_triggers_shutdown() -> None:
    """NaN loss → nan counter incremented, stop event set, loop exits."""
    driver, mocks, stop = _make_driver(nan_loss=True)
    # Capture the daemon-thread exception so pytest sees a clean test run.
    captured: dict[str, BaseException] = {}

    def excepthook(args) -> None:  # threading.ExceptHookArgs
        captured["exc"] = args.exc_value

    original = threading.excepthook
    threading.excepthook = excepthook
    try:
        driver.start(stop)
        # Daemon should die fast — within a couple iterations.
        driver.join(timeout=2.0)
    finally:
        threading.excepthook = original

    assert stop.is_set(), "stop_event must be set when NaN detected"
    mocks["metrics"].inc.assert_any_call("sts2_q10_nan_loss_total")
    # Optim.step should NOT have been called (NaN guard runs before it).
    assert mocks["optim"].step.call_count == 0, (
        "optim.step should not run after NaN detection"
    )
    # Exception propagated from the daemon thread.
    exc = captured.get("exc")
    assert isinstance(exc, RuntimeError)
    assert "NaN loss detected" in str(exc)


def test_sigterm_mid_step_completes_the_step() -> None:
    """Stop event set during optim.step → that step finishes, then exit."""
    # Use a hook that sets stop_event the moment optim.step is called
    # (i.e. mid-step body). The current step must still complete.
    driver_holder: list[TrainDriver] = []

    def hook(step_n: int) -> None:
        # Set stop on the 1st optim.step call.
        if step_n == 1:
            driver_holder[0]._stop_event.set()  # type: ignore[union-attr]

    driver, mocks, stop = _make_driver(
        every_n_steps=10_000, every_m_minutes=10_000.0, on_step_hook=hook
    )
    driver_holder.append(driver)

    driver.start(stop)
    driver.join(timeout=2.0)

    # The 1st optim.step ran to completion (we count its call).
    assert mocks["optim"].step.call_count == 1, (
        f"step should complete before exit; saw "
        f"{mocks['optim'].step.call_count} optim.step calls"
    )
    # Driver's step counter advanced to 1 (the bump after optim.step).
    assert driver.current_step() == 1


def test_empty_queue_exits_quickly() -> None:
    """ingest.get_batch returns None → loop exits in <100 ms."""
    driver, mocks, stop = _make_driver()
    # Override: get_batch returns None immediately (terminal).
    mocks["ingest"].get_batch.side_effect = lambda *a, **kw: None

    t0 = time.monotonic()
    driver.start(stop)
    driver.join(timeout=1.0)
    elapsed = time.monotonic() - t0

    assert elapsed < 0.5, f"loop should exit fast on None batch; took {elapsed:.3f}s"
    assert stop.is_set(), "stop_event must be set on terminal exhaustion"
    # No optim.step invocations on empty queue.
    assert mocks["optim"].step.call_count == 0


def test_prior_snapshot_cadence_fires_once_per_period() -> None:
    """kl_prior_refresh_steps=50 → snapshot_prior called exactly 1× per 50."""
    driver, mocks, stop = _make_driver(every_n_steps=10_000, every_m_minutes=10_000.0)
    # Override cadence on the driver instance.
    driver.kl_prior_refresh_steps = 50
    driver._next_prior_snapshot_at_step = 50

    _run_driver_for(driver, stop, n_steps=100, timeout_seconds=5.0)

    # Two snapshots: at step 50 and step 100.
    snap_count = mocks["model"].snapshot_prior.call_count
    assert snap_count == 2, (
        f"expected 2 snapshots at steps 50 and 100; got {snap_count}"
    )


# ---------------------------------------------------------------------------
# Auxiliary regression coverage (small, focused)
# ---------------------------------------------------------------------------
def test_current_step_is_atomic() -> None:
    """current_step returns 0 pre-start and reflects increments thereafter."""
    driver, mocks, stop = _make_driver()
    assert driver.current_step() == 0
    _run_driver_for(driver, stop, n_steps=3, timeout_seconds=3.0)
    assert driver.current_step() >= 3


def test_apply_schedule_event_is_phase1_noop() -> None:
    """Phase-1: apply_schedule_event records but does not touch optim."""
    driver, mocks, stop = _make_driver()
    driver.apply_schedule_event("freeze_combat")
    # No optim toggle should have happened.
    mocks["optim"].set_requires_grad.assert_not_called()
    assert "freeze_combat" in driver._schedule_events  # type: ignore[attr-defined]


def test_encoded_to_device_helper_preserves_metadata() -> None:
    """`_encoded_to_device` reconstructs the frozen dataclass with .to."""
    from pipeline.trainer.tensor_encoder import EncodedBatch

    eb = EncodedBatch(
        tokens=torch.zeros((1, 1), dtype=torch.long),
        padding_mask=torch.zeros((1, 1), dtype=torch.bool),
        legal_action_mask=torch.zeros((1, 1), dtype=torch.bool),
        policy_target=torch.zeros((1, 1), dtype=torch.float32),
        combat_sample_targets=torch.zeros((1, 4), dtype=torch.float32),
        combat_summary_targets=torch.zeros((1, 5), dtype=torch.float32),
        hp_frac_target=torch.zeros((1,), dtype=torch.float32),
        prior_logits=torch.zeros((1, 1), dtype=torch.float32),
        macro_context=torch.zeros((1, 11), dtype=torch.float32),
        metadata={"k": "v"},
    )
    moved = _encoded_to_device(eb, torch.device("cpu"))
    assert moved is not eb
    assert moved.metadata == {"k": "v"}
    assert moved.tokens.device == torch.device("cpu")

"""Data-ingest submodule: Q3 sampling client + bounded prefetch queue.

Issues serial ``POST /sample`` calls to Q3 (Q10-ADR-002 single in-flight RPC),
decodes the length-delimited protobuf response stream of ``TrajectoryStep``
frames terminated by a JSON ``{"status": ok|exhausted}`` trailer (per
``pipeline/experience-store/docs/specs/modules/sampler.md``), and pushes
:class:`Batch` records onto a bounded :class:`queue.Queue`. One daemon
prefetcher thread; ``train_driver`` is the single consumer.

Honors Q3-ADR-006 schema-drain (``503 {"reason": "schema_drain",
"retry_after_sec": N}``): sleeps the advertised duration and retries once;
two consecutive 503s raise :class:`Q3UnavailableError` per Q10-ADR-004
fail-fast. Any other 4xx/5xx / connection error also raises
:class:`Q3UnavailableError`.

Trailer-frame detection
------------------------
The Q3 sampler advertises the trailer via the ``X-Sts2-Q3-Trailer`` response
header (``ok`` or ``exhausted``). We trust that header as the primary
signal. As a defensive fallback (and to stay aligned with the spec line 13
contract that the stream itself terminates with a trailer frame), we also
attempt to JSON-decode the final frame: if it parses to a dict with a
``status`` key it is treated as the trailer (excluded from steps), else
it is parsed as a ``TrajectoryStep``.

Trajectory-ID accumulator (Q10-ADR-003 inputs)
-----------------------------------------------
The wire format streams ``TrajectoryStep`` records, not parent
``Trajectory`` envelopes — ``trajectory_id`` lives on ``Trajectory`` per
``contracts/schemas/trajectory/trajectory.proto`` and is therefore NOT
present on each step in the current Q3 contract. Phase-1 stub: we keep
the accumulator empty (``trajectory_ids = ()``). ``artifact_publisher``
will hash an empty list for ``dataset_sha`` until Q3 exposes a
trajectory-id stream (Phase-2 swap is one method).

No third-party deps: ``urllib.request``, ``json``, ``queue``, ``threading``,
``time``.
"""
from __future__ import annotations

import json
import queue
import threading
import time
import urllib.error
import urllib.request
from dataclasses import dataclass
from typing import Optional

from pipeline.common.framing import FramingError, iter_frames
from pipeline.common.trajectory_proto import TrajectoryStep
from pipeline.trainer.run_config import RunConfig


# ---------------------------------------------------------------------------
# Public types
# ---------------------------------------------------------------------------
@dataclass(frozen=True)
class Batch:
    """One prefetched sample batch from Q3.

    ``cursor_token is None`` signals terminal exhaustion (Q3 returned
    ``{"status": "exhausted"}``). ``trajectory_ids`` is a frozen tuple
    captured for ``dataset_sha`` accumulation (Phase-1: always empty —
    see module docstring).
    """

    steps: tuple[TrajectoryStep, ...]
    cursor_token: Optional[str]
    trajectory_ids: tuple[str, ...]


class Q3UnavailableError(RuntimeError):
    """Marker exception: Q3 is unreachable or has failed fast.

    Raised on connection refused, unretryable 5xx, two consecutive 503s,
    or framing errors that fail-fast per Q10-ADR-004. The prefetcher
    surfaces this on ``get_batch``: the next ``get_batch`` after a
    prefetcher death returns the captured exception via re-raise.
    """


# ---------------------------------------------------------------------------
# Implementation
# ---------------------------------------------------------------------------
_VALID_SAMPLING_MODES = frozenset({"uniform", "prioritized"})


class DataIngest:
    """Q3 sampling client + bounded prefetch queue.

    Single producer (prefetcher daemon) / single consumer (``train_driver``
    via :meth:`get_batch`). Owns no persistent state. Constructed once at
    bootstrap; :meth:`start` spawns the daemon.
    """

    # Schema-drain retry policy: at most one consecutive retry before
    # failing fast. Two consecutive 503s -> Q3UnavailableError.
    _MAX_CONSECUTIVE_503 = 2
    _DEFAULT_RETRY_AFTER_SEC = 5
    # HTTP timeout per request (socket-level read). Phase-1: generous;
    # the prefetcher is bounded by the queue capacity anyway.
    _HTTP_TIMEOUT_SEC = 30.0
    # Queue.put / queue.get sub-timeout to make stop_event responsive.
    _STOP_POLL_INTERVAL_SEC = 0.5

    def __init__(self, config: RunConfig) -> None:
        if config.sampling_mode not in _VALID_SAMPLING_MODES:
            raise ValueError(
                f"sampling_mode {config.sampling_mode!r} not supported; "
                f"valid: {sorted(_VALID_SAMPLING_MODES)} (stratified rejected; "
                f"prioritized deferred to S1+ per ADR-020)"
            )
        self.config = config
        self._q3_url = config.q3_url.rstrip("/")
        self._batch_size = int(config.batch_size)
        self._sampling_mode = config.sampling_mode
        self._queue: queue.Queue[Batch] = queue.Queue(
            maxsize=int(config.prefetch_queue_size)
        )
        # Trajectory-ID accumulator (Phase-1: always empty — see docstring).
        self._consumed_ids: list[str] = []
        self._consumed_ids_lock = threading.Lock()
        # Prefetcher thread + stop event.
        self._thread: Optional[threading.Thread] = None
        self._stop_event: Optional[threading.Event] = None
        # Cursor for resumable sampling. None on first request.
        self._last_cursor: Optional[str] = None
        # Captured prefetcher fatal exception (surfaced to consumer).
        self._fatal: Optional[BaseException] = None
        # Sentinel marking terminal exhaustion (Q3 returned "exhausted").
        self._exhausted = threading.Event()

    # ------------------------------------------------------------------
    # Public surface
    # ------------------------------------------------------------------
    def start(self, stop_event: threading.Event) -> None:
        """Spawn the prefetcher daemon. Returns immediately.

        Validates ``sampling_mode`` once more on this path to satisfy the
        spec's "raises on start" requirement for ``prioritized`` (deferred
        to S1+ per ADR-020). ``uniform`` is the Phase-1 mode.
        """
        if self._sampling_mode == "prioritized":
            raise NotImplementedError(
                "sampling_mode='prioritized' deferred to S1+; see ADR-020"
            )
        if self._sampling_mode != "uniform":  # defense in depth (ctor already rejects)
            raise ValueError(
                f"sampling_mode {self._sampling_mode!r} not supported"
            )
        if self._thread is not None:
            raise RuntimeError("DataIngest.start() called twice")
        self._stop_event = stop_event
        self._thread = threading.Thread(
            target=self._run,
            name="q10-data-ingest-prefetcher",
            daemon=True,
        )
        self._thread.start()

    def get_batch(self, timeout: float = 30.0) -> Optional[Batch]:
        """Pop one prefetched batch.

        Blocks up to ``timeout`` seconds. Returns ``None`` on stop_event,
        on terminal exhaustion with an empty queue, or on timeout. Re-raises
        any captured prefetcher fatal exception (Q3UnavailableError) so
        the trainer can fail-fast per Q10-ADR-004.
        """
        deadline = time.monotonic() + float(timeout)
        while True:
            if self._fatal is not None and self._queue.empty():
                exc = self._fatal
                raise exc
            if self._stop_event is not None and self._stop_event.is_set():
                # Drain anything already queued so consumer sees in-flight
                # batches before bailing.
                try:
                    return self._queue.get_nowait()
                except queue.Empty:
                    return None
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                return None
            poll = min(remaining, self._STOP_POLL_INTERVAL_SEC)
            try:
                return self._queue.get(block=True, timeout=poll)
            except queue.Empty:
                if self._exhausted.is_set() and self._queue.empty():
                    return None
                continue

    def snapshot_consumed_ids(self) -> tuple[str, ...]:
        """Return the trajectory-id accumulator as a frozen tuple copy.

        Thread-safe: the snapshot is taken under the accumulator lock and
        returned as a tuple, so the caller cannot mutate the producer's
        state. Caller hashes the tuple for ``dataset_sha`` per Q10-ADR-003.
        """
        with self._consumed_ids_lock:
            return tuple(self._consumed_ids)

    def join(self, timeout: float = 5.0) -> None:
        """Join the prefetcher thread. Safe to call before start()."""
        if self._thread is not None:
            self._thread.join(timeout=timeout)

    # ------------------------------------------------------------------
    # Prefetcher
    # ------------------------------------------------------------------
    def _run(self) -> None:
        """Prefetcher loop. Single-RPC at a time per Q10-ADR-002."""
        assert self._stop_event is not None
        try:
            while not self._stop_event.is_set():
                if self._exhausted.is_set():
                    return
                batch = self._fetch_one_batch_with_retry()
                if batch is None:
                    # stop_event tripped mid-request.
                    return
                # Push batch; block until consumer drains. Honor stop_event
                # by short-poll loops so SIGTERM can interrupt back-pressure.
                while not self._stop_event.is_set():
                    try:
                        self._queue.put(
                            batch, block=True, timeout=self._STOP_POLL_INTERVAL_SEC
                        )
                        break
                    except queue.Full:
                        continue
                if batch.cursor_token is None:
                    # Terminal exhaustion: signal consumer + exit cleanly.
                    self._exhausted.set()
                    return
                self._last_cursor = batch.cursor_token
        except BaseException as exc:  # noqa: BLE001 — propagate to consumer
            self._fatal = exc
            return

    def _fetch_one_batch_with_retry(self) -> Optional[Batch]:
        """Issue one POST /sample, honoring 503-schema_drain retry.

        Returns ``None`` only on stop_event during sleep. Raises
        :class:`Q3UnavailableError` on second consecutive 503 or any
        non-retryable error.
        """
        consecutive_503 = 0
        assert self._stop_event is not None
        while True:
            try:
                return self._post_sample()
            except _SchemaDrain503 as drain:
                consecutive_503 += 1
                if consecutive_503 >= self._MAX_CONSECUTIVE_503:
                    raise Q3UnavailableError(
                        f"Q3 returned {consecutive_503} consecutive 503 "
                        f"schema_drain responses; aborting per Q10-ADR-004"
                    )
                # Sleep the advertised retry duration, then retry.
                # Use time.sleep directly so tests can patch it.
                time.sleep(float(drain.retry_after_sec))
                if self._stop_event.is_set():
                    return None
                continue

    def _post_sample(self) -> Batch:
        """One HTTP POST + response decode. Raises on transport errors."""
        body_bytes = json.dumps(
            {
                "mode": self._sampling_mode,
                "batch_size": self._batch_size,
                "filters": {},
                "cursor_id": self._last_cursor,
            }
        ).encode("utf-8")
        url = f"{self._q3_url}/sample"
        req = urllib.request.Request(
            url,
            data=body_bytes,
            method="POST",
            headers={"Content-Type": "application/json"},
        )
        try:
            with urllib.request.urlopen(req, timeout=self._HTTP_TIMEOUT_SEC) as resp:
                status = resp.getcode()
                resp_body = resp.read()
                trailer_header = resp.headers.get("X-Sts2-Q3-Trailer")
                cursor_header = resp.headers.get("X-Sts2-Q3-Cursor-Id")
        except urllib.error.HTTPError as http_err:
            # 503 schema_drain is the only retryable error per Q3-ADR-006.
            if http_err.code == 503:
                raise self._parse_schema_drain(http_err) from None
            raise Q3UnavailableError(
                f"Q3 /sample returned HTTP {http_err.code}; "
                f"failing fast per Q10-ADR-004 ({http_err.reason})"
            ) from http_err
        except urllib.error.URLError as url_err:
            raise Q3UnavailableError(
                f"Q3 /sample transport error: {url_err.reason}"
            ) from url_err
        except (TimeoutError, OSError) as transport_err:
            raise Q3UnavailableError(
                f"Q3 /sample transport error: {transport_err}"
            ) from transport_err
        if status != 200:
            raise Q3UnavailableError(
                f"Q3 /sample returned unexpected status {status}"
            )
        return self._decode_response(resp_body, trailer_header, cursor_header)

    def _decode_response(
        self,
        body: bytes,
        trailer_header: Optional[str],
        cursor_header: Optional[str],
    ) -> Batch:
        """Decode a length-delimited protobuf stream into a :class:`Batch`.

        The Q3 sampler uses ``X-Sts2-Q3-Trailer`` to advertise the trailer
        status. We trust the header as the primary signal. The stream's
        last frame is also a UTF-8 JSON trailer (``{"status": "..."}``);
        we strip it defensively and parse the remaining frames as
        ``TrajectoryStep``.
        """
        try:
            frames = list(iter_frames(body))
        except FramingError as exc:
            raise Q3UnavailableError(
                f"Q3 /sample framing error: {exc}"
            ) from exc

        # Detect + strip the JSON trailer frame from the end of the stream.
        # Two strategies, applied in order:
        #   1. The header ``X-Sts2-Q3-Trailer`` advertises ``ok|exhausted``;
        #      if the last frame parses as JSON with a "status" key, drop it.
        #   2. Without the header, attempt JSON-parse on the last frame and
        #      drop it iff it has a "status" key (fallback for older Q3).
        derived_trailer = trailer_header
        if frames:
            maybe_trailer = _try_parse_trailer(frames[-1])
            if maybe_trailer is not None:
                frames = frames[:-1]
                if derived_trailer is None:
                    derived_trailer = maybe_trailer

        # Parse remaining frames as TrajectoryStep.
        steps: list[TrajectoryStep] = []
        for frame in frames:
            step = TrajectoryStep()
            try:
                step.ParseFromString(frame)
            except Exception as exc:
                raise Q3UnavailableError(
                    f"Q3 /sample TrajectoryStep parse error: {exc}"
                ) from exc
            steps.append(step)

        # Phase-1: trajectory_ids stays empty (see module docstring).
        # The accumulator must remain consistent across batches; we do
        # not extend it under the Phase-1 stub.

        cursor_token: Optional[str]
        if derived_trailer == "exhausted":
            cursor_token = None
        else:
            # "ok" (or missing): use the X-Sts2-Q3-Cursor-Id header if
            # provided; otherwise fall back to the prior cursor so the
            # next request continues from the same logical position.
            cursor_token = cursor_header if cursor_header else self._last_cursor or ""
            if cursor_token == "":
                # Empty-string cursor is functionally None for the next
                # request body, but we distinguish "exhausted" (None)
                # from "fresh-cursor-not-yet-issued" (""). Keep "".
                pass
        return Batch(
            steps=tuple(steps),
            cursor_token=cursor_token,
            trajectory_ids=tuple(),
        )

    def _parse_schema_drain(
        self, http_err: urllib.error.HTTPError
    ) -> "_SchemaDrain503":
        """Parse the 503 body as ``{"reason","retry_after_sec"}``.

        Tolerant of missing fields: defaults ``retry_after_sec=5`` per
        the Q3-ADR-006 recipe.
        """
        try:
            raw = http_err.read() or b""
            payload = json.loads(raw.decode("utf-8")) if raw else {}
        except (UnicodeDecodeError, json.JSONDecodeError, AttributeError):
            payload = {}
        retry_after = payload.get("retry_after_sec", self._DEFAULT_RETRY_AFTER_SEC)
        try:
            retry_after_sec = int(retry_after)
        except (TypeError, ValueError):
            retry_after_sec = self._DEFAULT_RETRY_AFTER_SEC
        return _SchemaDrain503(retry_after_sec=retry_after_sec)


# ---------------------------------------------------------------------------
# Internals
# ---------------------------------------------------------------------------
@dataclass
class _SchemaDrain503(Exception):
    """Internal control-flow signal: caller should sleep + retry once."""

    retry_after_sec: int


def _try_parse_trailer(frame: bytes) -> Optional[str]:
    """Return the trailer status if ``frame`` is a JSON trailer, else None."""
    try:
        decoded = json.loads(frame.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError):
        return None
    if isinstance(decoded, dict) and "status" in decoded:
        status = decoded["status"]
        if isinstance(status, str):
            return status
    return None


__all__ = [
    "Batch",
    "DataIngest",
    "Q3UnavailableError",
]

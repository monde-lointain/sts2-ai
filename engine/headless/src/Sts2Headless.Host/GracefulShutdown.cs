namespace Sts2Headless.Host;

/// <summary>
/// SIGTERM / SIGINT bridge for the host. Owns a
/// <see cref="CancellationTokenSource"/> that <see cref="MainLoop"/> observes
/// at every action boundary. Subscribes to
/// <see cref="AppDomain.ProcessExit"/> and
/// <see cref="Console.CancelKeyPress"/> — together these cover Linux SIGTERM
/// (sent by <c>systemd</c> / <c>kubectl</c>) and the operator's Ctrl+C
/// (SIGINT).
///
/// <para>
/// <b>Contract per M9 spec:</b> "finish current ExecutionContext step, flush
/// logs, close metrics endpoint, exit 0." This class only sets the token;
/// orchestration of the "finish current step + flush + close" sequence lives
/// in <see cref="Program.Main"/>'s finally block.
/// </para>
///
/// <para>
/// <b>Allowed wall-clock use:</b> the optional grace-deadline timer (started
/// when shutdown is requested) uses <see cref="System.Diagnostics.Stopwatch"/>
/// because it's a process-control concern, not a state-affecting quantity.
/// The token-fired flag and the cancellation propagation themselves remain
/// determinism-clean.
/// </para>
///
/// <para>
/// <b>Lifetime:</b> the class subscribes on construction and unsubscribes on
/// <see cref="Dispose"/>. Tests construct, fire <see cref="Trigger"/>
/// manually, and assert the token reports cancellation.
/// </para>
/// </summary>
public sealed class GracefulShutdown : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly EventHandler _processExitHandler;
    private readonly ConsoleCancelEventHandler _cancelKeyPressHandler;
    private readonly bool _attachedToProcess;
    private int _disposed;

    /// <summary>Cancellation token observed by <see cref="MainLoop"/>.</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>True once <see cref="Trigger"/> or a signal handler has fired.</summary>
    public bool IsShutdownRequested => _cts.IsCancellationRequested;

    /// <summary>
    /// Construct and (optionally) subscribe to process-level signal sources.
    /// Tests pass <paramref name="attachProcessSignals"/>=false to avoid
    /// polluting the shared AppDomain with cross-test handlers.
    /// </summary>
    public GracefulShutdown(bool attachProcessSignals = true)
    {
        _processExitHandler = (_, _) => RequestShutdown("process_exit");
        _cancelKeyPressHandler = (_, e) =>
        {
            // Prevent the runtime from killing the process immediately — let
            // the main loop finish its current step and unwind. M9 spec:
            // graceful shutdown is the canonical SIGINT response.
            e.Cancel = true;
            RequestShutdown("cancel_key");
        };
        if (attachProcessSignals)
        {
            AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
            Console.CancelKeyPress += _cancelKeyPressHandler;
            _attachedToProcess = true;
        }
    }

    /// <summary>Explicit programmatic trigger (also used by tests).</summary>
    public void Trigger(string reason = "manual")
    {
        RequestShutdown(reason);
    }

    /// <summary>Reason recorded on first trigger ("manual", "process_exit", "cancel_key").</summary>
    public string? TriggerReason { get; private set; }

    private void RequestShutdown(string reason)
    {
        // Capture the FIRST reason; subsequent triggers don't overwrite.
        if (!_cts.IsCancellationRequested)
        {
            TriggerReason = reason;
        }
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Race with Dispose() — fine, we're shutting down anyway.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_attachedToProcess)
        {
            try { AppDomain.CurrentDomain.ProcessExit -= _processExitHandler; }
            catch { /* swallow */ }
            try { Console.CancelKeyPress -= _cancelKeyPressHandler; }
            catch { /* swallow */ }
        }
        _cts.Dispose();
    }
}

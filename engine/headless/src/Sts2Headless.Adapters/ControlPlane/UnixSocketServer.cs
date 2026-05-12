using System.Net.Sockets;

namespace Sts2Headless.Adapters.ControlPlane;

/// <summary>
/// A minimal Unix-domain-socket server. Phase-1 contract (per
/// <c>docs/specs/modules/control-plane.md</c>):
///
/// <list type="bullet">
///   <item>Bind to a configured path; create the socket file on
///         <see cref="Start"/>.</item>
///   <item>Accept ONE connection at a time. The handler runs on the server's
///         background thread; while it's running, new connect attempts queue
///         in the kernel backlog.</item>
///   <item>After the handler returns (because the peer closed or the handler
///         finished), return to the accept loop and wait for the next client.</item>
///   <item><see cref="Stop"/> closes the listening socket, deletes the socket
///         file, and joins the accept thread cleanly.</item>
/// </list>
///
/// <para>
/// <b>No concurrency:</b> single-orchestrator pattern. A second client
/// arriving while the first is active will block in connect() until the first
/// disconnects. This is enough for Q11 / Q12 / debug tools (cold path).
/// </para>
///
/// <para>
/// <b>Lifetime:</b> the server thread holds the socket. Disposing the
/// server calls <see cref="Stop"/> if it hasn't been called already; idempotent.
/// </para>
/// </summary>
public sealed class UnixSocketServer : IDisposable
{
    /// <summary>
    /// Signature for the per-connection handler. Receives a duplex
    /// <see cref="Stream"/> over the connected socket; should read/write
    /// line-delimited messages until EOF or until <paramref name="cancellationToken"/>
    /// fires (server <see cref="Stop"/>). When the delegate returns, the
    /// server closes the socket and returns to the accept loop.
    /// </summary>
    public delegate void ConnectionHandler(Stream stream, CancellationToken cancellationToken);

    private readonly string _socketPath;
    private readonly ConnectionHandler _handler;
    private readonly object _lifecycleLock = new();

    private Socket? _listener;
    private Thread? _acceptThread;
    private CancellationTokenSource? _cts;
    private bool _started;
    private bool _disposed;

    /// <param name="socketPath">
    /// Absolute path where the Unix-domain socket file will be created. A
    /// stale file at this path is removed before binding.
    /// </param>
    /// <param name="handler">
    /// Per-connection callback. The server invokes this synchronously on its
    /// accept thread for each accepted client.
    /// </param>
    public UnixSocketServer(string socketPath, ConnectionHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        ArgumentNullException.ThrowIfNull(handler);
        _socketPath = socketPath;
        _handler = handler;
    }

    /// <summary>Absolute path the server is configured to bind to.</summary>
    public string SocketPath => _socketPath;

    /// <summary>True once <see cref="Start"/> has succeeded; reset on <see cref="Stop"/>.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_lifecycleLock) return _started;
        }
    }

    /// <summary>
    /// Bind, listen, and start the accept loop on a background thread. Throws
    /// <see cref="InvalidOperationException"/> if already started. Removes
    /// any stale socket file at the configured path before binding.
    /// </summary>
    public void Start()
    {
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException(
                    $"UnixSocketServer at '{_socketPath}' is already started.");
            }

            // Remove stale file (left over from a crash).
            if (File.Exists(_socketPath))
            {
                File.Delete(_socketPath);
            }

            // Ensure the directory exists (caller error if not, but a clear
            // exception is better than a cryptic SocketException).
            string? dir = Path.GetDirectoryName(_socketPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                throw new DirectoryNotFoundException(
                    $"UnixSocketServer: socket directory does not exist: '{dir}'.");
            }

            Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var endpoint = new UnixDomainSocketEndPoint(_socketPath);
            listener.Bind(endpoint);
            listener.Listen(backlog: 4);

            _listener = listener;
            _cts = new CancellationTokenSource();
            _started = true;

            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = $"UnixSocketServer:{Path.GetFileName(_socketPath)}",
            };
            _acceptThread.Start();
        }
    }

    /// <summary>
    /// Stop the accept loop, close the listener and any in-flight client
    /// socket, and remove the socket file. Idempotent: calling on an
    /// already-stopped or never-started server is a no-op. Blocks until the
    /// accept thread exits.
    /// </summary>
    public void Stop()
    {
        Thread? toJoin = null;
        lock (_lifecycleLock)
        {
            if (!_started) return;
            _started = false;
            _cts?.Cancel();
            // Closing the listener interrupts any blocking Accept() with a
            // SocketException (ObjectDisposed/OperationAborted) — the accept
            // loop catches and exits.
            try { _listener?.Close(); } catch { /* swallowed */ }
            toJoin = _acceptThread;
        }

        // Join outside the lock so the accept thread isn't blocked on us.
        toJoin?.Join();

        lock (_lifecycleLock)
        {
            _listener?.Dispose();
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            _acceptThread = null;

            // Remove the socket file. Some platforms (Linux) leave it on disk
            // after close.
            if (File.Exists(_socketPath))
            {
                try { File.Delete(_socketPath); } catch { /* swallowed */ }
            }
        }
    }

    /// <summary>Calls <see cref="Stop"/>.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        lock (_lifecycleLock) _disposed = true;
    }

    // === Internals ========================================================

    private void AcceptLoop()
    {
        Socket listener;
        CancellationToken ct;
        lock (_lifecycleLock)
        {
            if (_listener is null || _cts is null) return;
            listener = _listener;
            ct = _cts.Token;
        }

        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = listener.Accept();
            }
            catch (ObjectDisposedException)
            {
                // Stop() closed the listener — clean shutdown.
                return;
            }
            catch (SocketException)
            {
                // Listener disposed mid-Accept; treat as shutdown.
                return;
            }

            try
            {
                using NetworkStream ns = new(client, ownsSocket: true);
                _handler(ns, ct);
            }
            catch (IOException)
            {
                // Client closed mid-write; loop on to the next connection.
            }
            catch (ObjectDisposedException)
            {
                // Stream disposed under us; loop on.
            }
            catch
            {
                // Per Phase-1 disconnect-mid-session policy: log + continue.
                // We don't have a logger here; the host wraps us and observes
                // exceptions at a higher level. Swallow so one bad client
                // doesn't kill the server.
            }
        }
    }
}

using System.Text;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Adapters.ControlPlane;

/// <summary>
/// Integrating wrapper: a <see cref="UnixSocketServer"/> serving line-delimited
/// JSON-RPC requests via a <see cref="JsonRpcDispatcher"/> against a
/// <see cref="ControlPlaneSession"/>. The full Phase-1 M4 surface.
///
/// <para>
/// <b>Lifecycle:</b> construct with a session (or build one from a bundle);
/// call <see cref="Start"/>; the server enters the accept loop on a background
/// thread. Clients connect, send one or more line-terminated JSON-RPC
/// requests, and the server writes one response line per request. When the
/// client disconnects, the server returns to the accept loop. Call
/// <see cref="Stop"/> (or dispose) to shut down cleanly.
/// </para>
///
/// <para>
/// <b>Terminate RPC:</b> when the orchestrator invokes <c>terminate</c>, the
/// handler calls back into the server's <see cref="RequestShutdown"/> via the
/// callback wired at construction. The response is sent first, then the
/// server gracefully stops.
/// </para>
/// </summary>
public sealed class ControlPlaneServer : IDisposable
{
    private readonly UnixSocketServer _socketServer;
    private readonly JsonRpcDispatcher _dispatcher;
    private readonly ControlPlaneSession _session;

    /// <summary>Build a server bound to <paramref name="socketPath"/> over an existing session.</summary>
    public ControlPlaneServer(string socketPath, ControlPlaneSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _dispatcher = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(_dispatcher, session, terminate: RequestShutdown);
        _socketServer = new UnixSocketServer(socketPath, HandleConnection);
    }

    /// <summary>The socket path the server is bound to.</summary>
    public string SocketPath => _socketServer.SocketPath;

    /// <summary>The dispatcher routing requests. Exposed for tests that want to inspect/reach handlers.</summary>
    public JsonRpcDispatcher Dispatcher => _dispatcher;

    /// <summary>The session this server owns. Mutated by RPC handlers.</summary>
    public ControlPlaneSession Session => _session;

    /// <summary>True once <see cref="Start"/> has succeeded; reset on <see cref="Stop"/>.</summary>
    public bool IsRunning => _socketServer.IsRunning;

    /// <summary>Start the underlying socket server.</summary>
    public void Start() => _socketServer.Start();

    /// <summary>Stop the underlying socket server. Idempotent.</summary>
    public void Stop() => _socketServer.Stop();

    /// <summary>
    /// Request a graceful shutdown. Used by the <c>terminate</c> RPC handler;
    /// schedules the shutdown on a background task so the current response can
    /// be written to the client before the socket closes. Safe to call
    /// multiple times.
    /// </summary>
    public void RequestShutdown()
    {
        // Run the stop on a worker thread so we don't deadlock by trying to
        // join our own accept thread.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                _socketServer.Stop();
            }
            catch
            { /* swallowed */
            }
        });
    }

    /// <summary>Dispose: stop the server.</summary>
    public void Dispose()
    {
        _socketServer.Dispose();
    }

    // === Internals ========================================================

    /// <summary>
    /// Per-connection callback. Reads line-delimited JSON-RPC requests from
    /// the client, dispatches each, writes the response line back. Loops
    /// until the client closes the socket or the server is cancelled.
    /// </summary>
    private void HandleConnection(Stream stream, CancellationToken ct)
    {
        // No BOM; explicit \n line terminator on the way out.
        var utf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: false
        );
        using StreamReader reader = new(stream, utf8, leaveOpen: true);
        using StreamWriter writer = new(stream, utf8, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = reader.ReadLine();
            }
            catch (IOException)
            {
                return; // client closed mid-read
            }
            catch (ObjectDisposedException)
            {
                return; // stream torn down
            }
            if (line is null)
                return; // EOF

            // Empty line — ignore (some clients flush whitespace).
            if (string.IsNullOrWhiteSpace(line))
                continue;

            string response = _dispatcher.Handle(line);
            try
            {
                writer.WriteLine(response);
            }
            catch (IOException)
            {
                return; // client closed during write
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    // === Factory ==========================================================

    /// <summary>
    /// Convenience builder: bootstrap a fresh smoke session (Silent +
    /// RingOfTheSnake vs CultistsNormal, single Rng with the given seed) and
    /// wrap it in a ControlPlaneServer bound to <paramref name="socketPath"/>.
    /// Used by the Host bootstrap; tests construct sessions directly.
    /// </summary>
    public static ControlPlaneServer BuildSmokeServer(
        string socketPath,
        ManifestStamp stamp,
        uint seed,
        CombatContext context,
        Rng rng,
        IClock clock,
        CardCatalog cards,
        RelicCatalog relics,
        PowerCatalog powers,
        MonsterCatalog monsters,
        EncounterCatalog encounters
    )
    {
        var session = new ControlPlaneSession(
            context,
            rng,
            clock,
            cards,
            relics,
            powers,
            monsters,
            encounters,
            runRng: new RunRngSet("control-plane-default"),
            playerRng: new PlayerRngSet(seed),
            tokens: new TokenMap(),
            stamp: stamp
        );
        return new ControlPlaneServer(socketPath, session);
    }
}

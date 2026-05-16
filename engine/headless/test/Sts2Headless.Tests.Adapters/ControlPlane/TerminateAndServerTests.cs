using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Sts2Headless.Adapters.ControlPlane;

namespace Sts2Headless.Tests.Adapters.ControlPlane;

/// <summary>
/// S11-T6 — terminate RPC + clean server shutdown.
///
/// <para>
/// <b>terminate</b>: no params; returns <c>{ok: true}</c>; the server then
/// gracefully shuts down (closes the socket file and exits the accept loop).
/// </para>
/// </summary>
public sealed class TerminateAndServerTests : IDisposable
{
    private readonly string _socketPath;
    private ControlPlaneServer? _server;

    public TerminateAndServerTests()
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"sts2-headless-t6-{Guid.NewGuid():N}.sock");
    }

    public void Dispose()
    {
        try
        {
            _server?.Stop();
        }
        catch
        { /* swallowed */
        }
        _server?.Dispose();
        if (File.Exists(_socketPath))
        {
            try
            {
                File.Delete(_socketPath);
            }
            catch
            { /* swallowed */
            }
        }
    }

    [Fact]
    public async Task End_to_end_save_state_over_socket()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        _server = new ControlPlaneServer(_socketPath, session);
        _server.Start();

        string resp = await SendOneAsync("""{"jsonrpc":"2.0","method":"save_state","id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        string blob = doc.RootElement.GetProperty("result").GetProperty("state_blob").GetString()!;
        Assert.False(string.IsNullOrEmpty(blob));
    }

    [Fact]
    public async Task End_to_end_unknown_method_returns_minus32601()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        _server = new ControlPlaneServer(_socketPath, session);
        _server.Start();

        string resp = await SendOneAsync("""{"jsonrpc":"2.0","method":"unknown_method","id":5}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(-32601, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Terminate_returns_ok_then_server_shuts_down()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        _server = new ControlPlaneServer(_socketPath, session);
        _server.Start();
        Assert.True(_server.IsRunning);

        string resp = await SendOneAsync("""{"jsonrpc":"2.0","method":"terminate","id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.True(doc.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());

        // Wait for the background shutdown to complete (up to 2s).
        for (int i = 0; i < 40; i++)
        {
            if (!_server.IsRunning)
                break;
            await Task.Delay(50);
        }
        Assert.False(_server.IsRunning, "Server should have stopped after terminate.");
        Assert.False(
            File.Exists(_socketPath),
            $"Socket file at {_socketPath} should be removed after terminate."
        );
    }

    [Fact]
    public async Task Sequential_clients_share_session_state()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        _server = new ControlPlaneServer(_socketPath, session);
        _server.Start();

        // First client: save_state.
        string saveResp = await SendOneAsync("""{"jsonrpc":"2.0","method":"save_state","id":1}""");
        string blob;
        using (JsonDocument doc = JsonDocument.Parse(saveResp))
        {
            blob = doc.RootElement.GetProperty("result").GetProperty("state_blob").GetString()!;
        }
        Assert.False(string.IsNullOrEmpty(blob));

        // Second client: load_state with that blob.
        string loadReq =
            $$"""{"jsonrpc":"2.0","method":"load_state","params":{"state_blob":"{{blob}}"},"id":2}""";
        string loadResp = await SendOneAsync(loadReq);
        using (JsonDocument doc = JsonDocument.Parse(loadResp))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
        }

        // Third client: save_state must return same blob.
        string saveResp2 = await SendOneAsync("""{"jsonrpc":"2.0","method":"save_state","id":3}""");
        using (JsonDocument doc = JsonDocument.Parse(saveResp2))
        {
            string blob2 = doc
                .RootElement.GetProperty("result")
                .GetProperty("state_blob")
                .GetString()!;
            Assert.Equal(blob, blob2);
        }
    }

    [Fact]
    public async Task Multiple_requests_in_same_connection_handled_in_order()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        _server = new ControlPlaneServer(_socketPath, session);
        _server.Start();

        using Socket client = await ConnectAsync(_socketPath);
        await using NetworkStream ns = new(client, ownsSocket: false);
        using StreamWriter w = new(ns, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };
        using StreamReader r = new(ns, new UTF8Encoding(false), leaveOpen: true);

        await w.WriteLineAsync("""{"jsonrpc":"2.0","method":"save_state","id":10}""");
        string? l1 = await r.ReadLineAsync();
        await w.WriteLineAsync("""{"jsonrpc":"2.0","method":"save_state","id":11}""");
        string? l2 = await r.ReadLineAsync();
        await w.WriteLineAsync("""{"jsonrpc":"2.0","method":"save_state","id":12}""");
        string? l3 = await r.ReadLineAsync();

        Assert.NotNull(l1);
        Assert.NotNull(l2);
        Assert.NotNull(l3);

        using JsonDocument d1 = JsonDocument.Parse(l1!);
        using JsonDocument d2 = JsonDocument.Parse(l2!);
        using JsonDocument d3 = JsonDocument.Parse(l3!);
        Assert.Equal(10, d1.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(11, d2.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(12, d3.RootElement.GetProperty("id").GetInt32());
    }

    // === Helpers ==========================================================

    private async Task<string> SendOneAsync(string requestLine)
    {
        using Socket client = await ConnectAsync(_socketPath);
        await using NetworkStream ns = new(client, ownsSocket: false);
        using StreamWriter w = new(ns, new UTF8Encoding(false));
        using StreamReader r = new(ns, new UTF8Encoding(false));
        w.NewLine = "\n";
        await w.WriteLineAsync(requestLine);
        await w.FlushAsync();
        string? line = await r.ReadLineAsync();
        return line ?? string.Empty;
    }

    private static async Task<Socket> ConnectAsync(string path)
    {
        Socket s = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var ep = new UnixDomainSocketEndPoint(path);
        const int MaxAttempts = 50;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                await s.ConnectAsync(ep);
                return s;
            }
            catch (SocketException)
            {
                if (attempt == MaxAttempts - 1)
                    throw;
                await Task.Delay(100);
            }
        }
        return s;
    }
}

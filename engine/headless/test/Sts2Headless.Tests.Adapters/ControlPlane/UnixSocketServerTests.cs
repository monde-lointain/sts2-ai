using System.Net.Sockets;
using System.Text;
using Sts2Headless.Adapters.ControlPlane;

namespace Sts2Headless.Tests.Adapters.ControlPlane;

/// <summary>
/// S11-T1 — UnixSocketServer lifecycle + accept-loop validation.
///
/// <para>
/// Phase 1 contract: open a Unix-domain socket at a configured path; accept
/// ONE connection at a time (sequential, no concurrency); return to the
/// accept loop after the client disconnects; shut down cleanly on Stop.
/// </para>
/// </summary>
public sealed class UnixSocketServerTests : IDisposable
{
    private readonly string _socketPath;

    public UnixSocketServerTests()
    {
        // Each test gets a unique socket path under /tmp so parallel test
        // runs don't collide.
        _socketPath = Path.Combine(
            Path.GetTempPath(),
            $"sts2-headless-t1-{Guid.NewGuid():N}.sock");
    }

    public void Dispose()
    {
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Start_creates_socket_file_at_configured_path()
    {
        using var server = new UnixSocketServer(_socketPath, EchoHandler);
        server.Start();
        try
        {
            // Socket file should exist after Start.
            Assert.True(File.Exists(_socketPath),
                $"Expected socket file at {_socketPath} after Start.");
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void Stop_removes_socket_file()
    {
        var server = new UnixSocketServer(_socketPath, EchoHandler);
        server.Start();
        Assert.True(File.Exists(_socketPath));
        server.Stop();
        Assert.False(File.Exists(_socketPath),
            $"Expected socket file at {_socketPath} to be removed after Stop.");
        server.Dispose();
    }

    [Fact]
    public async Task Single_client_can_connect_send_and_receive()
    {
        using var server = new UnixSocketServer(_socketPath, EchoHandler);
        server.Start();
        try
        {
            string echoed = await SendAndReadLineAsync("hello\n");
            Assert.Equal("hello", echoed);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Server_returns_to_accept_loop_after_client_disconnect()
    {
        using var server = new UnixSocketServer(_socketPath, EchoHandler);
        server.Start();
        try
        {
            // First client.
            string first = await SendAndReadLineAsync("first\n");
            Assert.Equal("first", first);

            // Second client must succeed — proves the accept loop re-entered.
            string second = await SendAndReadLineAsync("second\n");
            Assert.Equal("second", second);

            // Third for good measure.
            string third = await SendAndReadLineAsync("third\n");
            Assert.Equal("third", third);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Multiple_lines_per_connection_are_handled()
    {
        using var server = new UnixSocketServer(_socketPath, EchoHandler);
        server.Start();
        try
        {
            using Socket client = await ConnectAsync(_socketPath);
            await using NetworkStream ns = new(client, ownsSocket: false);
            using StreamWriter w = new(ns, new UTF8Encoding(false));
            using StreamReader r = new(ns, new UTF8Encoding(false));
            w.NewLine = "\n";
            await w.WriteLineAsync("line-a");
            await w.FlushAsync();
            string? a = await r.ReadLineAsync();
            Assert.Equal("line-a", a);

            await w.WriteLineAsync("line-b");
            await w.FlushAsync();
            string? b = await r.ReadLineAsync();
            Assert.Equal("line-b", b);

            await w.WriteLineAsync("line-c");
            await w.FlushAsync();
            string? c = await r.ReadLineAsync();
            Assert.Equal("line-c", c);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void Start_twice_throws()
    {
        using var server = new UnixSocketServer(_socketPath, EchoHandler);
        server.Start();
        try
        {
            Assert.Throws<InvalidOperationException>(() => server.Start());
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void Stop_before_Start_is_noop()
    {
        var server = new UnixSocketServer(_socketPath, EchoHandler);
        // No exception.
        server.Stop();
        server.Dispose();
    }

    [Fact]
    public void Stop_twice_is_noop()
    {
        var server = new UnixSocketServer(_socketPath, EchoHandler);
        server.Start();
        server.Stop();
        server.Stop();
        server.Dispose();
    }

    [Fact]
    public async Task Stale_socket_file_at_path_is_cleaned_up_on_Start()
    {
        File.WriteAllText(_socketPath, "stale");
        Assert.True(File.Exists(_socketPath));
        using var server = new UnixSocketServer(_socketPath, EchoHandler);
        server.Start();
        try
        {
            // Server should have removed the stale file and created its own
            // socket — connection succeeds.
            using Socket client = await ConnectAsync(_socketPath);
            Assert.True(client.Connected);
        }
        finally
        {
            server.Stop();
        }
    }

    // === Helpers ==========================================================

    /// <summary>
    /// Connect to the Unix socket. Retries briefly because the server's
    /// accept loop may not have entered the Listen state in the milliseconds
    /// between Start returning and the first connect attempt.
    /// </summary>
    private static async Task<Socket> ConnectAsync(string path)
    {
        Socket s = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var ep = new UnixDomainSocketEndPoint(path);
        const int MaxAttempts = 50; // 5s total
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            try
            {
                await s.ConnectAsync(ep);
                return s;
            }
            catch (SocketException)
            {
                if (attempt == MaxAttempts - 1) throw;
                await Task.Delay(100);
            }
        }
        return s;
    }

    private async Task<string> SendAndReadLineAsync(string line)
    {
        using Socket client = await ConnectAsync(_socketPath);
        await using NetworkStream ns = new(client, ownsSocket: false);
        using StreamWriter w = new(ns, new UTF8Encoding(false));
        using StreamReader r = new(ns, new UTF8Encoding(false));
        await w.WriteAsync(line);
        await w.FlushAsync();
        string? echoed = await r.ReadLineAsync();
        return echoed ?? string.Empty;
    }

    /// <summary>
    /// Test handler: read a line, echo it back, repeat until disconnect.
    /// </summary>
    private static void EchoHandler(Stream s, CancellationToken ct)
    {
        using StreamReader r = new(s, new UTF8Encoding(false), leaveOpen: true);
        using StreamWriter w = new(s, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };
        while (!ct.IsCancellationRequested)
        {
            string? line = r.ReadLine();
            if (line is null) return; // EOF / client closed
            w.WriteLine(line);
        }
    }
}

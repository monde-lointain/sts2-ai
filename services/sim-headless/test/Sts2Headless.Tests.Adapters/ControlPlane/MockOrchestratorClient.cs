using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Sts2Headless.Tests.Adapters.ControlPlane;

/// <summary>
/// Minimal client harness that drives a <see cref="Sts2Headless.Adapters.ControlPlane.ControlPlaneServer"/>
/// over a Unix-domain socket using line-delimited JSON-RPC 2.0. Used by the
/// S11-T7 hard-gate end-to-end test to mirror the shape an external
/// orchestrator (Q11 curriculum generator / Q12 eval harness) would take.
///
/// <para>
/// <b>Single connection:</b> opens one socket on construction and reuses it
/// for the lifetime of the client. RPCs are serial — the wire is a single
/// request-then-response cycle per call.
/// </para>
/// </summary>
internal sealed class MockOrchestratorClient : IDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private int _nextId = 1;

    public MockOrchestratorClient(string socketPath)
    {
        _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var ep = new UnixDomainSocketEndPoint(socketPath);

        // Retry briefly — accept loop may not be ready on the very first ms.
        Exception? lastEx = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                _socket.Connect(ep);
                lastEx = null;
                break;
            }
            catch (SocketException ex)
            {
                lastEx = ex;
                Thread.Sleep(100);
            }
        }
        if (lastEx is not null)
        {
            throw new InvalidOperationException(
                $"MockOrchestratorClient: failed to connect to '{socketPath}' after retries.", lastEx);
        }

        _stream = new NetworkStream(_socket, ownsSocket: false);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _writer = new StreamWriter(_stream, utf8, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = true,
        };
        _reader = new StreamReader(_stream, utf8, leaveOpen: true);
    }

    public void Dispose()
    {
        try { _writer.Dispose(); } catch { /* swallowed */ }
        try { _reader.Dispose(); } catch { /* swallowed */ }
        try { _stream.Dispose(); } catch { /* swallowed */ }
        try { _socket.Dispose(); } catch { /* swallowed */ }
    }

    /// <summary>
    /// Send one RPC; return the parsed response root element. Caller must
    /// keep the returned <see cref="JsonDocument"/> alive while reading the
    /// element; use the <c>params: JsonElement?</c> overload that returns
    /// the doc for buffered access.
    /// </summary>
    public JsonDocument Call(string method, object? paramsObj = null)
    {
        int id = _nextId++;
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WriteString("method", method);
            if (paramsObj is not null)
            {
                w.WritePropertyName("params");
                JsonSerializer.Serialize(w, paramsObj);
            }
            w.WriteNumber("id", id);
            w.WriteEndObject();
        }
        string requestLine = Encoding.UTF8.GetString(ms.ToArray());
        _writer.WriteLine(requestLine);

        string? response = _reader.ReadLine();
        if (response is null)
        {
            throw new InvalidOperationException(
                $"MockOrchestratorClient: server closed the connection while awaiting response to method='{method}' id={id}.");
        }

        JsonDocument doc = JsonDocument.Parse(response);
        // Verify the response id matches.
        if (doc.RootElement.TryGetProperty("id", out JsonElement idElem)
            && idElem.ValueKind == JsonValueKind.Number)
        {
            int respId = idElem.GetInt32();
            if (respId != id)
            {
                doc.Dispose();
                throw new InvalidOperationException(
                    $"MockOrchestratorClient: response id mismatch (sent {id}, got {respId}).");
            }
        }
        return doc;
    }

    /// <summary>Convenience: call and require a result. Throws on error response.</summary>
    public JsonElement CallExpectResult(string method, object? paramsObj = null)
    {
        JsonDocument doc = Call(method, paramsObj);
        try
        {
            if (doc.RootElement.TryGetProperty("error", out JsonElement err))
            {
                int code = err.GetProperty("code").GetInt32();
                string msg = err.GetProperty("message").GetString() ?? "<no message>";
                throw new InvalidOperationException(
                    $"MockOrchestratorClient: method '{method}' returned error code={code} message='{msg}'.");
            }
            // Clone the result element so callers can use it after the doc is
            // disposed (doc is scope-bound here).
            return doc.RootElement.GetProperty("result").Clone();
        }
        finally
        {
            doc.Dispose();
        }
    }
}

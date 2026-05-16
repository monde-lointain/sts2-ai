using System.Text;
using System.Text.Json;

namespace Sts2Headless.Adapters.ControlPlane;

/// <summary>
/// Line-delimited JSON-RPC 2.0 dispatcher. Parses one request, routes to a
/// registered handler, returns one response line.
///
/// <para>
/// <b>Wire framing (per <c>docs/specs/modules/control-plane.md</c>):</b>
/// each request and response is one line of JSON, terminated by '\n'. The
/// dispatcher takes/returns the JSON without the trailing newline; the
/// transport (<see cref="UnixSocketServer"/>) adds the newline.
/// </para>
/// </summary>
public sealed class JsonRpcDispatcher
{
    /// <summary>Signature for RPC handlers. <paramref name="params"/> is null when the request omits a params field.</summary>
    public delegate JsonRpcResult Handler(JsonElement? @params);

    private readonly Dictionary<string, Handler> _handlers = new(StringComparer.Ordinal);

    /// <summary>Register a method handler. Throws if the name is already registered.</summary>
    public void Register(string method, Handler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers.ContainsKey(method))
        {
            throw new InvalidOperationException(
                $"JsonRpcDispatcher: method '{method}' is already registered."
            );
        }
        _handlers[method] = handler;
    }

    /// <summary>True iff a handler is registered for the given method.</summary>
    public bool HasMethod(string method) => _handlers.ContainsKey(method);

    /// <summary>
    /// Parse one JSON-RPC request line and dispatch. Returns the JSON
    /// response (no trailing newline). On malformed JSON returns a parse
    /// error with id=null; on missing method/id structure returns invalid
    /// request; on unknown method returns method-not-found; on handler
    /// exception returns internal error.
    /// </summary>
    public string Handle(string requestLine)
    {
        ArgumentNullException.ThrowIfNull(requestLine);

        JsonDocument? parsed = null;
        try
        {
            parsed = JsonDocument.Parse(requestLine);
        }
        catch (JsonException)
        {
            return BuildErrorResponse(idElem: null, JsonRpcErrorCodes.ParseError, "Parse error");
        }

        using (parsed)
        {
            JsonElement root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return BuildErrorResponse(
                    idElem: null,
                    JsonRpcErrorCodes.InvalidRequest,
                    "Invalid Request: not an object"
                );
            }

            // Extract id verbatim — must echo back what the caller sent
            // (number, string, or null). If absent, the response carries
            // id=null.
            JsonElement? idElem = root.TryGetProperty("id", out JsonElement idVal)
                ? CloneElement(idVal)
                : null;

            // Validate method.
            if (
                !root.TryGetProperty("method", out JsonElement methodElem)
                || methodElem.ValueKind != JsonValueKind.String
            )
            {
                return BuildErrorResponse(
                    idElem,
                    JsonRpcErrorCodes.InvalidRequest,
                    "Invalid Request: missing or non-string 'method'"
                );
            }
            string method = methodElem.GetString()!;

            // Extract params (optional).
            JsonElement? paramsElem = null;
            if (root.TryGetProperty("params", out JsonElement p))
            {
                paramsElem = CloneElement(p);
            }

            // Find handler.
            if (!_handlers.TryGetValue(method, out Handler? handler))
            {
                return BuildErrorResponse(
                    idElem,
                    JsonRpcErrorCodes.MethodNotFound,
                    $"Method not found: '{method}'"
                );
            }

            // Invoke.
            JsonRpcResult result;
            try
            {
                result = handler(paramsElem);
            }
            catch (Exception ex)
            {
                return BuildErrorResponse(
                    idElem,
                    JsonRpcErrorCodes.InternalError,
                    $"Internal error: {ex.Message}"
                );
            }

            return result.IsOk
                ? BuildResultResponse(idElem, result.ResultValue)
                : BuildErrorResponse(idElem, result.ErrorCode, result.ErrorMessage);
        }
    }

    // === Serialization helpers ==========================================

    private static string BuildResultResponse(JsonElement? idElem, JsonElement result)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WritePropertyName("result");
            result.WriteTo(w);
            WriteId(w, idElem);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildErrorResponse(JsonElement? idElem, int code, string message)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WriteStartObject("error");
            w.WriteNumber("code", code);
            w.WriteString("message", message);
            w.WriteEndObject();
            WriteId(w, idElem);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteId(Utf8JsonWriter w, JsonElement? idElem)
    {
        w.WritePropertyName("id");
        if (idElem is null)
        {
            w.WriteNullValue();
            return;
        }
        idElem.Value.WriteTo(w);
    }

    /// <summary>
    /// Clone a JsonElement so it remains valid after the source
    /// <see cref="JsonDocument"/> is disposed.
    /// </summary>
    private static JsonElement CloneElement(JsonElement src) => src.Clone();
}

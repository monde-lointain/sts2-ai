using System.Text.Json;
using Sts2Headless.Adapters.ControlPlane;

namespace Sts2Headless.Tests.Adapters.ControlPlane;

/// <summary>
/// S11-T2 — JsonRpcDispatcher framing + dispatch validation.
///
/// <para>
/// Line-delimited JSON-RPC 2.0. Each request/response is one line of:
/// </para>
/// <code>
///   request:  {"jsonrpc":"2.0","method":"name","params":{...},"id":n}
///   result:   {"jsonrpc":"2.0","result":{...},"id":n}
///   error:    {"jsonrpc":"2.0","error":{"code":n,"message":"..."},"id":n}
/// </code>
///
/// <para>
/// Error codes (per JSON-RPC 2.0 spec):
/// </para>
/// <list type="bullet">
///   <item>-32700: Parse error (malformed JSON).</item>
///   <item>-32601: Method not found.</item>
///   <item>-32602: Invalid params.</item>
///   <item>-32603: Internal error.</item>
/// </list>
/// </summary>
public sealed class JsonRpcDispatcherTests
{
    [Fact]
    public void Registered_method_invoked_and_result_returned()
    {
        var d = new JsonRpcDispatcher();
        d.Register("echo", (JsonElement? p) =>
        {
            string text = p!.Value.GetProperty("text").GetString()!;
            return JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { echoed = text }));
        });

        string req = """{"jsonrpc":"2.0","method":"echo","params":{"text":"hi"},"id":1}""";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("hi", root.GetProperty("result").GetProperty("echoed").GetString());
    }

    [Fact]
    public void Method_without_params_is_supported()
    {
        var d = new JsonRpcDispatcher();
        d.Register("ping", _ => JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { pong = true })));
        string req = """{"jsonrpc":"2.0","method":"ping","id":42}""";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(42, doc.RootElement.GetProperty("id").GetInt32());
        Assert.True(doc.RootElement.GetProperty("result").GetProperty("pong").GetBoolean());
    }

    [Fact]
    public void Unknown_method_returns_minus32601()
    {
        var d = new JsonRpcDispatcher();
        string req = """{"jsonrpc":"2.0","method":"does_not_exist","id":7}""";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32601, err.GetProperty("code").GetInt32());
        Assert.Equal(7, doc.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void Malformed_json_returns_minus32700()
    {
        var d = new JsonRpcDispatcher();
        string req = "{not-json";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32700, err.GetProperty("code").GetInt32());
        // Per JSON-RPC: id MUST be null when parse error.
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("id").ValueKind);
    }

    [Fact]
    public void Invalid_params_handler_returns_minus32602()
    {
        var d = new JsonRpcDispatcher();
        d.Register("must_have_x", p =>
        {
            if (p is null || p.Value.ValueKind != JsonValueKind.Object || !p.Value.TryGetProperty("x", out _))
            {
                return JsonRpcResult.Error(-32602, "missing required param 'x'");
            }
            return JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { ok = true }));
        });

        string req = """{"jsonrpc":"2.0","method":"must_have_x","params":{},"id":3}""";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32602, err.GetProperty("code").GetInt32());
        Assert.Contains("'x'", err.GetProperty("message").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void Handler_throwing_returns_minus32603_internal_error()
    {
        var d = new JsonRpcDispatcher();
        d.Register("boom", _ => throw new InvalidOperationException("kaboom"));
        string req = """{"jsonrpc":"2.0","method":"boom","id":9}""";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32603, err.GetProperty("code").GetInt32());
        Assert.Equal(9, doc.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void Missing_method_field_returns_invalid_request()
    {
        var d = new JsonRpcDispatcher();
        string req = """{"jsonrpc":"2.0","id":1}""";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement err = doc.RootElement.GetProperty("error");
        // -32600 is Invalid Request per JSON-RPC spec.
        Assert.Equal(-32600, err.GetProperty("code").GetInt32());
    }

    [Fact]
    public void Response_is_one_line_with_no_embedded_newline()
    {
        var d = new JsonRpcDispatcher();
        d.Register("ok", _ => JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { ok = true })));
        string resp = d.Handle("""{"jsonrpc":"2.0","method":"ok","id":1}""");
        Assert.DoesNotContain('\n', resp);
        Assert.DoesNotContain('\r', resp);
    }

    [Fact]
    public void Register_duplicate_method_throws()
    {
        var d = new JsonRpcDispatcher();
        d.Register("a", _ => JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { })));
        Assert.Throws<InvalidOperationException>(
            () => d.Register("a", _ => JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { }))));
    }

    [Fact]
    public void Request_with_null_id_handled_as_notification()
    {
        // JSON-RPC notification: id absent or null, no response expected.
        // For our use we keep id field populated as null (caller chose to
        // treat as fire-and-forget). Dispatcher's contract: still emit one
        // response line, with id=null. Simpler than carving out a null path.
        var d = new JsonRpcDispatcher();
        bool called = false;
        d.Register("notify", _ =>
        {
            called = true;
            return JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { ok = true }));
        });
        string req = """{"jsonrpc":"2.0","method":"notify","id":null}""";
        string resp = d.Handle(req);
        Assert.True(called);
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("id").ValueKind);
    }

    [Fact]
    public void Multiple_requests_can_share_dispatcher_serially()
    {
        var d = new JsonRpcDispatcher();
        int counter = 0;
        d.Register("inc", _ =>
        {
            counter++;
            return JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { value = counter }));
        });
        for (int i = 1; i <= 5; i++)
        {
            string resp = d.Handle($$$"""{"jsonrpc":"2.0","method":"inc","id":{{{i}}}}""");
            using JsonDocument doc = JsonDocument.Parse(resp);
            Assert.Equal(i, doc.RootElement.GetProperty("id").GetInt32());
            Assert.Equal(i, doc.RootElement.GetProperty("result").GetProperty("value").GetInt32());
        }
    }
}

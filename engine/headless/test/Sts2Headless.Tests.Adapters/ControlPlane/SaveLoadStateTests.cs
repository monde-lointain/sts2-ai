using System.Text.Json;
using Sts2Headless.Adapters.ControlPlane;
using Sts2Headless.Adapters.StateCodec;

namespace Sts2Headless.Tests.Adapters.ControlPlane;

/// <summary>
/// S11-T3 — save_state / load_state RPC validation.
///
/// <para>
/// <b>save_state</b>: no params; returns
/// <c>{state_blob: "&lt;base64 S7 codec blob&gt;"}</c>.
/// </para>
///
/// <para>
/// <b>load_state</b>: params <c>{state_blob: "&lt;base64&gt;"}</c>; replaces
/// the session's CombatState; subsequent save_state returns the loaded blob
/// byte-equal.
/// </para>
/// </summary>
public sealed class SaveLoadStateTests
{
    [Fact]
    public void SaveState_returns_nonempty_base64_blob()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"save_state","id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement result = doc.RootElement.GetProperty("result");
        string blob = result.GetProperty("state_blob").GetString()!;
        Assert.False(string.IsNullOrEmpty(blob));

        // Base64 of the codec blob must round-trip.
        byte[] raw = Convert.FromBase64String(blob);
        Assert.True(raw.Length > 0);
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(raw);
        Assert.True(decoded.TrailerValidated);
        global::Sts2Headless.Domain.Combat.CombatState recovered =
            global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(decoded);
        Assert.Equal(session.Context.State, recovered);
    }

    [Fact]
    public void LoadState_replaces_session_state_byte_equal_roundtrip()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        // Save current state.
        string saveResp = d.Handle("""{"jsonrpc":"2.0","method":"save_state","id":1}""");
        string savedBlob;
        using (JsonDocument doc = JsonDocument.Parse(saveResp))
        {
            savedBlob = doc
                .RootElement.GetProperty("result")
                .GetProperty("state_blob")
                .GetString()!;
        }

        // Mutate the session (mimic some progress).
        var beforeState = session.Context.State;
        session.Context.SetState(session.Context.State with { Energy = 0, TurnCounter = 99 });
        Assert.NotEqual(beforeState, session.Context.State);

        // Now load the saved blob.
        string loadReq =
            $$"""{"jsonrpc":"2.0","method":"load_state","params":{"state_blob":"{{savedBlob}}"},"id":2}""";
        string loadResp = d.Handle(loadReq);
        using (JsonDocument doc = JsonDocument.Parse(loadResp))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
        }

        // State should be back to original (compared via CombatState.Equals).
        Assert.Equal(beforeState, session.Context.State);

        // Subsequent save_state should produce the same blob bytes.
        string saveResp2 = d.Handle("""{"jsonrpc":"2.0","method":"save_state","id":3}""");
        string blob2;
        using (JsonDocument doc = JsonDocument.Parse(saveResp2))
        {
            blob2 = doc.RootElement.GetProperty("result").GetProperty("state_blob").GetString()!;
        }
        Assert.Equal(savedBlob, blob2);
    }

    [Fact]
    public void LoadState_missing_state_blob_returns_invalid_params()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"load_state","params":{},"id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void LoadState_malformed_base64_returns_invalid_params()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        string resp = d.Handle(
            """{"jsonrpc":"2.0","method":"load_state","params":{"state_blob":"not-base64!!"},"id":1}"""
        );
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void LoadState_corrupted_codec_blob_returns_invalid_params()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        // Pass an empty base64 — too short to be a valid codec blob.
        string resp = d.Handle(
            """{"jsonrpc":"2.0","method":"load_state","params":{"state_blob":""},"id":1}"""
        );
        using JsonDocument doc = JsonDocument.Parse(resp);
        // Empty bytes — StateCodec throws StateCodecException. Wrapped as invalid params.
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }
}

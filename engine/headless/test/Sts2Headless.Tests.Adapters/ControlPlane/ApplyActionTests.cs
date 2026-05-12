using System.Text.Json;
using Sts2Headless.Adapters.ControlPlane;
using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Tests.Adapters.ControlPlane;

/// <summary>
/// S11-T5 — apply_action RPC validation.
///
/// <para>
/// <b>apply_action</b>: params <c>{action: {type: "play_card" | "end_turn", ...}}</c>;
/// applies via M6 CombatEngine; returns <c>{ok: true, post_hash: "..."}</c>
/// on success or error on illegal play (insufficient energy, wrong phase,
/// unknown card id).
/// </para>
/// </summary>
public sealed class ApplyActionTests
{
    [Fact]
    public void ApplyAction_play_card_happy_path()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        // Pick the first attack card in hand and its first enemy as target.
        CardInstance? attackCard = session.Context.State.HandPile.Cards
            .FirstOrDefault(c => c.ModelId == "StrikeSilent");
        Assert.NotNull(attackCard);
        var enemy = session.Context.State.Enemies.First(e => e.IsAlive);

        int prevHp = enemy.CurrentHp;
        string preHash = ControlPlaneRpcHandlers.HashCurrentState(session);

        string req = "{\"jsonrpc\":\"2.0\",\"method\":\"apply_action\",\"params\":{\"action\":{\"type\":\"play_card\",\"card_instance_id\":"
            + attackCard.InstanceId + ",\"target_enemy_id\":" + enemy.Id + "}},\"id\":1}";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement result = doc.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("ok").GetBoolean());

        // Enemy HP should drop (or block absorbed).
        var freshEnemy = session.Context.State.GetEnemy(enemy.Id);
        Assert.True(freshEnemy.CurrentHp <= prevHp, "Enemy HP must not increase after Strike.");

        // post_hash must differ from pre.
        string postHash = result.GetProperty("post_hash").GetString()!;
        Assert.NotEqual(preHash, postHash);
    }

    [Fact]
    public void ApplyAction_end_turn_advances_phase()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        Assert.Equal(CombatPhase.PlayerActing, session.Context.State.Phase);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"end_turn"}},"id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.True(doc.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());

        // After EndPlayerTurn engine sits at EnemyTurnStart (per CombatEngine.EndPlayerTurn).
        Assert.Equal(CombatPhase.EnemyTurnStart, session.Context.State.Phase);
    }

    [Fact]
    public void ApplyAction_illegal_insufficient_energy_returns_error()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        // Drain energy.
        session.Context.SetState(session.Context.State with { Energy = 0 });

        // Try to play a 1-cost Strike.
        CardInstance? strike = session.Context.State.HandPile.Cards
            .FirstOrDefault(c => c.ModelId == "StrikeSilent");
        Assert.NotNull(strike);
        var enemy = session.Context.State.Enemies.First(e => e.IsAlive);

        string req = "{\"jsonrpc\":\"2.0\",\"method\":\"apply_action\",\"params\":{\"action\":{\"type\":\"play_card\",\"card_instance_id\":"
            + strike.InstanceId + ",\"target_enemy_id\":" + enemy.Id + "}},\"id\":1}";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32602, err.GetProperty("code").GetInt32());
        Assert.Contains("energy", err.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyAction_illegal_card_not_in_hand_returns_error()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        // No card with instance_id=999999 exists in hand.
        string req = """{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"play_card","card_instance_id":999999,"target_enemy_id":1}},"id":1}""";
        string resp = d.Handle(req);
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void ApplyAction_missing_action_param_returns_invalid_params()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{},"id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void ApplyAction_unknown_type_returns_invalid_params()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"nonsense"}},"id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32602, err.GetProperty("code").GetInt32());
        Assert.Contains("nonsense", err.GetProperty("message").GetString()!);
    }

    [Fact]
    public void ApplyAction_end_turn_during_enemy_phase_returns_error()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        // End the player turn first; engine now sits at EnemyTurnStart.
        d.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"end_turn"}},"id":1}""");
        Assert.NotEqual(CombatPhase.PlayerActing, session.Context.State.Phase);

        // Now another end_turn is illegal.
        string resp = d.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"end_turn"}},"id":2}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void ApplyAction_post_hash_is_deterministic()
    {
        // Two parallel sessions with same seed + same action sequence → same hash.
        ControlPlaneSession a = SessionFactory.BootSmokeSession(seed: 42u);
        ControlPlaneSession b = SessionFactory.BootSmokeSession(seed: 42u);
        var da = new JsonRpcDispatcher();
        var db = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(da, a);
        ControlPlaneRpcHandlers.Register(db, b);

        string respA = da.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"end_turn"}},"id":1}""");
        string respB = db.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"end_turn"}},"id":1}""");

        using JsonDocument docA = JsonDocument.Parse(respA);
        using JsonDocument docB = JsonDocument.Parse(respB);
        string hashA = docA.RootElement.GetProperty("result").GetProperty("post_hash").GetString()!;
        string hashB = docB.RootElement.GetProperty("result").GetProperty("post_hash").GetString()!;
        Assert.Equal(hashA, hashB);
    }
}

using System.Text.Json;
using Sts2Headless.Adapters.ControlPlane;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Adapters.ControlPlane;

/// <summary>
/// S11-T4 — set_seed and step_until_decision RPC validation.
///
/// <para>
/// <b>set_seed</b>: params <c>{seed: u32}</c>; replaces the session RNG with
/// a fresh <c>new Rng(seed)</c>; returns <c>{ok: true}</c>. A subsequent step
/// produces a different CanonicalHash than the prior seed (because shuffles
/// land differently and the resulting state differs).
/// </para>
///
/// <para>
/// <b>step_until_decision</b>: no params; advances the engine to the next
/// decision boundary; returns <c>{post_hash, legal_actions, phase}</c>. At
/// combat start the engine has already advanced into PlayerActing — calling
/// this is a no-op that simply reports current state.
/// </para>
/// </summary>
public sealed class SetSeedAndStepTests
{
    [Fact]
    public void SetSeed_replaces_rng_and_session_holds_new_seed()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession(seed: 42u);
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        // B.1-alpha-T2 (RC-3): set_seed re-seeds the entire RunRngSet via
        // the `$"seed-{N}"` string-hash protocol. The session-level
        // observable is `RunRng.StringSeed` (and the derived
        // `RunRng.Seed = hash($"seed-{N}")`). The legacy `Rng` handle is a
        // BUCKET of the run-scope set (the .Shuffle bucket); its Seed is
        // bucket-derived, not the raw `N` from the request.
        RunRngSet originalRunRng = session.RunRng;
        Assert.Equal("seed-42", originalRunRng.StringSeed);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"set_seed","params":{"seed":1234},"id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.True(doc.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
        Assert.NotSame(originalRunRng, session.RunRng);
        Assert.Equal("seed-1234", session.RunRng.StringSeed);
    }

    [Fact]
    public void SetSeed_missing_param_returns_invalid_params()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"set_seed","params":{},"id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void SetSeed_negative_returns_invalid_params()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"set_seed","params":{"seed":-1},"id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        Assert.Equal(-32602, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void SetSeed_changes_canonical_hash_after_an_action()
    {
        // Strategy: drive two parallel sessions; same starting state; one is
        // set_seed'd to 42, the other to 7777. After playing one card that
        // triggers a draw (DeadlyPoison + draw reshuffles a permutation), the
        // resulting state diverges → hashes differ.
        //
        // Phase-1 smoke note: the engine consumes RNG on Shuffle / draw
        // reshuffles. Playing cards from the initial hand on turn 1 won't
        // consume RNG (no reshuffle). To force divergence we end the turn
        // (no RNG), let the enemy act (Cultists are deterministic, no RNG),
        // start a new player turn (which draws — consumes RNG on reshuffle if
        // draw pile depletes). With a 14-card deck and 7-card initial hand,
        // turn-2 draw of 7 will reshuffle once draw goes empty.
        ControlPlaneSession a = SessionFactory.BootSmokeSession(seed: 42u);
        ControlPlaneSession b = SessionFactory.BootSmokeSession(seed: 42u);
        var da = new JsonRpcDispatcher();
        var db = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(da, a);
        ControlPlaneRpcHandlers.Register(db, b);

        // Reseed b to a different seed.
        string reseed = db.Handle("""{"jsonrpc":"2.0","method":"set_seed","params":{"seed":7777},"id":1}""");
        using (JsonDocument doc = JsonDocument.Parse(reseed))
        {
            Assert.True(doc.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
        }

        // End turn on both — engine drives through enemy turn + new player turn.
        string a1 = da.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"end_turn"}},"id":2}""");
        string b1 = db.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"end_turn"}},"id":2}""");

        // Now step to next decision (turn 2 ready). The draw on turn 2 will
        // reshuffle the empty draw pile from discard, consuming RNG, so seeds
        // diverge here.
        string aStep = da.Handle("""{"jsonrpc":"2.0","method":"step_until_decision","id":3}""");
        string bStep = db.Handle("""{"jsonrpc":"2.0","method":"step_until_decision","id":3}""");

        string aHash;
        string bHash;
        using (JsonDocument doc = JsonDocument.Parse(aStep))
        {
            aHash = doc.RootElement.GetProperty("result").GetProperty("post_hash").GetString()!;
        }
        using (JsonDocument doc = JsonDocument.Parse(bStep))
        {
            bHash = doc.RootElement.GetProperty("result").GetProperty("post_hash").GetString()!;
        }

        Assert.NotEqual(aHash, bHash);
    }

    [Fact]
    public void StepUntilDecision_at_player_acting_is_noop_returning_legal_actions()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        // Combat starts in PlayerActing already.
        Assert.Equal(CombatPhase.PlayerActing, session.Context.State.Phase);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"step_until_decision","id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement result = doc.RootElement.GetProperty("result");

        Assert.Equal("PlayerActing", result.GetProperty("phase").GetString());

        JsonElement actions = result.GetProperty("legal_actions");
        Assert.True(actions.GetArrayLength() > 0);

        // End turn must be among them.
        bool hasEndTurn = false;
        foreach (JsonElement a in actions.EnumerateArray())
        {
            if (a.GetProperty("type").GetString() == "end_turn") hasEndTurn = true;
        }
        Assert.True(hasEndTurn);

        // post_hash is a 64-char lowercase hex string.
        string hash = result.GetProperty("post_hash").GetString()!;
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]+$", hash);
    }

    [Fact]
    public void StepUntilDecision_advances_through_enemy_turn()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        // End turn — engine pushes to EnemyTurnStart.
        d.Handle("""{"jsonrpc":"2.0","method":"apply_action","params":{"action":{"type":"end_turn"}},"id":1}""");
        // The smoke EndPlayerTurn transitions to EnemyTurnStart; our
        // step_until_decision should drive through enemy turn back to
        // PlayerActing on turn 2.
        string resp = d.Handle("""{"jsonrpc":"2.0","method":"step_until_decision","id":2}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement result = doc.RootElement.GetProperty("result");
        string phase = result.GetProperty("phase").GetString()!;
        Assert.True(phase == "PlayerActing" || phase == "CombatEnd",
            $"Expected PlayerActing or CombatEnd, got {phase}");
        // Should now be turn 2 if we landed in PlayerActing.
        if (phase == "PlayerActing")
        {
            Assert.Equal(2, session.Context.State.TurnCounter);
        }
    }

    [Fact]
    public void StepUntilDecision_legal_play_card_action_serialization_shape()
    {
        ControlPlaneSession session = SessionFactory.BootSmokeSession();
        var d = new JsonRpcDispatcher();
        ControlPlaneRpcHandlers.Register(d, session);

        string resp = d.Handle("""{"jsonrpc":"2.0","method":"step_until_decision","id":1}""");
        using JsonDocument doc = JsonDocument.Parse(resp);
        JsonElement actions = doc.RootElement.GetProperty("result").GetProperty("legal_actions");

        bool sawPlayCard = false;
        foreach (JsonElement a in actions.EnumerateArray())
        {
            if (a.GetProperty("type").GetString() != "play_card") continue;
            sawPlayCard = true;
            Assert.Equal(JsonValueKind.Number, a.GetProperty("card_instance_id").ValueKind);
            // target_enemy_id is optional — present only for targeted cards.
        }
        Assert.True(sawPlayCard, "Smoke hand should include at least one playable card.");
    }
}

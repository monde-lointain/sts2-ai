using System.Text.Json;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Adapters.ControlPlane;

/// <summary>
/// Registers the Phase-1 control-plane RPC methods on a
/// <see cref="JsonRpcDispatcher"/> against a backing
/// <see cref="ControlPlaneSession"/>. Per <c>docs/specs/modules/control-plane.md</c>:
///
/// <list type="bullet">
///   <item><c>save_state</c>: returns base64 codec blob.</item>
///   <item><c>load_state</c>: replaces session state from a base64 codec blob.</item>
///   <item><c>set_seed</c>: replaces session RNG.</item>
///   <item><c>step_until_decision</c>: advances to next decision boundary.</item>
///   <item><c>apply_action</c>: applies a PlayerAction to current state.</item>
///   <item><c>terminate</c>: signals the host to begin shutdown.</item>
/// </list>
/// </summary>
public static class ControlPlaneRpcHandlers
{
    /// <summary>
    /// Register all six Phase-1 RPC methods on <paramref name="dispatcher"/>.
    /// The <paramref name="terminate"/> callback fires after the
    /// <c>terminate</c> RPC's response is built — implementations should
    /// schedule graceful server shutdown, not block on it (the response must
    /// reach the client before the socket closes).
    /// </summary>
    public static void Register(
        JsonRpcDispatcher dispatcher,
        ControlPlaneSession session,
        Action? terminate = null
    )
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(session);

        dispatcher.Register("save_state", _ => HandleSaveState(session));
        dispatcher.Register("load_state", p => HandleLoadState(session, p));
        dispatcher.Register("set_seed", p => HandleSetSeed(session, p));
        dispatcher.Register("step_until_decision", _ => HandleStepUntilDecision(session));
        dispatcher.Register("apply_action", p => HandleApplyAction(session, p));
        dispatcher.Register("terminate", _ => HandleTerminate(terminate));
    }

    // === save_state =======================================================

    internal static JsonRpcResult HandleSaveState(ControlPlaneSession session)
    {
        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            session.Context.State,
            session.RunRng,
            session.PlayerRng,
            session.Tokens,
            session.Stamp
        );
        string base64 = Convert.ToBase64String(blob);
        JsonElement result = JsonSerializer.SerializeToElement(new { state_blob = base64 });
        return JsonRpcResult.Ok(result);
    }

    // === load_state =======================================================

    internal static JsonRpcResult HandleLoadState(ControlPlaneSession session, JsonElement? @params)
    {
        if (
            @params is null
            || @params.Value.ValueKind != JsonValueKind.Object
            || !@params.Value.TryGetProperty("state_blob", out JsonElement blobElem)
            || blobElem.ValueKind != JsonValueKind.String
        )
        {
            return JsonRpcResult.Error(
                JsonRpcErrorCodes.InvalidParams,
                "load_state: missing or non-string 'state_blob' param"
            );
        }

        string base64 = blobElem.GetString()!;
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            return JsonRpcResult.Error(
                JsonRpcErrorCodes.InvalidParams,
                $"load_state: state_blob is not valid base64: {ex.Message}"
            );
        }

        StateBlob decoded;
        CombatState state;
        TokenMap tokens;
        RunRngSet runRng;
        PlayerRngSet playerRng;
        try
        {
            decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(raw);
            state = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(decoded);
            tokens = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToTokenMap(decoded);
            (runRng, playerRng) = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToRngBundle(
                decoded
            );
        }
        catch (StateCodecException ex)
        {
            return JsonRpcResult.Error(
                JsonRpcErrorCodes.InvalidParams,
                $"load_state: codec rejected blob: {ex.Message}"
            );
        }

        session.ReplaceState(state);
        session.ReplaceCodecCarriers(runRng, playerRng, tokens);
        return JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { ok = true }));
    }

    // === set_seed =========================================================

    internal static JsonRpcResult HandleSetSeed(ControlPlaneSession session, JsonElement? @params)
    {
        if (
            @params is null
            || @params.Value.ValueKind != JsonValueKind.Object
            || !@params.Value.TryGetProperty("seed", out JsonElement seedElem)
            || seedElem.ValueKind != JsonValueKind.Number
        )
        {
            return JsonRpcResult.Error(
                JsonRpcErrorCodes.InvalidParams,
                "set_seed: missing or non-numeric 'seed' param"
            );
        }

        uint seed;
        try
        {
            seed = seedElem.GetUInt32();
        }
        catch (Exception ex)
            when (ex is FormatException
                || ex is OverflowException
                || ex is InvalidOperationException
            )
        {
            return JsonRpcResult.Error(
                JsonRpcErrorCodes.InvalidParams,
                $"set_seed: 'seed' must fit in a uint32 ({ex.Message})"
            );
        }

        // B.1-alpha-T2 (RC-3): reseed by building a fresh RunRngSet via the
        // upstream-shape `$"seed-{N}"` string-hash protocol so every bucket
        // (HP rolls, deck shuffles, monster AI, ...) inherits the new master
        // seed uniformly. The legacy session.Rng handle is re-derived from
        // the new set's .Shuffle bucket.
        var newRunRng = new RunRngSet($"seed-{seed}");
        session.ReplaceRunRng(newRunRng);
        return JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { ok = true }));
    }

    // === step_until_decision ==============================================

    internal static JsonRpcResult HandleStepUntilDecision(ControlPlaneSession session)
    {
        // Phase 1 contract: drive the engine forward until phase becomes
        // PlayerActing (next decision boundary) or CombatEnd. If we're already
        // at a decision boundary, this is a no-op.
        CombatContext ctx = session.Context;
        const int MaxSteps = 64; // safety bound against runaway state machines
        for (int i = 0; i < MaxSteps; i++)
        {
            if (ctx.State.Phase == CombatPhase.PlayerActing)
                break;
            if (ctx.State.IsCombatOver)
                break;

            switch (ctx.State.Phase)
            {
                case CombatPhase.EnemyTurnStart:
                    CombatEngine.EnemyTurn(ctx);
                    break;
                case CombatPhase.EnemyTurnEnd:
                    // The engine leaves phase at EnemyTurnEnd after enemy turn;
                    // the caller (us) then transitions via StartPlayerTurn.
                    CombatEngine.StartPlayerTurn(ctx);
                    break;
                case CombatPhase.EnemyActing:
                    // Mid-enemy-acting: drive to end via EnemyTurn re-entry is
                    // not supported; engine completes synchronously in
                    // EnemyTurn. Treat as bug if we land here.
                    return JsonRpcResult.Error(
                        JsonRpcErrorCodes.InternalError,
                        "step_until_decision: unexpected phase EnemyActing (engine should never pause here)."
                    );
                case CombatPhase.PlayerTurnEnd:
                    // Engine pauses here only as a transient between EndPlayerTurn
                    // and EnemyTurn; the helper transitions to EnemyTurnStart.
                    // Should not normally be observed externally.
                    return JsonRpcResult.Error(
                        JsonRpcErrorCodes.InternalError,
                        "step_until_decision: unexpected phase PlayerTurnEnd."
                    );
                case CombatPhase.PlayerTurnStart:
                    // Engine leaves PlayerTurnStart only via StartCombat / StartPlayerTurn;
                    // both transition directly to PlayerActing.
                    return JsonRpcResult.Error(
                        JsonRpcErrorCodes.InternalError,
                        "step_until_decision: unexpected phase PlayerTurnStart."
                    );
                case CombatPhase.CombatStart:
                    return JsonRpcResult.Error(
                        JsonRpcErrorCodes.InternalError,
                        "step_until_decision: unexpected phase CombatStart (engine should never pause here)."
                    );
                default:
                    return JsonRpcResult.Error(
                        JsonRpcErrorCodes.InternalError,
                        $"step_until_decision: unhandled phase {ctx.State.Phase}."
                    );
            }
        }

        // Build response.
        var legal = LegalActions.Enumerate(ctx.State, session.Cards);
        var legalArr = new List<object>(legal.Length);
        foreach (PlayerAction a in legal)
        {
            legalArr.Add(SerializeAction(a));
        }
        string postHash = HashCurrentState(session);

        var result = JsonSerializer.SerializeToElement(
            new
            {
                post_hash = postHash,
                legal_actions = legalArr,
                phase = ctx.State.Phase.ToString(),
            }
        );
        return JsonRpcResult.Ok(result);
    }

    // === apply_action =====================================================

    internal static JsonRpcResult HandleApplyAction(
        ControlPlaneSession session,
        JsonElement? @params
    )
    {
        if (
            @params is null
            || @params.Value.ValueKind != JsonValueKind.Object
            || !@params.Value.TryGetProperty("action", out JsonElement actionElem)
            || actionElem.ValueKind != JsonValueKind.Object
        )
        {
            return JsonRpcResult.Error(
                JsonRpcErrorCodes.InvalidParams,
                "apply_action: missing or non-object 'action' param"
            );
        }

        if (
            !actionElem.TryGetProperty("type", out JsonElement typeElem)
            || typeElem.ValueKind != JsonValueKind.String
        )
        {
            return JsonRpcResult.Error(
                JsonRpcErrorCodes.InvalidParams,
                "apply_action: 'action.type' missing or non-string"
            );
        }
        string type = typeElem.GetString()!;
        CombatContext ctx = session.Context;

        try
        {
            switch (type)
            {
                case "play_card":
                {
                    if (
                        !actionElem.TryGetProperty("card_instance_id", out JsonElement idElem)
                        || idElem.ValueKind != JsonValueKind.Number
                    )
                    {
                        return JsonRpcResult.Error(
                            JsonRpcErrorCodes.InvalidParams,
                            "apply_action: play_card requires numeric 'card_instance_id'"
                        );
                    }
                    uint cardId = idElem.GetUInt32();
                    uint? targetId = null;
                    if (
                        actionElem.TryGetProperty("target_enemy_id", out JsonElement tgtElem)
                        && tgtElem.ValueKind == JsonValueKind.Number
                    )
                    {
                        targetId = tgtElem.GetUInt32();
                    }
                    CombatEngine.PlayerPlayCard(ctx, cardId, targetId);
                    break;
                }
                case "end_turn":
                {
                    CombatEngine.EndPlayerTurn(ctx);
                    break;
                }
                default:
                    return JsonRpcResult.Error(
                        JsonRpcErrorCodes.InvalidParams,
                        $"apply_action: unknown action type '{type}'"
                    );
            }
        }
        catch (InvalidOperationException ex)
        {
            // Engine raises this for illegal actions (insufficient energy,
            // wrong phase, unknown card id, etc.). Surface as application
            // error — invalid params from the orchestrator's perspective.
            return JsonRpcResult.Error(
                JsonRpcErrorCodes.InvalidParams,
                $"apply_action: rejected by engine: {ex.Message}"
            );
        }

        string postHash = HashCurrentState(session);
        var result = JsonSerializer.SerializeToElement(new { ok = true, post_hash = postHash });
        return JsonRpcResult.Ok(result);
    }

    // === terminate ========================================================

    internal static JsonRpcResult HandleTerminate(Action? terminate)
    {
        // We must build the response BEFORE running the terminate callback
        // (caller closes the socket / server in that callback). The dispatch
        // pipeline returns this result, the transport writes the line, and
        // only after that does the server close — handled at the transport
        // level via the terminate hook. To keep handler synchronous, schedule
        // termination on a background task with a brief delay so the response
        // has time to reach the client.
        if (terminate is not null)
        {
            Task.Run(() =>
            {
                // Allow the response line to flush before shutting down.
                Thread.Sleep(50);
                try
                {
                    terminate();
                }
                catch
                { /* swallowed — best-effort shutdown */
                }
            });
        }
        return JsonRpcResult.Ok(JsonSerializer.SerializeToElement(new { ok = true }));
    }

    // === Helpers ==========================================================

    /// <summary>
    /// Serialize a <see cref="PlayerAction"/> as a small anonymous-typed
    /// object the orchestrator can echo back to <c>apply_action</c>. Wire
    /// shape mirrors what <see cref="HandleApplyAction"/> accepts.
    /// </summary>
    internal static object SerializeAction(PlayerAction action)
    {
        switch (action)
        {
            case PlayerAction.PlayCard pc:
                if (pc.TargetEnemyId.HasValue)
                {
                    return new
                    {
                        type = "play_card",
                        card_instance_id = pc.CardInstanceId,
                        target_enemy_id = pc.TargetEnemyId.Value,
                    };
                }
                return new { type = "play_card", card_instance_id = pc.CardInstanceId };
            case PlayerAction.EndTurn:
                return new { type = "end_turn" };
            default:
                throw new InvalidOperationException(
                    $"SerializeAction: unknown PlayerAction variant {action.GetType().Name}"
                );
        }
    }

    /// <summary>
    /// Canonical SHA-256 hash of the current combat state, computed from the
    /// codec blob. Stable across processes — the property the orchestrator
    /// uses as a "where am I in the trajectory" fingerprint.
    /// </summary>
    internal static string HashCurrentState(ControlPlaneSession session)
    {
        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            session.Context.State,
            session.RunRng,
            session.PlayerRng,
            session.Tokens,
            session.Stamp
        );
        return CanonicalHash.Sha256Hex(blob);
    }
}

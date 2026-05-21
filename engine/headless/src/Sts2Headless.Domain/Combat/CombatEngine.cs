using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Public facade for the combat engine. Forwards all calls to internal-static
/// implementation classes. Preserves the public API verbatim so all callers
/// (MainLoop, ControlPlaneRpcHandlers, tests) recompile unchanged.
///
/// <para>
/// Internal implementation is split across:
/// <list type="bullet">
///   <item><see cref="CombatStarter"/> — <c>StartCombat</c> + 7 helpers.</item>
///   <item><see cref="TurnRunner"/> — turn lifecycle + 7 helpers.</item>
///   <item><see cref="CardPlayer"/> — card-play seam.</item>
///   <item><see cref="DeathBroadcaster"/> — death-snapshot + AfterDeath broadcast.</item>
///   <item><see cref="HookFireSession"/> — shared hook-fire envelope.</item>
/// </list>
/// </para>
/// </summary>
public static class CombatEngine
{
    /// <summary>Upstream <c>CombatManager.baseHandDrawCount</c>.</summary>
    public const int BaseHandDrawCount = 5;

    /// <summary>Upstream Silent base energy per turn.</summary>
    public const int BaseEnergyPerTurnSilent = 3;

    /// <summary>Upstream Silent max HP (Ascension 0).</summary>
    public const int BaseMaxHpSilent = 70;

    /// <summary>Player creature id (always 0 in Phase 1 — single-player).</summary>
    public static readonly CreatureId PlayerId = CreatureId.Player;

    /// <summary>First enemy id (allocated sequentially in spawn order).</summary>
    public static readonly CreatureId FirstEnemyId = CreatureId.FirstEnemy;

    /// <inheritdoc cref="CombatStarter.Start"/>
    public static CombatContext StartCombat(
        IEncounterModel encounter,
        CombatBootstrap catalogs,
        PlayerSpec player,
        RunRngSet runRng,
        IClock clock,
        int totalFloor = 0
    ) => CombatStarter.Start(encounter, catalogs, player, runRng, clock, totalFloor);

    /// <inheritdoc cref="TurnRunner.StartPlayerTurn"/>
    public static void StartPlayerTurn(CombatContext ctx) =>
        TurnRunner.StartPlayerTurn(ctx);

    /// <inheritdoc cref="CardPlayer.PlayCard"/>
    public static void PlayerPlayCard(CombatContext ctx, uint cardInstanceId, CreatureId? targetEnemyId) =>
        CardPlayer.PlayCard(ctx, cardInstanceId, targetEnemyId);

    /// <inheritdoc cref="TurnRunner.EndPlayerTurn"/>
    public static void EndPlayerTurn(CombatContext ctx) =>
        TurnRunner.EndPlayerTurn(ctx);

    /// <inheritdoc cref="TurnRunner.EnemyTurn"/>
    public static void EnemyTurn(CombatContext ctx) =>
        TurnRunner.EnemyTurn(ctx);

    /// <inheritdoc cref="TurnRunner.CheckCombatEnd"/>
    public static void CheckCombatEnd(CombatContext ctx) =>
        TurnRunner.CheckCombatEnd(ctx);
}

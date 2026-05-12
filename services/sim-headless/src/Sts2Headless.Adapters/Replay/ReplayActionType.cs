namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// On-wire identifier for the action recorded in a replay entry. The u8 byte
/// value is part of the schema contract; existing variants must never change.
///
/// <para>
/// Phase-1 actions:
/// </para>
/// <list type="bullet">
///   <item><see cref="PlayCard"/> — a PlayerAction.PlayCard. action_data
///   encodes <c>u32 CardInstanceId</c> + <c>u8 HasTarget</c> + (if HasTarget=1)
///   <c>u32 TargetEnemyId</c>.</item>
///   <item><see cref="EndTurn"/> — a PlayerAction.EndTurn. action_data is empty.</item>
///   <item><see cref="EnemyMove"/> — sentinel for enemy intent resolution.
///   action_data is empty for Phase-1; future stages may embed per-enemy
///   choices.</item>
/// </list>
///
/// <para>
/// Future actions (run-level: card pick, map move, shop, event, rest, potion)
/// will be appended without breaking existing encoded files.
/// </para>
/// </summary>
public enum ReplayActionType : byte
{
    /// <summary>Player plays a card (optionally targeting an enemy).</summary>
    PlayCard = 0,

    /// <summary>Player ends their turn.</summary>
    EndTurn = 1,

    /// <summary>Enemy turn resolution; in Phase-1 the enemy's chosen move is
    /// implicit from intent + RNG state and the entry just marks the step.</summary>
    EnemyMove = 2,

    // Run-level actions reserved for S15+:
    //   CardPick = 16,
    //   MapMove  = 17,
    //   ShopBuy  = 18,
    //   EventChoice = 19,
    //   RestSiteChoice = 20,
    //   PotionUse = 21,
}

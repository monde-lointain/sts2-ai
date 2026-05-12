namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Where we are in the combat-turn lifecycle. Each phase maps to a discrete
/// engine step:
/// <list type="bullet">
///   <item><see cref="CombatStart"/> — pre-combat hooks fired (Anchor, Vajra,
///   etc.); deck shuffled; first hand drawn.</item>
///   <item><see cref="PlayerTurnStart"/> — energy refilled; per-turn-start
///   relics (BloodVial) fired; hand drawn; enemy intents resolved.</item>
///   <item><see cref="PlayerActing"/> — waiting for player to play cards / end
///   turn. The legal-action enumerator runs here.</item>
///   <item><see cref="PlayerTurnEnd"/> — discards hand; ticks down powers
///   (Poison, Vulnerable/Weak, etc.); fires turn-end hooks.</item>
///   <item><see cref="EnemyTurnStart"/> — bookkeeping before enemies act.</item>
///   <item><see cref="EnemyActing"/> — each enemy resolves its intent; new
///   intents resolved for next turn.</item>
///   <item><see cref="EnemyTurnEnd"/> — bookkeeping after enemies act;
///   transitions back to <see cref="PlayerTurnStart"/>.</item>
///   <item><see cref="CombatEnd"/> — terminal. Either victory (all enemies dead)
///   or defeat (player HP &lt;= 0).</item>
/// </list>
///
/// <para>
/// <b>Ordering matches upstream's <c>CombatManager.StartTurn</c> and
/// <c>ExecuteEnemyTurn</c>:</b> player turn start → player acting → player turn
/// end → enemy turn start → enemy acting → enemy turn end → back to player
/// turn start. <see cref="CombatEnd"/> is reached from any phase the moment
/// the win/loss condition is detected.
/// </para>
/// </summary>
public enum CombatPhase
{
    /// <summary>Pre-combat setup; the only phase before turn 1 starts.</summary>
    CombatStart = 0,
    PlayerTurnStart,
    PlayerActing,
    PlayerTurnEnd,
    EnemyTurnStart,
    EnemyActing,
    EnemyTurnEnd,
    CombatEnd,
}

namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Coarse intent kind shown to the player ahead of a monster move. Verbatim port of
/// the subset of upstream
/// <c>MegaCrit.Sts2.Core.MonsterMoves.Intents.IntentType</c>
/// needed by the smoke encounter (Cultists do Buff + SingleAttack only).
/// </summary>
public enum IntentKind
{
    None = 0,
    Attack,
    Buff,
    Debuff,
    Defend,
    Sleep,
    Stun,
    Unknown,
    DeathBlow,

    /// <summary>
    /// Monster intends to add status cards to the player's discard pile (e.g.,
    /// Chomper's SCREECH adds 3 Dazed). Stream-B-T3 adds the intent kind so
    /// rotations are byte-faithful; the resolver no-ops the card-pollution
    /// effect for Phase-1 (engine does not yet add cards mid-combat).
    /// </summary>
    Status,
}

/// <summary>
/// What the monster intends to do next turn. Triple of (kind, numeric value,
/// hit count). Matches upstream's <c>BuffIntent</c> / <c>SingleAttackIntent</c>
/// / <c>MultiAttackIntent</c> at the data level:
/// <c>SingleAttackIntent(damage)</c> → <c>(Attack, damage, 1)</c>;
/// <c>MultiAttackIntent(damage, hits)</c> → <c>(Attack, damage, hits)</c>;
/// <c>BuffIntent()</c> → <c>(Buff, 0, 0)</c>.
///
/// <para>
/// <b>Stream-B-T3 surface:</b> <see cref="HitCount"/> was added so multi-hit
/// attack intents (Chomper's CLAMP 2x8, Exoskeleton's SKITTER 3x1) can be
/// expressed without a separate IntentKind. Defaults to 0 for non-attack
/// intents and to 1 for the legacy <see cref="Attack(int)"/> factory.
/// </para>
/// </summary>
public readonly record struct Intent(IntentKind Kind, int Value, int HitCount = 0)
{
    /// <summary>Convenience for single-attack intents (one hit).</summary>
    public static Intent Attack(int damage) => new(IntentKind.Attack, damage, 1);

    /// <summary>
    /// Convenience for multi-attack intents (n hits at <paramref name="damage"/> each).
    /// Upstream: <c>MultiAttackIntent(damage, repeats)</c>.
    /// </summary>
    public static Intent MultiAttack(int damage, int hits) => new(IntentKind.Attack, damage, hits);

    /// <summary>Convenience for buff intents (no numeric payload).</summary>
    public static Intent Buff() => new(IntentKind.Buff, 0, 0);

    /// <summary>Convenience for status-card-pollution intents (e.g., Chomper SCREECH).</summary>
    public static Intent Status(int cards) => new(IntentKind.Status, cards, 0);

    /// <summary>Convenience for defend intents (block monster gains this turn).</summary>
    public static Intent Defend(int block) => new(IntentKind.Defend, block, 0);

    /// <summary>Convenience for debuff intents (e.g., Frail/Weak applied to player).</summary>
    public static Intent Debuff() => new(IntentKind.Debuff, 0, 0);
}

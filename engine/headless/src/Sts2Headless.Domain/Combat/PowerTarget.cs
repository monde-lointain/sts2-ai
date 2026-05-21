namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Which side of the board a <see cref="MonsterIntentPower"/> application targets.
/// Used by <see cref="MonsterIntent.AppliesPowers"/> to distinguish self-buffs from
/// player debuffs within the same move.
/// </summary>
/// <remarks>
/// <c>Self = 0</c> is the default so that pre-Wave-B serialized blobs (which do not
/// carry the target field) decode as <c>Self</c> after the schema version bump.
/// </remarks>
public enum PowerTarget
{
    /// <summary>Apply the power to the monster itself.</summary>
    Self = 0,

    /// <summary>Apply the power to the player.</summary>
    Player = 1,
}

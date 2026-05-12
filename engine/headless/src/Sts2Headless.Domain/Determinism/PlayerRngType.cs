namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Per-player RNG subsystems, ported verbatim from upstream
/// <c>MegaCrit.Sts2.Core.Entities.Rngs.PlayerRngType</c>. Ordering and member
/// names are part of the determinism contract — the snake_case name is hashed
/// into each subsystem's derived seed (via
/// <see cref="StringHelpers.GetDeterministicHashCode(string)"/>), so renaming
/// a member or changing the enum's numeric values is a state-schema break.
/// </summary>
public enum PlayerRngType
{
    Rewards,
    Shops,
    Transformations,
}

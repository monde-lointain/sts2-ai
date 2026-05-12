namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Per-run RNG subsystems, ported verbatim from upstream
/// <c>MegaCrit.Sts2.Core.Entities.Rngs.RunRngType</c>. As with
/// <see cref="PlayerRngType"/>, ordering and member names are determinism
/// contract — the snake_case name is hashed into each subsystem's derived
/// seed. Rename = state-schema break.
///
/// Note: the upstream <c>CombatOrbs</c> member maps to the property accessor
/// <c>CombatOrbGeneration</c> on <see cref="RunRngSet"/>. The enum value
/// keeps its upstream identifier (otherwise the hashed snake_case name
/// changes and produces a different derived seed).
/// </summary>
public enum RunRngType
{
    UpFront,
    Shuffle,
    UnknownMapPoint,
    CombatCardGeneration,
    CombatPotionGeneration,
    CombatCardSelection,
    CombatEnergyCosts,
    CombatTargets,
    MonsterAi,
    Niche,
    CombatOrbs,
    TreasureRoomRelics,
}

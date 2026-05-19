using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Monsters;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Monsters.SneakyGremlin</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Monsters/SneakyGremlin.cs).
///
/// <list type="bullet">
///   <item>HP: 10–14 (Ascension 0).</item>
///   <item>Rotation: SPAWNED_MOVE (Stun, wake-up no-op) → TACKLE_MOVE (Attack 9) → TACKLE_MOVE (self-loop).</item>
///   <item>Initial move: SPAWNED_MOVE (spawned into combat mid-fight via SurprisePower).</item>
///   <item>No spawn powers (spawned bare by SurprisePower's AfterDeath hook).</item>
/// </list>
///
/// <remarks>
/// Spawned by <see cref="SurprisePower"/> when GremlinMerc dies. SPAWNED_MOVE
/// is a <see cref="IntentKind.Stun"/> no-op turn (wake-up animation in upstream);
/// TACKLE_MOVE self-loops from turn 2 onward.
/// </remarks>
public sealed class SneakyGremlin : MonsterModel
{
    public const string CanonicalId = "SneakyGremlin";

    /// <summary>A0 MinInitialHp — upstream <c>GetValueIfAscension(ToughEnemies, 11, 10)</c>.</summary>
    public const int MinHp = 10;

    /// <summary>A0 MaxInitialHp — upstream <c>GetValueIfAscension(ToughEnemies, 15, 14)</c>.</summary>
    public const int MaxHp = 14;

    /// <summary>TACKLE_MOVE per-hit damage — upstream <c>GetValueIfAscension(DeadlyEnemies, 10, 9)</c> A0.</summary>
    public const int TackleDamage = 9;

    public const string SpawnedMoveId = "SPAWNED_MOVE";
    public const string TackleMoveId = "TACKLE_MOVE";

    public SneakyGremlin()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                // SPAWNED_MOVE: Stun intent (no-damage wake-up turn). Follow-up is TACKLE.
                // Upstream: StunIntent — IntentKind.Stun, no damage value.
                new(SpawnedMoveId, new Intent(IntentKind.Stun, 0), FollowUpMoveId: TackleMoveId),
                // TACKLE_MOVE: single attack 9. Self-loop from turn 2 onward.
                new(TackleMoveId, Intent.Attack(TackleDamage), FollowUpMoveId: TackleMoveId),
            },
            initialMoveId: SpawnedMoveId
        ) { }
}

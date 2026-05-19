using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Monsters;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Monsters.FatGremlin</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Monsters/FatGremlin.cs).
///
/// <list type="bullet">
///   <item>HP: 13–17 (Ascension 0).</item>
///   <item>Rotation: SPAWNED_MOVE (Stun, wake-up no-op) → FLEE_MOVE (Escape, removes self) → FLEE_MOVE (self-loop).</item>
///   <item>Initial move: SPAWNED_MOVE (spawned mid-combat via SurprisePower).</item>
///   <item>No spawn powers (spawned bare by SurprisePower's AfterDeath hook).</item>
/// </list>
///
/// <remarks>
/// Spawned by <see cref="SurprisePower"/> when GremlinMerc dies. SPAWNED_MOVE
/// is a <see cref="IntentKind.Stun"/> no-op turn; FLEE_MOVE maps to
/// <see cref="IntentKind.Unknown"/> as a Q1 placeholder for upstream's
/// <c>EscapeIntent</c> (no Q1 Escape mechanic yet; the escape is documentation-only
/// at Phase-1). The FLEE self-loop is byte-faithful to upstream's machine.
/// </remarks>
public sealed class FatGremlin : MonsterModel
{
    public const string CanonicalId = "FatGremlin";

    /// <summary>A0 MinInitialHp — upstream <c>GetValueIfAscension(ToughEnemies, 14, 13)</c>.</summary>
    public const int MinHp = 13;

    /// <summary>A0 MaxInitialHp — upstream <c>GetValueIfAscension(ToughEnemies, 18, 17)</c>.</summary>
    public const int MaxHp = 17;

    public const string SpawnedMoveId = "SPAWNED_MOVE";
    public const string FleeMoveId = "FLEE_MOVE";

    public FatGremlin()
        : base(
            CanonicalId,
            MinHp,
            MaxHp,
            new MonsterMove[]
            {
                // SPAWNED_MOVE: Stun intent (no-damage wake-up turn). Follow-up is FLEE.
                new(SpawnedMoveId, new Intent(IntentKind.Stun, 0), FollowUpMoveId: FleeMoveId),
                // FLEE_MOVE: Escape intent (removes self from combat in upstream).
                // Q1 maps EscapeIntent → Unknown (no engine Escape mechanic at Phase-1).
                // Self-loop (upstream: moveState2.FollowUpState = moveState2).
                new(FleeMoveId, new Intent(IntentKind.Unknown, 0), FollowUpMoveId: FleeMoveId),
            },
            initialMoveId: SpawnedMoveId
        ) { }
}

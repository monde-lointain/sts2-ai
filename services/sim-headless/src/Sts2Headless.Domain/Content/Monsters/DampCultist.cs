using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Monsters;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Monsters.DampCultist</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Monsters/DampCultist.cs):
/// </summary>
/// <list type="bullet">
///   <item>HP: 51–53 (Ascension 0). Upstream MinInitialHp / MaxInitialHp.</item>
///   <item>Move rotation: INCANTATION (BuffIntent) → DARK_STRIKE_MOVE
///         (SingleAttackIntent 1) → DARK_STRIKE_MOVE (self-loop).</item>
///   <item>INCANTATION applies 5 stacks of Ritual to self.</item>
///   <item>DARK_STRIKE deals 1 damage (Ascension 0; DeadlyEnemies bumps to 3).</item>
/// </list>
public sealed class DampCultist : MonsterModel
{
    public const string CanonicalId = "DampCultist";

    public const string IncantationMoveId = "INCANTATION_MOVE";
    public const string DarkStrikeMoveId = "DARK_STRIKE_MOVE";

    /// <summary>Upstream MinInitialHp (Ascension 0).</summary>
    public const int MinHp = 51;

    /// <summary>Upstream MaxInitialHp (Ascension 0).</summary>
    public const int MaxHp = 53;

    /// <summary>Upstream DarkStrikeDamage (Ascension 0).</summary>
    public const int DarkStrikeDamage = 1;

    /// <summary>Upstream IncantationAmount — 5 stacks of Ritual.</summary>
    public const int IncantationRitualStacks = 5;

    public DampCultist() : base(
        id: CanonicalId,
        minInitialHp: MinHp,
        maxInitialHp: MaxHp,
        moves: new MonsterMove[]
        {
            new(IncantationMoveId, Intent.Buff(), FollowUpMoveId: DarkStrikeMoveId),
            new(DarkStrikeMoveId, Intent.Attack(DarkStrikeDamage), FollowUpMoveId: DarkStrikeMoveId),
        },
        initialMoveId: IncantationMoveId)
    { }
}

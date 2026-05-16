using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Monsters;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Monsters.CalcifiedCultist</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Monsters/CalcifiedCultist.cs):
/// </summary>
/// <list type="bullet">
///   <item>HP: 38–41 (Ascension 0). Upstream MinInitialHp / MaxInitialHp.</item>
///   <item>Move rotation: INCANTATION (BuffIntent) → DARK_STRIKE_MOVE
///         (SingleAttackIntent 9) → DARK_STRIKE_MOVE (self-loop).</item>
///   <item>INCANTATION applies 2 stacks of Ritual to self.</item>
///   <item>DARK_STRIKE deals 9 damage (Ascension 0; DeadlyEnemies bumps to 11).</item>
/// </list>
/// <para>
/// Ascension-variant HP / damage are deferred to S12 / later (the smoke encounter
/// is Ascension 0 only).
/// </para>
public sealed class CalcifiedCultist : MonsterModel
{
    public const string CanonicalId = "CalcifiedCultist";

    public const string IncantationMoveId = "INCANTATION_MOVE";
    public const string DarkStrikeMoveId = "DARK_STRIKE_MOVE";

    /// <summary>Upstream MinInitialHp (Ascension 0).</summary>
    public const int MinHp = 38;

    /// <summary>Upstream MaxInitialHp (Ascension 0).</summary>
    public const int MaxHp = 41;

    /// <summary>Upstream DarkStrikeDamage (Ascension 0).</summary>
    public const int DarkStrikeDamage = 9;

    /// <summary>Upstream IncantationAmount — 2 stacks of Ritual.</summary>
    public const int IncantationRitualStacks = 2;

    public CalcifiedCultist()
        : base(
            id: CanonicalId,
            minInitialHp: MinHp,
            maxInitialHp: MaxHp,
            moves: new MonsterMove[]
            {
                new(IncantationMoveId, Intent.Buff(), FollowUpMoveId: DarkStrikeMoveId),
                new(
                    DarkStrikeMoveId,
                    Intent.Attack(DarkStrikeDamage),
                    FollowUpMoveId: DarkStrikeMoveId
                ),
            },
            initialMoveId: IncantationMoveId
        )
    { }
}

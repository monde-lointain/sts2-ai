using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Monsters;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Monsters.Nibbit</c>:
/// </summary>
/// <list type="bullet">
///   <item>HP: 42–46 (Ascension 0).</item>
///   <item>Move rotation: BUTT_MOVE (Attack 12) → SLICE_MOVE (Attack 6, +5 self-block)
///         → HISS_MOVE (Buff, +2 Strength) → BUTT_MOVE (repeats).</item>
///   <item>SLICE_MOVE side-effect: +5 self-block applied by the engine after damage.</item>
///   <item>HISS_MOVE: BuffIntent; engine applies 2 stacks of Strength to self.</item>
///   <item>InitialMoveId = BUTT_MOVE (NibbitsWeak uses this as-is;
///         NibbitsNormal overrides per slot).</item>
/// </list>
/// <remarks>
/// Per Q1-ADR-014: HISS Strength is applied via <c>CombatEngine.ExtractBuffEffect</c>
/// per-monster dispatch; SLICE self-block is applied via
/// <c>CombatEngine.ExtractAttackSelfBlock</c> per-monster dispatch. NibbitsNormal's
/// per-slot initial-move overrides flow through <c>EncounterModel.GenerateMonstersWithMoves</c>.
/// </remarks>
public sealed class Nibbit : MonsterModel
{
    public const string CanonicalId = "Nibbit";

    public const string ButtMoveId = "BUTT_MOVE";
    public const string SliceMoveId = "SLICE_MOVE";
    public const string HissMoveId = "HISS_MOVE";

    /// <summary>Upstream MinInitialHp (Ascension 0).</summary>
    public const int MinHp = 42;

    /// <summary>Upstream MaxInitialHp (Ascension 0).</summary>
    public const int MaxHp = 46;

    /// <summary>BUTT_MOVE damage (Ascension 0).</summary>
    public const int ButtDamage = 12;

    /// <summary>SLICE_MOVE damage (Ascension 0).</summary>
    public const int SliceDamage = 6;

    /// <summary>Block Nibbit gains on the SLICE_MOVE turn (self-block side-effect).</summary>
    public const int SliceSelfBlock = 5;

    /// <summary>Strength stacks applied by HISS_MOVE.</summary>
    public const int HissStrengthStacks = 2;

    public Nibbit()
        : base(
            id: CanonicalId,
            minInitialHp: MinHp,
            maxInitialHp: MaxHp,
            moves: new MonsterMove[]
            {
                new(ButtMoveId, Intent.Attack(ButtDamage), FollowUpMoveId: SliceMoveId),
                new(SliceMoveId, Intent.Attack(SliceDamage), FollowUpMoveId: HissMoveId),
                new(HissMoveId, Intent.Buff(), FollowUpMoveId: ButtMoveId),
            },
            initialMoveId: ButtMoveId
        ) { }
}

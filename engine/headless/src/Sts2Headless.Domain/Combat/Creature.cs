using System.Collections.Immutable;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// A combatant in a fight — player or monster. Holds HP, block, power stacks, and
/// for monsters the resolved-but-not-yet-executed intent. The runtime analogue of
/// upstream's <c>Creature</c> class
/// (~/development/projects/godot/sts2/src/Core/Entities/Creatures/Creature.cs:294).
///
/// <para>
/// <b>Cheap-clone friendly:</b> immutable <c>record</c> with primitive fields and
/// <see cref="ImmutableList{T}"/> for powers. <c>with</c>-expressions are O(1) for
/// scalars and structural-sharing for the power list.
/// </para>
///
/// <para>
/// <b>State-codec friendly:</b> field order is the S7 byte-serialization order:
/// <c>Id, Name, CurrentHp, MaxHp, Block, Powers, Intent, IsPlayer</c>.
/// </para>
///
/// <para>
/// <b>Player vs monster distinction:</b> <see cref="IsPlayer"/> is the flag.
/// Monsters carry an <see cref="Intent"/>; players' Intent is always null.
/// We do NOT split into Player / Monster subtypes — the unified shape makes
/// cheap-cloning and serialization simpler and matches upstream's single
/// <c>Creature</c> class (which also carries an <c>Intent</c>-shaped state via
/// the linked monster).
/// </para>
/// </summary>
/// <param name="Id">
/// Per-combat unique id. Assigned by <c>StartCombat</c>; stable across the fight.
/// Player typically id 0; enemies 1..N in spawn order.
/// </param>
/// <param name="Name">
/// Stable name (player class name or monster catalog id). Used for logs and
/// the canonical state hash; not user-facing.
/// </param>
/// <param name="CurrentHp">Current HP. Clamped at zero by damage application.</param>
/// <param name="MaxHp">Maximum HP. Combat-start value (modifiers apply at runtime).</param>
/// <param name="Block">Current block. Zeroed at owner's turn start.</param>
/// <param name="Powers">Power stack instances (ordered for deterministic hash).</param>
/// <param name="Intent">
/// For monsters: the resolved next-turn intent (null until <c>StartPlayerTurn</c>
/// pre-resolves it). For the player: always null.
/// </param>
/// <param name="IsPlayer">True for the player; false for monsters.</param>
public sealed record Creature(
    CreatureId Id,
    string Name,
    int CurrentHp,
    int MaxHp,
    int Block,
    ImmutableList<PowerInstance> Powers,
    MonsterIntent? Intent,
    bool IsPlayer
)
{
    /// <summary>True iff <see cref="CurrentHp"/> &gt; 0.</summary>
    public bool IsAlive => CurrentHp > 0;

    /// <summary>True iff <see cref="CurrentHp"/> &lt;= 0.</summary>
    public bool IsDead => !IsAlive;

    /// <summary>
    /// Override record-default equality so the <see cref="ImmutableList{T}"/>
    /// of powers is compared element-wise rather than by reference. Without
    /// this, two creatures with the same field values but distinct list
    /// references would not compare equal — making CombatState equality
    /// reference-dependent. Required for the state-hash machinery (S7) and
    /// for the smoke-harness golden trace.
    /// </summary>
    public bool Equals(Creature? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (!Id.Equals(other.Id) || Name != other.Name)
            return false;
        if (CurrentHp != other.CurrentHp || MaxHp != other.MaxHp || Block != other.Block)
            return false;
        if (IsPlayer != other.IsPlayer)
            return false;
        if (!Equals(Intent, other.Intent))
            return false;
        if (Powers.Count != other.Powers.Count)
            return false;
        for (int i = 0; i < Powers.Count; i++)
        {
            if (!Powers[i].Equals(other.Powers[i]))
                return false;
        }
        return true;
    }

    /// <summary>Override required to match overridden <see cref="Equals(Creature?)"/>.</summary>
    public override int GetHashCode()
    {
        HashCode h = default;
        h.Add(Id);
        h.Add(Name);
        h.Add(CurrentHp);
        h.Add(MaxHp);
        h.Add(Block);
        h.Add(IsPlayer);
        h.Add(Intent);
        for (int i = 0; i < Powers.Count; i++)
            h.Add(Powers[i]);
        return h.ToHashCode();
    }
}

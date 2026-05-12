using System.Collections.Immutable;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// The root combat-state aggregate. One value per fight, replaced on every
/// mutation via <c>with</c>-expressions. Encapsulates every piece of state that
/// must roundtrip through S7's State Codec bit-identically — every field here is
/// designed to byte-serialize deterministically in field-declaration order.
///
/// <para>
/// <b>Cheap-clone (S17) constraint:</b> every field is either an immutable
/// primitive, an <see cref="ImmutableList{T}"/>, or another immutable record.
/// A <c>state with { ... }</c> is the canonical clone — no explicit Clone method
/// is needed, and structural sharing means the cost is only the changed branch.
/// </para>
///
/// <para>
/// <b>State-codec (S7) constraint:</b> byte-serialization order is
/// <c>TurnCounter</c>, <c>Phase</c>, <c>Player</c>, <c>Enemies</c>,
/// <c>Energy</c>, <c>BaseEnergyPerTurn</c>, <c>HandDrawSize</c>,
/// <c>DrawPile</c>, <c>HandPile</c>, <c>DiscardPile</c>, <c>ExhaustPile</c>,
/// <c>PlayerRngCounter</c>, <c>MonsterRngCounter</c>. Add new fields at the end
/// in future stages to preserve replay compatibility.
/// </para>
///
/// <para>
/// <b>RNG counter snapshots:</b> S7 will use these to re-seed the RNG sources
/// on deserialize. The combat itself never reads these — they're recorded by
/// the engine when a mutation happens, then consumed by S7. Smoke set keeps
/// them at 0; the engine increments them in S6 to match what Rng consumed.
/// </para>
/// </summary>
/// <param name="TurnCounter">
/// Player-turn counter. Starts at 0 (combat-start) and increments at each
/// <c>StartPlayerTurn</c>. Turn 1 is the first player-acting phase.
/// </param>
/// <param name="Phase">Current <see cref="CombatPhase"/>.</param>
/// <param name="Player">The player creature.</param>
/// <param name="Enemies">
/// Enemy creatures in spawn order. Dead enemies remain in the list with
/// <c>CurrentHp=0</c>; they get filtered out by combat-side queries but stay
/// in the state for hash stability.
/// </param>
/// <param name="Energy">Current available energy.</param>
/// <param name="BaseEnergyPerTurn">Energy refilled at <c>StartPlayerTurn</c>.</param>
/// <param name="HandDrawSize">
/// Cards drawn at <c>StartPlayerTurn</c>. Starts at 5 (upstream's
/// <c>baseHandDrawCount</c>); modifiable by relics like RingOfTheSnake (+2)
/// before turn 1.
/// </param>
/// <param name="DrawPile">Cards still to be drawn (top at index 0).</param>
/// <param name="HandPile">Cards currently in the player's hand.</param>
/// <param name="DiscardPile">Discarded cards (reshuffled into Draw when empty).</param>
/// <param name="ExhaustPile">Exhausted cards (removed for the rest of combat).</param>
/// <param name="PlayerRngCounter">
/// Snapshot of the player-RNG counter at the last state mutation. S7 uses this
/// to re-seed on deserialize.
/// </param>
/// <param name="MonsterRngCounter">
/// Snapshot of the monster-RNG counter. Each monster has its own RNG upstream;
/// for Phase 1 smoke we collapse to a single counter since the smoke encounter
/// is deterministic (Cultists have no RNG-branching moves). S12 will split.
/// </param>
/// <param name="AttacksPlayedThisTurn">
/// Stream-B-T4: number of Attack-type cards the player has played in the
/// current player turn. Reset to 0 at <c>StartPlayerTurn</c>; incremented
/// per attack-card play. Powers calc-damage cards like Finisher
/// (<c>damage = base × attacks-played-this-turn</c>).
/// </param>
/// <param name="CardsDrawnThisCombat">
/// Stream-B-T4: cumulative card-draw count across all turns this combat.
/// Incremented per draw (whether by turn-start draw, relic, or card effect).
/// Powers calc-damage cards like Murder
/// (<c>damage = base × cards-drawn-this-combat</c>).
/// </param>
/// <param name="LastSpentEnergy">
/// B.1-gamma-T5: energy consumed by the most recently played X-cost card.
/// Engine snapshots <see cref="Energy"/> immediately before consumption when
/// the card is X-cost, then sets this field so the card's <c>OnPlay</c> body
/// can read it. Mirrors upstream's <c>CardModel.ResolveEnergyXValue()</c>.
/// Defaults to 0 (no X-cost card played yet this combat).
/// </param>
/// <param name="ExhaustedShivCount">
/// B.1-gamma-T5: count of Shiv-tagged cards that have landed in the exhaust
/// pile this combat. Drives KnifeTrap's calc-damage formula
/// (<c>damage = base + ExhaustedShivCount</c>). Q1's smoke set does not
/// produce Shivs (no Shiv-generating cards in the Silent starter deck),
/// so this counter stays at 0 in current scenarios — KnifeTrap then deals
/// its base damage. Wired ahead of Shiv-generator content.
/// </param>
public sealed record CombatState(
    int TurnCounter,
    CombatPhase Phase,
    Creature Player,
    ImmutableList<Creature> Enemies,
    int Energy,
    int BaseEnergyPerTurn,
    int HandDrawSize,
    CardPile DrawPile,
    CardPile HandPile,
    CardPile DiscardPile,
    CardPile ExhaustPile,
    int PlayerRngCounter,
    int MonsterRngCounter,
    int AttacksPlayedThisTurn = 0,
    int CardsDrawnThisCombat = 0,
    int LastSpentEnergy = 0,
    int ExhaustedShivCount = 0)
{
    /// <summary>
    /// Find the enemy with the given id; returns null if missing. Convenience for
    /// content code that needs to mutate a specific enemy without writing the
    /// LINQ search inline.
    /// </summary>
    public Creature? FindEnemy(uint id)
    {
        for (int i = 0; i < Enemies.Count; i++)
        {
            if (Enemies[i].Id == id) return Enemies[i];
        }
        return null;
    }

    /// <summary>
    /// Find the enemy with the given id or throw. Use when the caller knows the
    /// id must exist (e.g., card-play resolution with a target validated by the
    /// legal-action enumerator).
    /// </summary>
    public Creature GetEnemy(uint id)
    {
        Creature? enemy = FindEnemy(id);
        if (enemy is null)
        {
            throw new InvalidOperationException(
                $"CombatState: no enemy with id={id} (have ids: {string.Join(",", Enemies.Select(e => e.Id))}).");
        }
        return enemy;
    }

    /// <summary>
    /// Find the creature with the given id (player or enemy). Throws if unknown.
    /// </summary>
    public Creature GetCreature(uint id)
    {
        if (Player.Id == id) return Player;
        return GetEnemy(id);
    }

    /// <summary>True iff combat has reached terminal state (player dead or all enemies dead).</summary>
    public bool IsCombatOver => Phase == CombatPhase.CombatEnd;

    /// <summary>True iff combat ended in player victory (all enemies dead).</summary>
    public bool PlayerWon => IsCombatOver && Player.IsAlive && Enemies.All(e => e.IsDead);

    /// <summary>True iff combat ended in player defeat (player dead).</summary>
    public bool PlayerLost => IsCombatOver && Player.IsDead;

    /// <summary>
    /// Replace this state's player with <paramref name="updated"/>. Sugar over
    /// <c>state with { Player = updated }</c>; useful for chaining in
    /// <see cref="ICombatContext"/> mutations.
    /// </summary>
    public CombatState WithPlayer(Creature updated)
    {
        if (!updated.IsPlayer)
        {
            throw new ArgumentException(
                "WithPlayer requires a creature with IsPlayer=true.", nameof(updated));
        }
        return this with { Player = updated };
    }

    /// <summary>Replace one enemy by id, preserving order. Throws if not found.</summary>
    public CombatState WithEnemy(Creature updated)
    {
        if (updated.IsPlayer)
        {
            throw new ArgumentException(
                "WithEnemy requires a creature with IsPlayer=false.", nameof(updated));
        }
        int index = -1;
        for (int i = 0; i < Enemies.Count; i++)
        {
            if (Enemies[i].Id == updated.Id) { index = i; break; }
        }
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"WithEnemy: no enemy with id={updated.Id}.");
        }
        return this with { Enemies = Enemies.SetItem(index, updated) };
    }

    /// <summary>
    /// Override record-default equality so the <see cref="Enemies"/> list is
    /// compared element-wise. Combined with the per-creature, per-pile, and
    /// per-monster-intent equality overrides, this gives full structural
    /// equality across the CombatState graph — needed for the
    /// canonical-hash byte-serialization gate and for downstream state
    /// codec (S7) work.
    /// </summary>
    public bool Equals(CombatState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (TurnCounter != other.TurnCounter || Phase != other.Phase) return false;
        if (Energy != other.Energy || BaseEnergyPerTurn != other.BaseEnergyPerTurn) return false;
        if (HandDrawSize != other.HandDrawSize) return false;
        if (PlayerRngCounter != other.PlayerRngCounter || MonsterRngCounter != other.MonsterRngCounter) return false;
        if (AttacksPlayedThisTurn != other.AttacksPlayedThisTurn) return false;
        if (CardsDrawnThisCombat != other.CardsDrawnThisCombat) return false;
        if (LastSpentEnergy != other.LastSpentEnergy) return false;
        if (ExhaustedShivCount != other.ExhaustedShivCount) return false;
        if (!Player.Equals(other.Player)) return false;
        if (!DrawPile.Equals(other.DrawPile)) return false;
        if (!HandPile.Equals(other.HandPile)) return false;
        if (!DiscardPile.Equals(other.DiscardPile)) return false;
        if (!ExhaustPile.Equals(other.ExhaustPile)) return false;
        if (Enemies.Count != other.Enemies.Count) return false;
        for (int i = 0; i < Enemies.Count; i++)
        {
            if (!Enemies[i].Equals(other.Enemies[i])) return false;
        }
        return true;
    }

    /// <summary>Override required to match overridden <see cref="Equals(CombatState?)"/>.</summary>
    public override int GetHashCode()
    {
        HashCode h = default;
        h.Add(TurnCounter);
        h.Add(Phase);
        h.Add(Energy);
        h.Add(BaseEnergyPerTurn);
        h.Add(HandDrawSize);
        h.Add(PlayerRngCounter);
        h.Add(MonsterRngCounter);
        h.Add(AttacksPlayedThisTurn);
        h.Add(CardsDrawnThisCombat);
        h.Add(LastSpentEnergy);
        h.Add(ExhaustedShivCount);
        h.Add(Player);
        h.Add(DrawPile);
        h.Add(HandPile);
        h.Add(DiscardPile);
        h.Add(ExhaustPile);
        for (int i = 0; i < Enemies.Count; i++) h.Add(Enemies[i]);
        return h.ToHashCode();
    }
}

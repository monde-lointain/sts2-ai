namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Per-combat accumulator counters that update as cards are played and turns
/// pass. Extracted from <see cref="CombatState"/> in wave-41 to localize
/// "additive integer field" growth — a new counter touches this record, the
/// codec read/write pair, and the consumer call-site, not the 5 sites it
/// would have before (CombatState ctor + codec write + codec read + Equals +
/// GetHashCode).
///
/// <para>
/// <b>Type choice — value:</b> stack-allocated; <c>state with { Trail =
/// state.Trail with { LastSpentEnergy = c } }</c> avoids a heap allocation
/// per X-cost card play. Record-struct auto-generates value equality
/// (CA1815 / CA2231 satisfied under <c>latest-recommended</c> analyzers).
/// </para>
///
/// <para>
/// <b>Wire format:</b> <see cref="Sts2Headless.Adapters.StateCodec.StateCodec"/>
/// emits the four <see cref="int"/> fields in declaration order — byte-identical
/// to the pre-wave-41 flat-field layout. No
/// <see cref="Sts2Headless.Adapters.StateCodec.StateCodecConstants.SchemaVersion"/>
/// bump required.
/// </para>
/// </summary>
/// <param name="AttacksPlayedThisTurn">
/// Number of Attack-type cards the player has played in the current player
/// turn. Reset to 0 at <c>TurnRunner.StartPlayerTurn</c>; incremented per
/// attack-card play. Drives Finisher (<c>damage = base × attacks-played-this-turn</c>).
/// </param>
/// <param name="CardsDrawnThisCombat">
/// Cumulative card-draw count across all turns this combat. Incremented per
/// draw (turn-start, relic, or card effect). Drives Murder
/// (<c>damage = base × cards-drawn-this-combat</c>).
/// </param>
/// <param name="LastSpentEnergy">
/// Energy consumed by the most recently played X-cost card.
/// <see cref="CardPlayer.PlayCard"/> snapshots <c>CombatState.Energy</c>
/// immediately before consumption when the card is X-cost, then writes here
/// so the card's <c>OnPlay</c> body can read it. Mirrors upstream's
/// <c>CardModel.ResolveEnergyXValue()</c>.
/// </param>
/// <param name="ExhaustedShivCount">
/// Count of Shiv-tagged cards that have landed in the exhaust pile this
/// combat. Drives KnifeTrap's calc-damage formula
/// (<c>damage = base + ExhaustedShivCount</c>). Q1's smoke set does not
/// produce Shivs (no Shiv-generating cards in the Silent starter deck), so
/// this counter stays at 0 in current scenarios.
/// </param>
public readonly record struct TrailCounters(
    int AttacksPlayedThisTurn,
    int CardsDrawnThisCombat,
    int LastSpentEnergy,
    int ExhaustedShivCount);

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// A runtime stack of a <see cref="Sts2Headless.Domain.Content.Models.PowerModel"/>
/// living on a single <see cref="Creature"/>. The stack count is the per-instance
/// state — the catalog PowerModel is shared and stateless.
///
/// <para>
/// <b>JustApplied semantics:</b> matches upstream's <c>_wasJustAppliedByEnemy</c>
/// flag on <c>RitualPower</c> and the general "skip the turn applied" rule for
/// enemy buffs. Set true at apply time; cleared at the owner's turn-end. Smoke
/// set uses this for Ritual (Cultists' INCANTATION) — the Ritual stack applied
/// this turn must not yet grant Strength on this same turn-end.
/// </para>
///
/// <para>
/// <b>Cheap-clone / state-codec friendly:</b> primitives only; field order is the
/// S7 byte-serialization order.
/// </para>
/// </summary>
/// <param name="ModelId">
/// String id matching <c>PowerCatalog</c> (e.g., <c>"PoisonPower"</c>, <c>"RitualPower"</c>).
/// </param>
/// <param name="Stacks">Current stack count. Zero = expired (caller strips).</param>
/// <param name="SourceCreatureId">
/// Id of the creature that applied this power. Zero for combat-start grants
/// (player applies their own initial Strength via Vajra → source is themselves).
/// </param>
/// <param name="JustApplied">
/// True iff applied this turn; cleared at the owner's turn-end. Drives the
/// "skip first turn" rule for enemy buffs like Ritual.
/// </param>
public sealed record PowerInstance(
    string ModelId,
    int Stacks,
    CreatureId SourceCreatureId,
    bool JustApplied
);

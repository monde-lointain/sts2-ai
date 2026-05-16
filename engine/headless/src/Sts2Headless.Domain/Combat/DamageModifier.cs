using Sts2Headless.Domain.Content.Powers;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Canonical Strength→Vulnerable→Weak damage-modifier pipeline. Used by every
/// damage-applying path (card-played → enemy via EffectDispatcher; enemy attack
/// → player via CombatEngine). One implementation = bit-identical outcomes
/// regardless of dispatch route.
/// </summary>
public static class DamageModifier
{
    /// <summary>
    /// Apply Strength (additive on source) → Vulnerable (×1.5 on target) → Weak
    /// (×0.75 on source) → floor. Mirrors upstream's
    /// <c>ModifyDamageAdditive</c> + <c>ModifyDamageMultiplicative</c> hook
    /// order. Non-positive <paramref name="raw"/> is returned unchanged.
    /// </summary>
    public static int Modify(CombatState state, uint sourceId, uint targetId, int raw)
    {
        if (raw <= 0)
            return raw;

        Creature source = state.GetCreature(sourceId);
        Creature target = state.GetCreature(targetId);

        decimal amount = raw;
        for (int i = 0; i < source.Powers.Count; i++)
        {
            if (source.Powers[i].ModelId == PowerIds.Strength)
            {
                amount += source.Powers[i].Stacks;
            }
        }
        for (int i = 0; i < target.Powers.Count; i++)
        {
            if (target.Powers[i].ModelId == PowerIds.Vulnerable && target.Powers[i].Stacks > 0)
            {
                amount *= VulnerablePower.DamageIncrease;
            }
        }
        for (int i = 0; i < source.Powers.Count; i++)
        {
            if (source.Powers[i].ModelId == PowerIds.Weak && source.Powers[i].Stacks > 0)
            {
                amount *= WeakPower.DamageDecrease;
            }
        }
        return (int)System.Math.Floor(amount);
    }
}

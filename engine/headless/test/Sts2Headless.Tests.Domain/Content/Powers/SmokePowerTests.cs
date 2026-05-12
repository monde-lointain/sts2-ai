using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;

namespace Sts2Headless.Tests.Domain.Content.Powers;

/// <summary>
/// Metadata checks for every smoke-set power. Each test asserts canonical
/// catalog properties (id, type, stack type) and the per-power static damage
/// constants where applicable. Per-instance stack counts and damage modification
/// live on the combat-side <c>PowerInstance</c> / <c>EffectDispatcher</c>, so
/// behavioral assertions belong in combat-engine tests, not here.
/// Cross-reference upstream file paths in each power's class doc-comment.
/// </summary>
public class SmokePowerTests
{
    // ===== StrengthPower (upstream: src/Core/Models/Powers/StrengthPower.cs) =====

    [Fact]
    public void StrengthPower_canonical_properties()
    {
        StrengthPower p = new();
        Assert.Equal("StrengthPower", p.Id);
        Assert.Equal(PowerType.Buff, p.Type);
        Assert.Equal(PowerStackType.Counter, p.StackType);
    }

    // ===== VulnerablePower (upstream: src/Core/Models/Powers/VulnerablePower.cs) =====

    [Fact]
    public void VulnerablePower_canonical_properties()
    {
        VulnerablePower p = new();
        Assert.Equal("VulnerablePower", p.Id);
        Assert.Equal(PowerType.Debuff, p.Type);
        Assert.Equal(PowerStackType.Counter, p.StackType);
        Assert.Equal(1.5m, VulnerablePower.DamageIncrease);
    }

    // ===== WeakPower (upstream: src/Core/Models/Powers/WeakPower.cs) =====

    [Fact]
    public void WeakPower_canonical_properties()
    {
        WeakPower p = new();
        Assert.Equal("WeakPower", p.Id);
        Assert.Equal(PowerType.Debuff, p.Type);
        Assert.Equal(PowerStackType.Counter, p.StackType);
        Assert.Equal(0.75m, WeakPower.DamageDecrease);
    }

    // ===== PoisonPower (upstream: src/Core/Models/Powers/PoisonPower.cs) =====

    [Fact]
    public void PoisonPower_canonical_properties()
    {
        PoisonPower p = new();
        Assert.Equal("PoisonPower", p.Id);
        Assert.Equal(PowerType.Debuff, p.Type);
        Assert.Equal(PowerStackType.Counter, p.StackType);
    }

    // ===== RitualPower (upstream: src/Core/Models/Powers/RitualPower.cs) =====

    [Fact]
    public void RitualPower_canonical_properties()
    {
        RitualPower p = new();
        Assert.Equal("RitualPower", p.Id);
        Assert.Equal(PowerType.Buff, p.Type);
        Assert.Equal(PowerStackType.Counter, p.StackType);
    }

    [Fact]
    public void RitualPower_skips_first_turn_end_after_application()
    {
        RitualPower p = new();
        p.MarkJustApplied();
        // The turn it was applied: should NOT grant strength.
        Assert.False(p.ShouldGrantStrengthThisTurnEnd());
        // Subsequent turn ends: should grant strength.
        Assert.True(p.ShouldGrantStrengthThisTurnEnd());
        Assert.True(p.ShouldGrantStrengthThisTurnEnd());
    }

    [Fact]
    public void RitualPower_without_MarkJustApplied_grants_strength_immediately()
    {
        RitualPower p = new();
        Assert.True(p.ShouldGrantStrengthThisTurnEnd());
    }
}

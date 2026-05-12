// Tests for the HookType enum. Per S4 spec, this enum must cover the full
// upstream hook surface (~150 values) so later stages (S5+) don't trigger mass
// renames. Names are PascalCase verbatim from upstream
// godot/sts2/src/Core/Hooks/Hook.cs callsites on AbstractModel.

using System;
using System.Linq;
using Sts2Headless.Domain.Actions;

namespace Sts2Headless.Tests.Domain.Actions;

public class HookTypeTests
{
    [Fact]
    public void EnumHasAtLeast150Values()
    {
        // Stage S4 spec: "Full ~150-entry HookType enum".
        var values = Enum.GetValues<HookType>();
        Assert.True(values.Length >= 150,
            $"HookType must have >=150 values to cover upstream Hook.cs surface; found {values.Length}");
    }

    [Fact]
    public void EnumHasNoDuplicateNamesOrValues()
    {
        var names = Enum.GetNames<HookType>();
        Assert.Equal(names.Length, names.Distinct().Count());
        var values = Enum.GetValues<HookType>().Cast<int>().ToArray();
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    // Sample upstream callsites — every name here must exist verbatim. If
    // upstream renames one of these we want this test to fail loudly so the
    // S13 probe doesn't silently diverge.
    [Theory]
    [InlineData("AfterCardPlayed")]
    [InlineData("AfterCardPlayedLate")]
    [InlineData("BeforeCardPlayed")]
    [InlineData("AfterDamageReceived")]
    [InlineData("AfterDamageReceivedLate")]
    [InlineData("BeforeDamageReceived")]
    [InlineData("AfterPlayerTurnStartEarly")]
    [InlineData("AfterPlayerTurnStart")]
    [InlineData("AfterPlayerTurnStartLate")]
    [InlineData("BeforeTurnEnd")]
    [InlineData("BeforeTurnEndEarly")]
    [InlineData("BeforeTurnEndVeryEarly")]
    [InlineData("AfterTurnEnd")]
    [InlineData("AfterTurnEndLate")]
    [InlineData("BeforeCombatStart")]
    [InlineData("BeforeCombatStartLate")]
    [InlineData("AfterCombatEnd")]
    [InlineData("AfterCombatVictory")]
    [InlineData("AfterCombatVictoryEarly")]
    [InlineData("ModifyDamageAdditive")]
    [InlineData("ModifyDamageMultiplicative")]
    [InlineData("ModifyDamageCap")]
    [InlineData("ModifyBlockAdditive")]
    [InlineData("ModifyBlockMultiplicative")]
    [InlineData("ShouldDie")]
    [InlineData("ShouldDieLate")]
    [InlineData("AfterDeath")]
    [InlineData("BeforeDeath")]
    [InlineData("AfterPreventingDeath")]
    [InlineData("AfterPreventingBlockClear")]
    [InlineData("TryModifyCardBeingAddedToDeck")]
    [InlineData("TryModifyCardBeingAddedToDeckLate")]
    public void NameExistsInEnum(string name)
    {
        Assert.True(Enum.TryParse<HookType>(name, ignoreCase: false, out _),
            $"HookType.{name} missing — upstream Hook.cs callsite is not represented");
    }

    [Fact]
    public void NoneIsZeroAndSentinel()
    {
        // The None sentinel is convenient for "no hook" flags / default(struct).
        Assert.Equal(0, (int)HookType.None);
    }
}

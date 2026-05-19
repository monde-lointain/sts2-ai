using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;

namespace Sts2Headless.Tests.Domain.Content.Powers;

/// <summary>
/// Byte-faithful metadata checks for S12 Phase-1 powers. Asserts each power's
/// canonical id + Buff/Debuff type + Counter/Single stack type per upstream's
/// <c>~/development/projects/godot/sts2/src/Core/Models/Powers/*.cs</c>.
/// </summary>
public class Phase1PowerTests
{
    [Theory]
    [InlineData(typeof(ThornsPower), "ThornsPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(DexterityPower), "DexterityPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(IntangiblePower), "IntangiblePower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(ArtifactPower), "ArtifactPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(FrailPower), "FrailPower", PowerType.Debuff, PowerStackType.Counter)]
    [InlineData(typeof(AccelerantPower), "AccelerantPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(AccuracyPower), "AccuracyPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(AfterimagePower), "AfterimagePower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(EnvenomPower), "EnvenomPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(
        typeof(SerpentFormPower),
        "SerpentFormPower",
        PowerType.Buff,
        PowerStackType.Counter
    )]
    [InlineData(typeof(OutbreakPower), "OutbreakPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(
        typeof(PhantomBladesPower),
        "PhantomBladesPower",
        PowerType.Buff,
        PowerStackType.Counter
    )]
    [InlineData(typeof(StranglePower), "StranglePower", PowerType.Debuff, PowerStackType.Counter)]
    [InlineData(typeof(SneakyPower), "SneakyPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(SpeedsterPower), "SpeedsterPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(
        typeof(WraithFormPower),
        "WraithFormPower",
        PowerType.Debuff,
        PowerStackType.Counter
    )]
    [InlineData(typeof(BurstPower), "BurstPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(
        typeof(MasterPlannerPower),
        "MasterPlannerPower",
        PowerType.Buff,
        PowerStackType.Single
    )]
    [InlineData(
        typeof(InfiniteBladesPower),
        "InfiniteBladesPower",
        PowerType.Buff,
        PowerStackType.Counter
    )]
    [InlineData(
        typeof(FanOfKnivesPower),
        "FanOfKnivesPower",
        PowerType.Buff,
        PowerStackType.Single
    )]
    [InlineData(typeof(TrackingPower), "TrackingPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(
        typeof(ToolsOfTheTradePower),
        "ToolsOfTheTradePower",
        PowerType.Buff,
        PowerStackType.Counter
    )]
    [InlineData(typeof(CurlUpPower), "CurlUpPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(AngerPower), "AngerPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(EnragePower), "EnragePower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(
        typeof(MetallicizePower),
        "MetallicizePower",
        PowerType.Buff,
        PowerStackType.Counter
    )]
    [InlineData(
        typeof(PlatedArmorPower),
        "PlatedArmorPower",
        PowerType.Buff,
        PowerStackType.Counter
    )]
    [InlineData(typeof(RegenPower), "RegenPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(ModeShiftPower), "ModeShiftPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(PenNibPower), "PenNibPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(EnergizedPower), "EnergizedPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(typeof(BlockPower), "BlockPower", PowerType.Buff, PowerStackType.Counter)]
    [InlineData(
        typeof(DexterityLossPower),
        "DexterityLossPower",
        PowerType.Debuff,
        PowerStackType.Counter
    )]
    [InlineData(
        typeof(StrengthDownPower),
        "StrengthDownPower",
        PowerType.Debuff,
        PowerStackType.Counter
    )]
    [InlineData(typeof(EntangledPower), "EntangledPower", PowerType.Debuff, PowerStackType.Counter)]
    [InlineData(typeof(NoBlockPower), "NoBlockPower", PowerType.Debuff, PowerStackType.Counter)]
    [InlineData(typeof(NoDrawPower), "NoDrawPower", PowerType.Debuff, PowerStackType.Single)]
    [InlineData(typeof(ConfusedPower), "ConfusedPower", PowerType.Debuff, PowerStackType.Single)]
    [InlineData(typeof(BarricadePower), "BarricadePower", PowerType.Buff, PowerStackType.Single)]
    [InlineData(typeof(BlurPower), "BlurPower", PowerType.Buff, PowerStackType.Counter)]
    // Wave-26/Q1.D: SurprisePower (Buff/Single) + ThieveryPower (Buff/Counter) for GremlinMerc.
    [InlineData(typeof(SurprisePower), "SurprisePower", PowerType.Buff, PowerStackType.Single)]
    [InlineData(typeof(ThieveryPower), "ThieveryPower", PowerType.Buff, PowerStackType.Counter)]
    public void Power_canonical_metadata(
        System.Type t,
        string expectedId,
        PowerType type,
        PowerStackType stack
    )
    {
        PowerModel p = (PowerModel)System.Activator.CreateInstance(t)!;
        Assert.Equal(expectedId, p.Id);
        Assert.Equal(type, p.Type);
        Assert.Equal(stack, p.StackType);
    }

    [Fact]
    public void Phase1Content_power_catalog_contains_all_smoke_plus_s12_powers()
    {
        PowerCatalog catalog = Phase1Content.BuildPowerCatalog();
        Assert.True(catalog.Count >= 40, $"expected >=40 powers, got {catalog.Count}");
        Assert.True(catalog.Contains(PowerIds.Poison));
        Assert.True(catalog.Contains(PowerIds.Thorns));
        Assert.True(catalog.Contains(PowerIds.WraithForm));
    }
}

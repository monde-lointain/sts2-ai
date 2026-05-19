using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Powers;

/// <summary>
/// Bulk-port of Phase-1 powers from upstream
/// <c>~/development/projects/godot/sts2/src/Core/Models/Powers/*.cs</c>.
/// Each power captures its canonical id + Buff/Debuff type + Counter/Single stack
/// type per upstream's <c>public override PowerType Type</c> / <c>StackType</c>
/// overrides. Trigger hooks (on-hit, on-turn-start, modify-damage) are wired in S13
/// alongside the determinism probe.
/// </summary>
public sealed class ThornsPower : PowerModel
{
    public ThornsPower()
        : base(PowerIds.Thorns, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class DexterityPower : PowerModel
{
    public DexterityPower()
        : base(PowerIds.Dexterity, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class IntangiblePower : PowerModel
{
    public IntangiblePower()
        : base(PowerIds.Intangible, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class ArtifactPower : PowerModel
{
    public ArtifactPower()
        : base(PowerIds.Artifact, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class FrailPower : PowerModel
{
    /// <summary>Frail multiplies block gained by 0.75.</summary>
    public const decimal BlockMultiplier = 0.75m;

    public FrailPower()
        : base(PowerIds.Frail, PowerType.Debuff, PowerStackType.Counter) { }
}

public sealed class AccelerantPower : PowerModel
{
    public AccelerantPower()
        : base(PowerIds.Accelerant, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class AccuracyPower : PowerModel
{
    public AccuracyPower()
        : base(PowerIds.Accuracy, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class AfterimagePower : PowerModel
{
    public AfterimagePower()
        : base(PowerIds.Afterimage, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class EnvenomPower : PowerModel
{
    public EnvenomPower()
        : base(PowerIds.Envenom, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class SerpentFormPower : PowerModel
{
    public SerpentFormPower()
        : base(PowerIds.SerpentForm, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class OutbreakPower : PowerModel
{
    public OutbreakPower()
        : base(PowerIds.Outbreak, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class PhantomBladesPower : PowerModel
{
    public PhantomBladesPower()
        : base(PowerIds.PhantomBlades, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class StranglePower : PowerModel
{
    public StranglePower()
        : base(PowerIds.Strangle, PowerType.Debuff, PowerStackType.Counter) { }
}

public sealed class SneakyPower : PowerModel
{
    public SneakyPower()
        : base(PowerIds.Sneaky, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class SpeedsterPower : PowerModel
{
    public SpeedsterPower()
        : base(PowerIds.Speedster, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class WraithFormPower : PowerModel
{
    public WraithFormPower()
        : base(PowerIds.WraithForm, PowerType.Debuff, PowerStackType.Counter) { }
}

public sealed class BurstPower : PowerModel
{
    public BurstPower()
        : base(PowerIds.Burst, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class MasterPlannerPower : PowerModel
{
    public MasterPlannerPower()
        : base(PowerIds.MasterPlanner, PowerType.Buff, PowerStackType.Single) { }
}

public sealed class InfiniteBladesPower : PowerModel
{
    public InfiniteBladesPower()
        : base(PowerIds.InfiniteBlades, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class FanOfKnivesPower : PowerModel
{
    public FanOfKnivesPower()
        : base(PowerIds.FanOfKnives, PowerType.Buff, PowerStackType.Single) { }
}

public sealed class TrackingPower : PowerModel
{
    public TrackingPower()
        : base(PowerIds.Tracking, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class ToolsOfTheTradePower : PowerModel
{
    public ToolsOfTheTradePower()
        : base(PowerIds.ToolsOfTheTrade, PowerType.Buff, PowerStackType.Counter) { }
}

// === Monster-side powers ===
public sealed class CurlUpPower : PowerModel
{
    public CurlUpPower()
        : base(PowerIds.CurlUp, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class AngerPower : PowerModel
{
    public AngerPower()
        : base(PowerIds.AngerPower, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class EnragePower : PowerModel
{
    public EnragePower()
        : base(PowerIds.Enrage, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class MetallicizePower : PowerModel
{
    public MetallicizePower()
        : base(PowerIds.Metallicize, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class PlatedArmorPower : PowerModel
{
    public PlatedArmorPower()
        : base(PowerIds.Plated, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class RegenPower : PowerModel
{
    public RegenPower()
        : base(PowerIds.Regen, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class ModeShiftPower : PowerModel
{
    public ModeShiftPower()
        : base(PowerIds.ModeShift, PowerType.Buff, PowerStackType.Counter) { }
}

// === Relic-driven powers ===
public sealed class PenNibPower : PowerModel
{
    public PenNibPower()
        : base(PowerIds.PenNib, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class EnergizedPower : PowerModel
{
    public EnergizedPower()
        : base(PowerIds.Energized, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class VigorPower : PowerModel
{
    public VigorPower()
        : base(PowerIds.Vigor, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class FocusPower : PowerModel
{
    public FocusPower()
        : base(PowerIds.Focus, PowerType.Buff, PowerStackType.Counter) { }
}

// === Misc debuffs/buffs ===
public sealed class BlockPower : PowerModel
{
    public BlockPower()
        : base(PowerIds.Block, PowerType.Buff, PowerStackType.Counter) { }
}

public sealed class DexterityLossPower : PowerModel
{
    public DexterityLossPower()
        : base(PowerIds.DexterityLoss, PowerType.Debuff, PowerStackType.Counter) { }
}

public sealed class StrengthDownPower : PowerModel
{
    public StrengthDownPower()
        : base(PowerIds.StrengthDown, PowerType.Debuff, PowerStackType.Counter) { }
}

public sealed class EntangledPower : PowerModel
{
    public EntangledPower()
        : base(PowerIds.Entangled, PowerType.Debuff, PowerStackType.Counter) { }
}

public sealed class NoBlockPower : PowerModel
{
    public NoBlockPower()
        : base(PowerIds.NoBlock, PowerType.Debuff, PowerStackType.Counter) { }
}

public sealed class NoDrawPower : PowerModel
{
    public NoDrawPower()
        : base(PowerIds.NoDraw, PowerType.Debuff, PowerStackType.Single) { }
}

public sealed class ConfusedPower : PowerModel
{
    public ConfusedPower()
        : base(PowerIds.Confused, PowerType.Debuff, PowerStackType.Single) { }
}

public sealed class BarricadePower : PowerModel
{
    public BarricadePower()
        : base("BarricadePower", PowerType.Buff, PowerStackType.Single) { }
}

public sealed class BlurPower : PowerModel
{
    public BlurPower()
        : base("BlurPower", PowerType.Buff, PowerStackType.Counter) { }
}

// === GremlinMerc companion powers (wave-26/Q1.D) ===

/// <summary>
/// Metadata-only stub for upstream <c>MegaCrit.Sts2.Core.Models.Powers.ThieveryPower</c>.
/// Upstream: gold-stealing mechanic — tracks a gold amount per player target and
/// transfers it to the player when GremlinMerc dies (via HeistPower on FatGremlin).
/// Gold-tracking and Heist transfer are Phase-2 deferred (no oracle impact;
/// see ADR-030). Registered so the engine does not fail-soft on GremlinMerc's spawn.
///
/// <para>StackType=Counter per upstream (gold amount accumulates per steal).</para>
/// </summary>
public sealed class ThieveryPower : PowerModel
{
    public ThieveryPower()
        : base(PowerIds.Thievery, PowerType.Buff, PowerStackType.Counter) { }
}

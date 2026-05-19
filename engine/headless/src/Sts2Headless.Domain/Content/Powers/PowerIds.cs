namespace Sts2Headless.Domain.Content.Powers;

/// <summary>
/// Canonical string ids for every power in the Phase-1 content set. Matches upstream
/// <c>ModelId.Entry</c> values (PascalCase verbatim — see
/// <c>MegaCrit.Sts2.Core.Models.Powers.*</c>). Centralising these constants here
/// keeps catalog registration, Q4 manifest, and card OnPlay implementations agreed
/// on the spelling.
///
/// <para>S5 smoke set: Poison, Vulnerable, Weak, Strength, Ritual.
/// S12 Phase-1 expansion: every additional power id referenced by S12 cards/relics/
/// monsters.</para>
/// </summary>
public static class PowerIds
{
    // ===== S5 smoke set =====
    public const string Poison = "PoisonPower";
    public const string Vulnerable = "VulnerablePower";
    public const string Weak = "WeakPower";
    public const string Strength = "StrengthPower";
    public const string Ritual = "RitualPower";

    // ===== S12 Phase-1 expansion =====
    public const string Thorns = "ThornsPower";
    public const string Dexterity = "DexterityPower";
    public const string Accelerant = "AccelerantPower";
    public const string Accuracy = "AccuracyPower";
    public const string Afterimage = "AfterimagePower";
    public const string Envenom = "EnvenomPower";
    public const string SerpentForm = "SerpentFormPower";
    public const string Outbreak = "OutbreakPower";
    public const string PhantomBlades = "PhantomBladesPower";
    public const string Strangle = "StranglePower";
    public const string Sneaky = "SneakyPower";
    public const string Speedster = "SpeedsterPower";
    public const string Intangible = "IntangiblePower";
    public const string WraithForm = "WraithFormPower";
    public const string Burst = "BurstPower";
    public const string MasterPlanner = "MasterPlannerPower";
    public const string InfiniteBlades = "InfiniteBladesPower";
    public const string FanOfKnives = "FanOfKnivesPower";
    public const string Tracking = "TrackingPower";
    public const string ToolsOfTheTrade = "ToolsOfTheTradePower";
    public const string Nightmare = "NightmarePower";
    public const string Malaise = "MalaisePower";
    public const string Block = "BlockPower";
    public const string Frail = "FrailPower";
    public const string Artifact = "ArtifactPower";
    public const string DexterityLoss = "DexterityLossPower";
    public const string StrengthDown = "StrengthDownPower";
    public const string Entangled = "EntangledPower";
    public const string NoBlock = "NoBlockPower";
    public const string NoDraw = "NoDrawPower";
    public const string Confused = "ConfusedPower";

    // ===== GremlinMerc spawn powers (wave-26/Q1.D) =====
    /// <summary>SurprisePower — AfterDeath spawn of SneakyGremlin + FatGremlin. StackType=Single.</summary>
    public const string Surprise = "SurprisePower";

    /// <summary>ThieveryPower — gold-steal metadata stub. Gold-tracking deferred to Phase-2 per ADR-030.</summary>
    public const string Thievery = "ThieveryPower";

    // ===== Monster-specific powers used by enemies =====
    public const string CurlUp = "CurlUpPower";
    public const string SplitPower = "SplitPower";
    public const string AngerPower = "AngerPower";
    public const string Enrage = "EnragePower";
    public const string Metallicize = "MetallicizePower";
    public const string Plated = "PlatedArmorPower";
    public const string Regen = "RegenPower";
    public const string ModeShift = "ModeShiftPower";

    // ===== Relic-driven powers =====
    public const string PenNib = "PenNibPower";
    public const string Energized = "EnergizedPower";

    // ===== Relic-applied buff powers (B.1-gamma-T4) =====
    /// <summary>Akabeko's +8 Vigor at side-turn-start (first turn).</summary>
    public const string Vigor = "VigorPower";

    /// <summary>DataDisk's +1 Focus on combat start (orb damage).</summary>
    public const string Focus = "FocusPower";
}

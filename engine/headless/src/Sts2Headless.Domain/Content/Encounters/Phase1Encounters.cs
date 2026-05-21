using System.Collections.Generic;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Content.Encounters;

/// <summary>
/// Phase-1 Acts 1-3 representative encounters. Each encounter is a thin
/// <see cref="EncounterModel"/> that spawns the monsters referenced by id. Order
/// matters — combat positions monsters left-to-right by spawn order, matching
/// upstream <c>GenerateMonsters</c>.
///
/// <para>
/// <b>Why a single bulk file:</b> the spec asks for ~20 representative encounters
/// alongside the smoke <c>CultistsNormal</c>. The encounter shape is uniform
/// (just an ordered monster-id list), so one file with all 20 keeps registration
/// review-friendly. Full move-rotation behaviour wires in S13.
/// </para>
/// </summary>
public sealed class ChompersNormal : EncounterModel
{
    public const string CanonicalId = "ChompersNormal";

    public ChompersNormal()
        : base(CanonicalId, new[] { Chomper.CanonicalId, Chomper.CanonicalId }) { }
}

/// <summary>
/// Upstream Exoskeleton uses ConditionalBranchState("INIT_MOVE") keyed on
/// <c>Creature.SlotName</c>: "first"→SKITTER, "second"→MANDIBLES,
/// "third"→ENRAGE, "fourth"→RAND. ExoskeletonsNormal spawns exactly 3, so
/// slots are first/second/third. Q1 handles per-slot override at encounter
/// level via <see cref="EncounterModel.GenerateMonstersWithMoves"/> (Nibbit
/// precedent). wave-49/A.3.
/// </summary>
public sealed class ExoskeletonsNormal : EncounterModel
{
    public const string CanonicalId = "ExoskeletonsNormal";

    public ExoskeletonsNormal()
        : base(
            CanonicalId,
            new[] { Exoskeleton.CanonicalId, Exoskeleton.CanonicalId, Exoskeleton.CanonicalId }
        ) { }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a fixed list; no Rng ticks.
    /// Slot 0 ("first")  → SKITTER_MOVE; slot 1 ("second") → MANDIBLES_MOVE;
    /// slot 2 ("third")  → ENRAGE_MOVE.
    /// Matches upstream ConditionalBranchState("INIT_MOVE") for the 3-slot case.
    /// </remarks>
    public override IReadOnlyList<(
        string MonsterId,
        string? InitialMoveIdOverride
    )> GenerateMonstersWithMoves(Rng rng) =>
        new (string, string?)[]
        {
            (Exoskeleton.CanonicalId, Exoskeleton.SkitterMoveId),
            (Exoskeleton.CanonicalId, Exoskeleton.MandiblesMoveId),
            (Exoskeleton.CanonicalId, Exoskeleton.EnrageMoveId),
        };
}

// B.1-final-T2a: deleted JawWormSolo (STS1-only monster JawWorm, no STS2 analogue).
// B.1-final-T2a: deleted TwoLouseNormal (STS1 monsters RedLouse + GreenLouse,
// no STS2 analogue; upstream STS2 has LouseProgenitor — added as separate encounter).
// B.1-final-T2a: deleted LargeSlimeBoss (STS1 monsters AcidSlimeL + SpikeSlimeL,
// no STS2 large-slime analogue).

// B.1-ε RESOLVED Wave 14 — SmallSlimes and MediumSlimes ported from upstream
// SlimesWeak / SlimesNormal byte-exact RNG patterns. See commit below.
//
// SmallSlimes = upstream SlimesWeak: 3 NextItem ticks from encounter Rng.
//   Pool small  = { LeafSlimeS, TwigSlimeS }; pool medium = { LeafSlimeM, TwigSlimeM }.
//   tick-1: small1 = NextItem(smallPool) + remove; tick-2: small2 = NextItem(smallPool remaining);
//   tick-3: medium = NextItem(mediumPool). Output = [small1, medium, small2].
//
// MediumSlimes = upstream SlimesNormal: 1 NextBool tick from encounter Rng.
//   flag=true  → [TwigSlimeM, LeafSlimeM, LeafSlimeS, TwigSlimeS]
//   flag=false → [TwigSlimeM, LeafSlimeM, TwigSlimeS, LeafSlimeS]

/// <summary>
/// Port of upstream <c>Encounters.SlimesWeak</c>. Overrides
/// <see cref="EncounterModel.GenerateMonsters(Rng)"/> to consume 3 <c>NextItem</c>
/// ticks from the per-encounter <see cref="Rng"/>, byte-exact with upstream.
/// Static <c>MonsterIds</c> lists LeafSlimeS as first monster for catalog purposes;
/// the actual spawn list is determined at runtime by the Rng override.
/// </summary>
public sealed class SmallSlimes : EncounterModel
{
    public const string CanonicalId = "SmallSlimes";

    // Upstream type name slugified per ModelDb.GetEntry(type) + Slugify:
    // SlimesWeak → "SLIMES_WEAK". Used by EncounterRngKey so ForEncounter
    // produces the same seed as upstream GenerateMonstersWithSlots.
    public const string UpstreamRngKey = "SLIMES_WEAK";

    // Static spawn list used by base EncounterModel for MonsterIds catalog
    // (fixture tests, manifest). Rng override produces the actual encounter list.
    private static readonly string[] _smallPool =
    {
        LeafSlimeS.CanonicalId,
        TwigSlimeS.CanonicalId,
    };
    private static readonly string[] _mediumPool =
    {
        LeafSlimeM.CanonicalId,
        TwigSlimeM.CanonicalId,
    };

    public SmallSlimes()
        // Canonical static spawn list: first small + one medium + second small.
        // Actual encounter uses GenerateMonsters(Rng) override below.
        : base(
            CanonicalId,
            new[] { LeafSlimeS.CanonicalId, LeafSlimeM.CanonicalId, TwigSlimeS.CanonicalId }
        ) { }

    /// <summary>
    /// Upstream slug for the encounter Rng seed. Matches
    /// <c>ModelDb.GetEntry(typeof(SlimesWeak))</c> = <c>Slugify("SlimesWeak")</c>
    /// = <c>"SLIMES_WEAK"</c> so <c>ForEncounter</c> produces the byte-exact seed.
    /// </summary>
    public override string EncounterRngKey => UpstreamRngKey;

    /// <summary>
    /// Byte-exact port of upstream <c>SlimesWeak.GenerateMonsters()</c>.
    /// Consumes 3 <c>NextItem</c> ticks: small1 from {LeafSlimeS, TwigSlimeS} (remove
    /// pattern); small2 from remaining; medium from {LeafSlimeM, TwigSlimeM}.
    /// Output order: [small1, medium, small2].
    /// </summary>
    public override IReadOnlyList<string> GenerateMonsters(Rng rng)
    {
        System.ArgumentNullException.ThrowIfNull(rng);
        var smallPool = new List<string>(_smallPool);
        // tick 1: pick first small slime, remove from pool
        string small1 = rng.NextItem<string>(smallPool)!;
        smallPool.Remove(small1);
        // tick 2: pick second small slime from remaining (1-element list; still a tick)
        string small2 = rng.NextItem<string>(smallPool)!;
        // tick 3: pick medium slime
        string medium = rng.NextItem<string>(_mediumPool)!;
        return new[] { small1, medium, small2 };
    }
}

/// <summary>
/// Port of upstream <c>Encounters.SlimesNormal</c>. Overrides
/// <see cref="EncounterModel.GenerateMonsters(Rng)"/> to consume 1 <c>NextBool</c>
/// tick from the per-encounter <see cref="Rng"/>, byte-exact with upstream.
/// </summary>
public sealed class MediumSlimes : EncounterModel
{
    public const string CanonicalId = "MediumSlimes";

    // Upstream type name slugified per ModelDb.GetEntry(type) + Slugify:
    // SlimesNormal → "SLIMES_NORMAL". Used by EncounterRngKey so ForEncounter
    // produces the same seed as upstream GenerateMonstersWithSlots.
    public const string UpstreamRngKey = "SLIMES_NORMAL";

    public MediumSlimes()
        // Canonical static spawn list: all 4 slimes; actual encounter uses Rng override.
        : base(
            CanonicalId,
            new[]
            {
                TwigSlimeM.CanonicalId,
                LeafSlimeM.CanonicalId,
                LeafSlimeS.CanonicalId,
                TwigSlimeS.CanonicalId,
            }
        ) { }

    /// <summary>
    /// Upstream slug for the encounter Rng seed. Matches
    /// <c>ModelDb.GetEntry(typeof(SlimesNormal))</c> = <c>Slugify("SlimesNormal")</c>
    /// = <c>"SLIMES_NORMAL"</c> so <c>ForEncounter</c> produces the byte-exact seed.
    /// </summary>
    public override string EncounterRngKey => UpstreamRngKey;

    /// <summary>
    /// Byte-exact port of upstream <c>SlimesNormal.GenerateMonsters()</c>.
    /// Consumes 1 <c>NextBool</c> tick.
    /// flag=true  → [TwigSlimeM, LeafSlimeM, LeafSlimeS, TwigSlimeS]
    /// flag=false → [TwigSlimeM, LeafSlimeM, TwigSlimeS, LeafSlimeS]
    /// </summary>
    public override IReadOnlyList<string> GenerateMonsters(Rng rng)
    {
        System.ArgumentNullException.ThrowIfNull(rng);
        // tick 1: NextBool picks which small is first
        bool flag = rng.NextBool();
        string small1 = flag ? LeafSlimeS.CanonicalId : TwigSlimeS.CanonicalId;
        string small2 = flag ? TwigSlimeS.CanonicalId : LeafSlimeS.CanonicalId;
        return new[] { TwigSlimeM.CanonicalId, LeafSlimeM.CanonicalId, small1, small2 };
    }
}

public sealed class BowlbugsTrio : EncounterModel
{
    public const string CanonicalId = "BowlbugsTrio";

    public BowlbugsTrio()
        : base(
            CanonicalId,
            new[] { BowlbugRock.CanonicalId, BowlbugNectar.CanonicalId, BowlbugSilk.CanonicalId }
        ) { }
}

public sealed class FuzzyWurmCrawlerSolo : EncounterModel
{
    public const string CanonicalId = "FuzzyWurmCrawlerSolo";

    public FuzzyWurmCrawlerSolo()
        : base(CanonicalId, new[] { FuzzyWurmCrawler.CanonicalId }) { }
}

public sealed class FossilStalkerElite : EncounterModel
{
    public const string CanonicalId = "FossilStalkerElite";

    public FossilStalkerElite()
        : base(CanonicalId, new[] { FossilStalker.CanonicalId }) { }
}

public sealed class FrogKnightElite : EncounterModel
{
    public const string CanonicalId = "FrogKnightElite";

    public FrogKnightElite()
        : base(CanonicalId, new[] { FrogKnight.CanonicalId }) { }
}

public sealed class LagavulinElite : EncounterModel
{
    public const string CanonicalId = "LagavulinElite";

    public LagavulinElite()
        : base(CanonicalId, new[] { LagavulinMatriarch.CanonicalId }) { }
}

// B.1-final-T2a: deleted SentryTrio (STS1-only monster Sentry, no STS2 analogue).

public sealed class HauntedShipSolo : EncounterModel
{
    public const string CanonicalId = "HauntedShipSolo";

    public HauntedShipSolo()
        : base(CanonicalId, new[] { HauntedShip.CanonicalId }) { }
}

public sealed class LivingFogSolo : EncounterModel
{
    public const string CanonicalId = "LivingFogSolo";

    public LivingFogSolo()
        : base(CanonicalId, new[] { LivingFog.CanonicalId }) { }
}

/// <summary>
/// Wave-26/Q1.D fix: upstream GremlinMercNormal spawns a single GremlinMerc
/// (one "merc" slot per upstream Encounters/GremlinMercNormal.cs:28).
/// The prior stub incorrectly used 2×GremlinMerc — corrected here to 1×.
/// SneakyGremlin + FatGremlin are spawned mid-combat by SurprisePower on GremlinMerc's death.
/// </summary>
public sealed class GremlinMercNormal : EncounterModel
{
    public const string CanonicalId = "GremlinMercNormal";

    public GremlinMercNormal()
        : base(CanonicalId, new[] { GremlinMerc.CanonicalId }) { }
}

// B.1-final-T2a: deleted SnakePlantSolo / FungalBossEncounter / CenturyGuardBoss
// (STS1-only monsters SnakePlant / FungalBoss / CenturyGuard, no STS2 analogues).

// B.1-final-T2b: KaiserCrabBoss reshape — upstream spawns Crusher + Rocket
// (left arm + right arm), NOT a single "KaiserCrab" monster (which is Q1-invented).
// Slots match upstream encounter file: "crusher" + "rocket".
public sealed class KaiserCrabBoss : EncounterModel
{
    public const string CanonicalId = "KaiserCrabBoss";

    public KaiserCrabBoss()
        : base(CanonicalId, new[] { Crusher.CanonicalId, Rocket.CanonicalId }) { }
}

// B.1-final-T2c: LouseProgenitorNormal — upstream encounter spawning a single
// LouseProgenitor (Q1 already has the monster; γ ported its rotation). Adds 10
// upstream-comparable initial-state seeds to the probe corpus.
public sealed class LouseProgenitorNormal : EncounterModel
{
    public const string CanonicalId = "LouseProgenitorNormal";

    public LouseProgenitorNormal()
        : base(CanonicalId, new[] { LouseProgenitor.CanonicalId }) { }
}

public sealed class CeremonialBeastBoss : EncounterModel
{
    public const string CanonicalId = "CeremonialBeastBoss";

    public CeremonialBeastBoss()
        : base(CanonicalId, new[] { CeremonialBeast.CanonicalId }) { }
}

/// <summary>
/// One-stop helper: pass an empty <see cref="EncounterCatalog"/>, get back the
/// catalog populated with all S12 Phase-1 encounters. Idempotent across processes.
/// </summary>
public static class Phase1EncountersRegistration
{
    public static void RegisterAll(EncounterCatalog encounters)
    {
        System.ArgumentNullException.ThrowIfNull(encounters);
        encounters.Register(ChompersNormal.CanonicalId, new ChompersNormal());
        encounters.Register(ExoskeletonsNormal.CanonicalId, new ExoskeletonsNormal());
        // B.1-final-T2a: removed JawWormSolo, TwoLouseNormal, LargeSlimeBoss (STS1-only).
        encounters.Register(SmallSlimes.CanonicalId, new SmallSlimes());
        encounters.Register(MediumSlimes.CanonicalId, new MediumSlimes());
        encounters.Register(BowlbugsTrio.CanonicalId, new BowlbugsTrio());
        encounters.Register(FuzzyWurmCrawlerSolo.CanonicalId, new FuzzyWurmCrawlerSolo());
        encounters.Register(FossilStalkerElite.CanonicalId, new FossilStalkerElite());
        encounters.Register(FrogKnightElite.CanonicalId, new FrogKnightElite());
        encounters.Register(LagavulinElite.CanonicalId, new LagavulinElite());
        // B.1-final-T2a: removed SentryTrio (STS1-only).
        encounters.Register(HauntedShipSolo.CanonicalId, new HauntedShipSolo());
        encounters.Register(LivingFogSolo.CanonicalId, new LivingFogSolo());
        encounters.Register(GremlinMercNormal.CanonicalId, new GremlinMercNormal());
        // B.1-final-T2a: removed SnakePlantSolo, FungalBossEncounter, CenturyGuardBoss (STS1-only).
        encounters.Register(KaiserCrabBoss.CanonicalId, new KaiserCrabBoss());
        encounters.Register(CeremonialBeastBoss.CanonicalId, new CeremonialBeastBoss());
        // B.1-final-T2c: added LouseProgenitorNormal (STS2 upstream encounter; monster
        // already present in Q1 from γ).
        encounters.Register(LouseProgenitorNormal.CanonicalId, new LouseProgenitorNormal());
        // Wave-24/K.q1: Nibbit encounters ported from upstream.
        encounters.Register(NibbitsWeak.CanonicalId, new NibbitsWeak());
        encounters.Register(NibbitsNormal.CanonicalId, new NibbitsNormal());
    }
}

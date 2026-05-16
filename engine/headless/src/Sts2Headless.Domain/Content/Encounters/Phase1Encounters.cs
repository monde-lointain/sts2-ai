using Sts2Headless.Domain.Content.Monsters;

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

public sealed class ExoskeletonsNormal : EncounterModel
{
    public const string CanonicalId = "ExoskeletonsNormal";

    public ExoskeletonsNormal()
        : base(
            CanonicalId,
            new[] { Exoskeleton.CanonicalId, Exoskeleton.CanonicalId, Exoskeleton.CanonicalId }
        ) { }
}

// B.1-final-T2a: deleted JawWormSolo (STS1-only monster JawWorm, no STS2 analogue).
// B.1-final-T2a: deleted TwoLouseNormal (STS1 monsters RedLouse + GreenLouse,
// no STS2 analogue; upstream STS2 has LouseProgenitor — added as separate encounter).
// B.1-final-T2a: deleted LargeSlimeBoss (STS1 monsters AcidSlimeL + SpikeSlimeL,
// no STS2 large-slime analogue).

// SmallSlimes + MediumSlimes preserved as DEFER (architectural blocker — encounter
// requires per-encounter Rng plumbing in upstream's GenerateMonsters(Rng); to be
// addressed in B.1-ε once architectural lift lands). They stay registered with
// static spawn lists, but their EncounterCatalog plan stays MissingUpstream so
// upstream-byte-comparison skips them.
public sealed class SmallSlimes : EncounterModel
{
    public const string CanonicalId = "SmallSlimes";

    public SmallSlimes()
        : base(CanonicalId, new[] { AcidSlimeS.CanonicalId, SpikeSlimeS.CanonicalId }) { }
}

public sealed class MediumSlimes : EncounterModel
{
    public const string CanonicalId = "MediumSlimes";

    public MediumSlimes()
        : base(CanonicalId, new[] { AcidSlimeM.CanonicalId, SpikeSlimeM.CanonicalId }) { }
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

public sealed class GremlinMercNormal : EncounterModel
{
    public const string CanonicalId = "GremlinMercNormal";

    public GremlinMercNormal()
        : base(CanonicalId, new[] { GremlinMerc.CanonicalId, GremlinMerc.CanonicalId }) { }
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
    }
}

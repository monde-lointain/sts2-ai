using System.Collections.Generic;
using System.Linq;

namespace Sts2Headless.UpstreamCapture;

/// <summary>
/// Maps Q1 encounter ids (from the determinism-probe phase1-corpus.json) to
/// the upstream MegaCrit.Sts2 type names needed to drive
/// <c>CombatState.CreateCreature</c>.
///
/// <para>
/// <b>Stream C canary:</b> Q1 invented some encounters whose monster
/// composition pre-dates STS2 (STS1-derived sets such as <c>JawWormSolo</c>,
/// <c>SmallSlimes</c>, <c>SentryTrio</c>, <c>FungalBossEncounter</c>,
/// <c>CenturyGuardBoss</c>). Those monsters DO NOT exist in upstream STS2 —
/// so byte-exact upstream capture is impossible. We tag them
/// <see cref="PlanKind.MissingUpstream"/> and report them as the canary the
/// Stream-C prompt requires.
/// </para>
/// </summary>
public static class EncounterCatalog
{
    /// <summary>Kind of resolution for a Q1 encounter id.</summary>
    public enum PlanKind
    {
        /// <summary>
        /// All Q1 monsters in this encounter map 1:1 to upstream
        /// <c>MegaCrit.Sts2.Core.Models.Monsters.*</c> classes; we can drive
        /// the upstream <c>SetUpCombat</c> path and capture canonical bytes.
        /// </summary>
        UpstreamComparable,

        /// <summary>
        /// At least one monster is missing upstream (STS1-derived or otherwise
        /// not present in the STS2 ModelDb). We record the reason and skip
        /// capture. This is documented divergence, not error.
        /// </summary>
        MissingUpstream,

        /// <summary>
        /// Encounter uses per-seed RNG to generate the monster list (upstream
        /// calls <c>EncounterModel.GenerateMonsters(base.Rng)</c>).  We
        /// drive the upstream encounter class directly via reflection —
        /// calling <c>GenerateMonstersWithSlots(runState)</c> — rather than
        /// hard-coding a static monster list.  <see cref="EncounterPlan.UpstreamTypeName"/>
        /// holds the fully-qualified upstream encounter type name.
        /// </summary>
        UpstreamEncounterRng,
    }

    /// <summary>Resolved plan for one encounter.</summary>
    public sealed record EncounterPlan(
        string EncounterId,
        IReadOnlyList<string> MonsterIds,
        IReadOnlyList<string?> Slots,
        PlanKind Kind,
        string? Reason,
        string? UpstreamTypeName = null,
        /// <summary>
        /// Optional filename (relative to <c>goldens-upstream/mid-combat/action-sequences/</c>)
        /// of the scripted action sequence used by the mid-combat probe (wave-45/Q1-A1 §2.2).
        /// Null for encounters that have no mid-combat golden yet.
        /// </summary>
        string? MidCombatActionSequenceId = null
    );

    /// <summary>
    /// The Phase-1 encounter ids per Q1's <c>phase1-corpus.json</c>. Order
    /// matches the corpus's structural-N entry order.
    ///
    /// <para>B.1-final-T2: 7 STS1-only encounters deleted (JawWormSolo,
    /// TwoLouseNormal, LargeSlimeBoss, SentryTrio, SnakePlantSolo,
    /// FungalBossEncounter, CenturyGuardBoss); 1 added (LouseProgenitorNormal).
    /// SmallSlimes / MediumSlimes preserved as DEFER pending B.1-ε encounter-RNG
    /// plumbing.</para>
    /// </summary>
    public static IReadOnlyList<string> AllKnownIds() =>
        new[]
        {
            "CultistsNormal",
            "ChompersNormal",
            "ExoskeletonsNormal",
            "SmallSlimes",
            "MediumSlimes",
            "BowlbugsTrio",
            "FuzzyWurmCrawlerSolo",
            "FossilStalkerElite",
            "FrogKnightElite",
            "LagavulinElite",
            "HauntedShipSolo",
            "LivingFogSolo",
            "GremlinMercNormal",
            "KaiserCrabBoss",
            "CeremonialBeastBoss",
            "LouseProgenitorNormal",
            // wave-47a/C: NibbitsWeak + NibbitsNormal (substrate wired wave-38/B;
            // no V1 divergence — upstream Nibbit INIT_MOVE conditional produces
            // exactly BUTT for IsAlone + SLICE/HISS per IsFront flag; Q1 port matches).
            "NibbitsWeak",
            "NibbitsNormal",
        };

    /// <summary>
    /// Resolve a Q1 encounter id to an upstream monster type list.
    /// Returns <see cref="PlanKind.MissingUpstream"/> with a documented reason
    /// when the encounter cannot be reproduced in upstream STS2.
    /// </summary>
    public static EncounterPlan Resolve(string id)
    {
        return id switch
        {
            // --- Encounters whose Q1 monster set maps cleanly to upstream STS2.
            "CultistsNormal" => new EncounterPlan(
                id,
                new[] { "CalcifiedCultist", "DampCultist" },
                new string?[] { null, null },
                PlanKind.UpstreamComparable,
                null,
                MidCombatActionSequenceId: "cultist-strategy.json"
            ),
            "ChompersNormal" => new EncounterPlan(
                id,
                new[] { "Chomper", "Chomper" },
                new string?[] { null, null },
                PlanKind.UpstreamComparable,
                null
            ),
            "ExoskeletonsNormal" => new EncounterPlan(
                id,
                new[] { "Exoskeleton", "Exoskeleton", "Exoskeleton" },
                new string?[] { null, null, null },
                PlanKind.UpstreamComparable,
                null,
                MidCombatActionSequenceId: "exoskeletons-normal-strategy.json"
            ),
            "BowlbugsTrio" => new EncounterPlan(
                id,
                new[] { "BowlbugRock", "BowlbugNectar", "BowlbugSilk" },
                new string?[] { null, null, null },
                PlanKind.UpstreamComparable,
                null
            ),
            "FuzzyWurmCrawlerSolo" => new EncounterPlan(
                id,
                new[] { "FuzzyWurmCrawler" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null
            ),
            "FossilStalkerElite" => new EncounterPlan(
                id,
                new[] { "FossilStalker" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null
            ),
            "FrogKnightElite" => new EncounterPlan(
                id,
                new[] { "FrogKnight" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null
            ),
            // Q1 and upstream both use "LagavulinMatriarch" as the monster class
            // (B.1-final-T1 renamed Q1's class from "Lagavulin" → "LagavulinMatriarch").
            "LagavulinElite" => new EncounterPlan(
                id,
                new[] { "LagavulinMatriarch" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null,
                MidCombatActionSequenceId: "lagavulin-elite-strategy.json"
            ),
            "HauntedShipSolo" => new EncounterPlan(
                id,
                new[] { "HauntedShip" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null
            ),
            "LivingFogSolo" => new EncounterPlan(
                id,
                new[] { "LivingFog" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null
            ),
            // Wave-26/Q1.D: 1 GremlinMerc initial (SneakyGremlin + FatGremlin spawn mid-combat).
            "GremlinMercNormal" => new EncounterPlan(
                id,
                new[] { "GremlinMerc" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null,
                MidCombatActionSequenceId: "gremlin-merc-normal-strategy.json"
            ),
            // B.1-final-T2b: KaiserCrabBoss reshaped to upstream's two-monster
            // spawn (Crusher + Rocket). Q1 monster classes ported from upstream
            // (Phase1Monsters.cs:Crusher / Rocket).
            "KaiserCrabBoss" => new EncounterPlan(
                id,
                new[] { "Crusher", "Rocket" },
                new string?[] { "crusher", "rocket" },
                PlanKind.UpstreamComparable,
                null,
                MidCombatActionSequenceId: "kaiser-crab-boss-strategy.json"
            ),
            "CeremonialBeastBoss" => new EncounterPlan(
                id,
                new[] { "CeremonialBeast" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null,
                MidCombatActionSequenceId: "ceremonial-beast-boss-strategy.json"
            ),
            // B.1-final-T2c: LouseProgenitorNormal added — upstream encounter
            // with single LouseProgenitor spawn (Q1 monster class already exists
            // from γ).
            "LouseProgenitorNormal" => new EncounterPlan(
                id,
                new[] { "LouseProgenitor" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null,
                MidCombatActionSequenceId: "louse-progenitor-normal-strategy.json"
            ),
            // wave-47a/C: NibbitsWeak — 1 Nibbit (IsAlone=true), starts BUTT_MOVE.
            // Upstream NibbitsWeak.cs:20-23: single Nibbit, IsAlone=true; INIT_MOVE
            // branches to BUTT_MOVE via ConditionalBranchState.IsAlone predicate.
            // Q1 port has no initial-move override; default BUTT_MOVE correct.
            "NibbitsWeak" => new EncounterPlan(
                id,
                new[] { "Nibbit" },
                new string?[] { null },
                PlanKind.UpstreamComparable,
                null,
                MidCombatActionSequenceId: "nibbits-weak-strategy.json"
            ),
            // wave-47a/C: NibbitsNormal — 2 Nibbits with per-slot initial-move
            // overrides matching upstream Nibbit.GenerateMoveStateMachine:
            //   Slot 0 (front, IsFront=true) → SLICE_MOVE (ConditionalBranch IsFront predicate).
            //   Slot 1 (back, IsFront=false) → HISS_MOVE (ConditionalBranch !IsFront predicate).
            // Upstream NibbitsNormal.cs:24-29: front slot has IsFront=true; back is default.
            // No V1 divergence — Q1 port GenerateMonstersWithMoves returns SLICE/HISS per slot.
            "NibbitsNormal" => new EncounterPlan(
                id,
                new[] { "Nibbit", "Nibbit" },
                new string?[] { "front", "back" },
                PlanKind.UpstreamComparable,
                null,
                MidCombatActionSequenceId: "nibbits-normal-strategy.json"
            ),
            // B.1-ε RESOLVED Wave 14: SmallSlimes / MediumSlimes now drive the
            // upstream encounter's GenerateMonstersWithSlots via UpstreamEncounterRng.
            // MonsterIds is a sentinel (all four slimes) — the actual spawn list is
            // determined at capture time by the upstream encounter's RNG.
            "SmallSlimes" => new EncounterPlan(
                id,
                new[] { "LeafSlimeS", "TwigSlimeS", "LeafSlimeM", "TwigSlimeM" },
                new string?[] { null, null, null, null },
                PlanKind.UpstreamEncounterRng,
                null,
                UpstreamTypeName: "MegaCrit.Sts2.Core.Models.Encounters.SlimesWeak"
            ),
            "MediumSlimes" => new EncounterPlan(
                id,
                new[] { "TwigSlimeM", "LeafSlimeM", "LeafSlimeS", "TwigSlimeS" },
                new string?[] { null, null, null, null },
                PlanKind.UpstreamEncounterRng,
                null,
                UpstreamTypeName: "MegaCrit.Sts2.Core.Models.Encounters.SlimesNormal"
            ),
            _ => throw new System.ArgumentException(
                $"unknown encounter id '{id}'; --list-encounters to enumerate."
            ),
        };
    }

    /// <summary>
    /// Convenience: count of encounters in each kind class for a given list.
    /// </summary>
    public static (int comparable, int missing) Tally(IEnumerable<string> ids)
    {
        int c = 0,
            m = 0;
        foreach (string id in ids)
        {
            switch (Resolve(id).Kind)
            {
                case PlanKind.UpstreamComparable:
                case PlanKind.UpstreamEncounterRng:
                    c++;
                    break;
                case PlanKind.MissingUpstream:
                    m++;
                    break;
            }
        }
        return (c, m);
    }
}

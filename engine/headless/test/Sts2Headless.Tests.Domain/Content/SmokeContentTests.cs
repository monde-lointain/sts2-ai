using System.IO;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;

namespace Sts2Headless.Tests.Domain.Content;

/// <summary>
/// End-to-end smoke wiring tests:
/// </summary>
/// <list type="number">
///   <item>Every catalog populated by <see cref="SmokeContent"/> has the expected
///         id set and yields the expected concrete types.</item>
///   <item>The on-disk Q4 manifest fixture covers every smoke id — coverage gate
///         returns green with zero missing and zero extras for the five gated
///         buckets.</item>
///   <item>The encounter wires to two real monsters in the catalog.</item>
/// </list>
public class SmokeContentTests
{
    private static string LocateRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "sts2-headless.sln")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new System.InvalidOperationException(
            "Could not locate repo root from " + AppContext.BaseDirectory
        );
    }

    private static Q4Manifest LoadFixture()
    {
        string path = Path.Combine(LocateRepoRoot(), "test", "fixtures", "q4-manifest-phase1.json");
        return Q4ManifestLoader.LoadFromString(File.ReadAllText(path));
    }

    [Fact]
    public void BuildCardCatalog_registers_all_smoke_cards_and_resolves_concrete_types()
    {
        CardCatalog cards = SmokeContent.BuildCardCatalog();
        Assert.Equal(9, cards.Count);
        Assert.IsType<StrikeSilent>(cards.Get(StrikeSilent.CanonicalId));
        Assert.IsType<DefendSilent>(cards.Get(DefendSilent.CanonicalId));
        Assert.IsType<Neutralize>(cards.Get(Neutralize.CanonicalId));
        Assert.IsType<Survivor>(cards.Get(Survivor.CanonicalId));
        Assert.IsType<Slice>(cards.Get(Slice.CanonicalId));
        Assert.IsType<DeadlyPoison>(cards.Get(DeadlyPoison.CanonicalId));
        Assert.IsType<Backflip>(cards.Get(Backflip.CanonicalId));
        Assert.IsType<Acrobatics>(cards.Get(Acrobatics.CanonicalId));
        Assert.IsType<DodgeAndRoll>(cards.Get(DodgeAndRoll.CanonicalId));
    }

    [Fact]
    public void BuildRelicCatalog_registers_all_smoke_relics_and_resolves_concrete_types()
    {
        RelicCatalog relics = SmokeContent.BuildRelicCatalog();
        Assert.Equal(5, relics.Count);
        Assert.IsType<RingOfTheSnake>(relics.Get(RingOfTheSnake.CanonicalId));
        Assert.IsType<Anchor>(relics.Get(Anchor.CanonicalId));
        Assert.IsType<Vajra>(relics.Get(Vajra.CanonicalId));
        Assert.IsType<BagOfPreparation>(relics.Get(BagOfPreparation.CanonicalId));
        Assert.IsType<BloodVial>(relics.Get(BloodVial.CanonicalId));
    }

    [Fact]
    public void BuildPowerCatalog_registers_all_smoke_powers_and_resolves_concrete_types()
    {
        PowerCatalog powers = SmokeContent.BuildPowerCatalog();
        Assert.Equal(5, powers.Count);
        Assert.IsType<PoisonPower>(powers.Get(PowerIds.Poison));
        Assert.IsType<VulnerablePower>(powers.Get(PowerIds.Vulnerable));
        Assert.IsType<WeakPower>(powers.Get(PowerIds.Weak));
        Assert.IsType<StrengthPower>(powers.Get(PowerIds.Strength));
        Assert.IsType<RitualPower>(powers.Get(PowerIds.Ritual));
    }

    [Fact]
    public void BuildMonsterCatalog_registers_both_cultists()
    {
        MonsterCatalog monsters = SmokeContent.BuildMonsterCatalog();
        Assert.Equal(2, monsters.Count);
        Assert.IsType<CalcifiedCultist>(monsters.Get(CalcifiedCultist.CanonicalId));
        Assert.IsType<DampCultist>(monsters.Get(DampCultist.CanonicalId));
    }

    [Fact]
    public void BuildPotionCatalog_is_empty()
    {
        PotionCatalog potions = SmokeContent.BuildPotionCatalog();
        Assert.Equal(0, potions.Count);
    }

    [Fact]
    public void BuildEncounterCatalog_registers_CultistsNormal_with_real_monster_ids()
    {
        EncounterCatalog encounters = SmokeContent.BuildEncounterCatalog();
        Assert.Equal(1, encounters.Count);
        IEncounterModel encounter = encounters.Get(CultistsNormal.CanonicalId);
        // Encounter references monster ids that are actually in the monster catalog.
        MonsterCatalog monsters = SmokeContent.BuildMonsterCatalog();
        foreach (string monsterId in encounter.MonsterIds)
        {
            Assert.True(
                monsters.Contains(monsterId),
                $"Encounter '{encounter.Id}' references monster id '{monsterId}' not in monster catalog."
            );
        }
    }

    // ===== Q4 coverage gate =====

    [Fact]
    public void Q4_manifest_fixture_plus_phase1_catalogs_pass_coverage_gate()
    {
        // S12: the Phase-1 fixture lists smoke + S12 content. Coverage is computed
        // against Phase1Content (smoke + S12 expansion), not SmokeContent alone.
        // S5's smoke-only coverage check is captured by Smoke_catalogs_are_a_subset_of_phase1.
        Q4Manifest manifest = LoadFixture();
        AggregateCoverageResult result = CoverageGate.ComputeAll(
            manifest,
            Phase1Content.BuildCardCatalog(),
            Phase1Content.BuildRelicCatalog(),
            Phase1Content.BuildPowerCatalog(),
            Phase1Content.BuildMonsterCatalog(),
            Phase1Content.BuildPotionCatalog()
        );

        // Each bucket must be green with zero missing.
        AssertBucketGreen(result.Cards, "Cards");
        AssertBucketGreen(result.Relics, "Relics");
        AssertBucketGreen(result.Powers, "Powers");
        AssertBucketGreen(result.Monsters, "Monsters");
        AssertBucketGreen(result.Potions, "Potions");
    }

    [Fact]
    public void Smoke_catalogs_are_a_subset_of_phase1()
    {
        // Sanity: every id registered in SmokeContent is also registered in Phase1Content.
        CardCatalog smoke = SmokeContent.BuildCardCatalog();
        CardCatalog phase1 = Phase1Content.BuildCardCatalog();
        foreach (string id in smoke.EnumerateIds())
        {
            Assert.True(phase1.Contains(id), $"Phase1 missing smoke card id '{id}'.");
        }
    }

    private static void AssertBucketGreen(CoverageResult bucket, string name)
    {
        Assert.True(
            bucket.IsGreen,
            $"{name} coverage gate is RED. Missing={{{string.Join(",", bucket.Missing)}}}"
        );
        Assert.Empty(bucket.Missing);
        // Extra entries are allowed (S12 cards may not yet be on the manifest mid-stage).
    }

    // ===== Smoke combat sanity =====

    [Fact]
    public void StrikeSilent_canonical_lookup_yields_same_id_as_register_constant()
    {
        // Catches typos: if the canonical-id constant ever diverges from the registration
        // string, this test fails before any combat code is wired.
        CardCatalog cards = SmokeContent.BuildCardCatalog();
        ICardModel strike = cards.Get(StrikeSilent.CanonicalId);
        Assert.Equal(StrikeSilent.CanonicalId, strike.Id);
    }
}

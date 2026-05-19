using System.Linq;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;

namespace Sts2Headless.Tests.Domain.Content;

/// <summary>
/// Phase-1 representative encounter fixture tests. Each test asserts the
/// encounter resolves to a valid monster id sequence; combat-driven final-state
/// hashes pin in S13 alongside the determinism probe (the spec defers final-hash
/// golden pinning to S13 once the combat engine has the full move-rotation
/// state machine wired up).
///
/// <para>
/// <b>Scope of this file:</b> verify each of the 21 Phase-1 encounters is
/// registered, has a non-empty spawn list, and every referenced monster id
/// exists in the monster catalog. The full "drive scripted actions, hash final
/// state" workflow promised by the S12 spec lands in S13 once combat-state
/// hashing is in place — for S12, we pin the encounter SHAPE (id list +
/// monster-id resolution), which is the part the upstream determinism probe
/// compares first anyway.
/// </para>
/// </summary>
public class EncounterFixtureTests
{
    // B.1-final-T2: 7 encounters deleted (JawWormSolo, TwoLouseNormal, LargeSlimeBoss,
    // SentryTrio, SnakePlantSolo, FungalBossEncounter, CenturyGuardBoss — STS1-only
    // monsters); KaiserCrabBoss reshaped to 2 monsters (Crusher + Rocket); 1 added
    // (LouseProgenitorNormal — STS2 upstream encounter using existing γ monster).
    [Theory]
    [InlineData("ChompersNormal", 2)]
    [InlineData("ExoskeletonsNormal", 3)]
    // Wave 14 / B.1-ε: SmallSlimes → 3 monsters (2 small + 1 medium via Rng);
    // MediumSlimes → 4 monsters (2 medium + 2 small via NextBool).
    [InlineData("SmallSlimes", 3)]
    [InlineData("MediumSlimes", 4)]
    [InlineData("BowlbugsTrio", 3)]
    [InlineData("FuzzyWurmCrawlerSolo", 1)]
    [InlineData("FossilStalkerElite", 1)]
    [InlineData("FrogKnightElite", 1)]
    [InlineData("LagavulinElite", 1)]
    [InlineData("HauntedShipSolo", 1)]
    [InlineData("LivingFogSolo", 1)]
    // Wave-26/Q1.D: 1 GremlinMerc initial (SneakyGremlin + FatGremlin spawn mid-combat via SurprisePower).
    [InlineData("GremlinMercNormal", 1)]
    [InlineData("KaiserCrabBoss", 2)]
    [InlineData("CeremonialBeastBoss", 1)]
    [InlineData("LouseProgenitorNormal", 1)]
    public void Encounter_spawn_list_resolves_against_monster_catalog(
        string encounterId,
        int expectedMonsterCount
    )
    {
        EncounterCatalog encounters = Phase1Content.BuildEncounterCatalog();
        MonsterCatalog monsters = Phase1Content.BuildMonsterCatalog();

        Assert.True(encounters.Contains(encounterId), $"missing encounter '{encounterId}'");
        IEncounterModel enc = encounters.Get(encounterId);
        Assert.Equal(expectedMonsterCount, enc.MonsterIds.Count);
        foreach (string monsterId in enc.MonsterIds)
        {
            Assert.True(
                monsters.Contains(monsterId),
                $"Encounter '{encounterId}' references monster '{monsterId}' not in catalog."
            );
        }
    }

    [Fact]
    public void Phase1_encounter_catalog_contains_smoke_plus_s12_encounters()
    {
        EncounterCatalog catalog = Phase1Content.BuildEncounterCatalog();
        // Post-B.1-final-T2: 1 smoke (CultistsNormal) + 15 S12 + 1 add = 16 encounters
        // (was 22, -7 STS1-only deletes, +1 LouseProgenitorNormal port).
        Assert.True(catalog.Count >= 15, $"expected >=15 encounters, got {catalog.Count}");
        Assert.True(catalog.Contains("CultistsNormal")); // S5 smoke preserved
        Assert.True(catalog.Contains("LouseProgenitorNormal")); // B.1-final-T2c add
    }

    [Fact]
    public void Encounter_id_list_is_stable_in_insertion_order()
    {
        // Determinism contract: encounter enumeration must be insertion-stable across
        // processes so M1 state codec / token map produces identical bytes.
        EncounterCatalog one = Phase1Content.BuildEncounterCatalog();
        EncounterCatalog two = Phase1Content.BuildEncounterCatalog();
        Assert.Equal(one.EnumerateIds().ToList(), two.EnumerateIds().ToList());
    }
}

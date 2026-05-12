using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;

namespace Sts2Headless.Tests.Domain.Content.Encounters;

/// <summary>
/// Tests for the smoke encounter wiring. Verifies the encounter id, the ordered
/// monster spawn list matches upstream, and the EncounterCatalog round-trip.
/// </summary>
public class CultistsNormalTests
{
    [Fact]
    public void CultistsNormal_canonical_properties()
    {
        CultistsNormal e = new();
        Assert.Equal("CultistsNormal", e.Id);
        Assert.Equal(2, e.MonsterIds.Count);
        Assert.Equal(CalcifiedCultist.CanonicalId, e.MonsterIds[0]);
        Assert.Equal(DampCultist.CanonicalId, e.MonsterIds[1]);
    }

    [Fact]
    public void EncounterCatalog_registers_and_looks_up_smoke_encounter()
    {
        EncounterCatalog catalog = new();
        CultistsNormal e = new();
        catalog.Register(e.Id, e);
        Assert.Equal(1, catalog.Count);
        Assert.Same(e, catalog.Get(CultistsNormal.CanonicalId));
    }

    [Fact]
    public void EncounterModel_construction_rejects_empty_id()
    {
        Assert.Throws<System.ArgumentException>(() => new BadEncounter(""));
    }

    [Fact]
    public void EncounterModel_construction_rejects_empty_monster_list()
    {
        Assert.Throws<System.ArgumentException>(() => new EmptyEncounter());
    }

    private sealed class BadEncounter : EncounterModel
    {
        public BadEncounter(string id) : base(id, new[] { "m" }) { }
    }

    private sealed class EmptyEncounter : EncounterModel
    {
        public EmptyEncounter() : base("e", System.Array.Empty<string>()) { }
    }
}

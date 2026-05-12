using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;

namespace Sts2Headless.Domain.Content;

/// <summary>
/// One-stop registration of every Phase-1 smoke content model into the five
/// Q4-tracked catalogs (cards / relics / powers / monsters / potions) plus the
/// S5-introduced <see cref="EncounterCatalog"/>. Lives in
/// <c>Sts2Headless.Domain.Content</c> next to the catalog types so combat-side glue
/// (S6) and the eventual M9 Process Host bootstrap share a single registration
/// path.
///
/// <para>
/// <b>Iteration-order contract:</b> registration order matches the per-catalog
/// declaration order in the Q4 manifest fixture
/// (<c>test/fixtures/q4-manifest-phase1.json</c>). That order is the source of
/// truth for the M1 State Codec's per-content enumeration; flipping ids here
/// breaks the coverage gate and could break replay determinism downstream.
/// </para>
///
/// <para>
/// <b>Potions:</b> the smoke set has no potions. The bucket is left empty (it's a
/// no-op coverage gate pass per the M7 contract).
/// </para>
/// </summary>
public static class SmokeContent
{
    /// <summary>
    /// Construct a fresh <see cref="CardCatalog"/> populated with all smoke cards.
    /// </summary>
    public static CardCatalog BuildCardCatalog()
    {
        CardCatalog cards = new();
        cards.Register(StrikeSilent.CanonicalId, new StrikeSilent());
        cards.Register(DefendSilent.CanonicalId, new DefendSilent());
        cards.Register(Neutralize.CanonicalId, new Neutralize());
        cards.Register(Survivor.CanonicalId, new Survivor());
        cards.Register(Slice.CanonicalId, new Slice());
        cards.Register(DeadlyPoison.CanonicalId, new DeadlyPoison());
        cards.Register(Backflip.CanonicalId, new Backflip());
        cards.Register(Acrobatics.CanonicalId, new Acrobatics());
        cards.Register(DodgeAndRoll.CanonicalId, new DodgeAndRoll());
        return cards;
    }

    /// <summary>
    /// Construct a fresh <see cref="RelicCatalog"/> populated with all smoke relics.
    /// </summary>
    public static RelicCatalog BuildRelicCatalog()
    {
        RelicCatalog relics = new();
        relics.Register(RingOfTheSnake.CanonicalId, new RingOfTheSnake());
        relics.Register(Anchor.CanonicalId, new Anchor());
        relics.Register(Vajra.CanonicalId, new Vajra());
        relics.Register(BagOfPreparation.CanonicalId, new BagOfPreparation());
        relics.Register(BloodVial.CanonicalId, new BloodVial());
        return relics;
    }

    /// <summary>
    /// Construct a fresh <see cref="PowerCatalog"/> populated with all smoke powers.
    /// </summary>
    public static PowerCatalog BuildPowerCatalog()
    {
        PowerCatalog powers = new();
        powers.Register(PowerIds.Poison, new PoisonPower());
        powers.Register(PowerIds.Vulnerable, new VulnerablePower());
        powers.Register(PowerIds.Weak, new WeakPower());
        powers.Register(PowerIds.Strength, new StrengthPower());
        powers.Register(PowerIds.Ritual, new RitualPower());
        return powers;
    }

    /// <summary>
    /// Construct a fresh <see cref="MonsterCatalog"/> populated with the cultists.
    /// </summary>
    public static MonsterCatalog BuildMonsterCatalog()
    {
        MonsterCatalog monsters = new();
        monsters.Register(CalcifiedCultist.CanonicalId, new CalcifiedCultist());
        monsters.Register(DampCultist.CanonicalId, new DampCultist());
        return monsters;
    }

    /// <summary>
    /// Construct a fresh empty <see cref="PotionCatalog"/>. The smoke set has no
    /// potions; this exists so callers can wire all five catalogs uniformly.
    /// </summary>
    public static PotionCatalog BuildPotionCatalog() => new();

    /// <summary>
    /// Construct a fresh <see cref="EncounterCatalog"/> populated with the smoke
    /// encounter (<see cref="CultistsNormal"/>).
    /// </summary>
    public static EncounterCatalog BuildEncounterCatalog()
    {
        EncounterCatalog encounters = new();
        encounters.Register(CultistsNormal.CanonicalId, new CultistsNormal());
        return encounters;
    }
}

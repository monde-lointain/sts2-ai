using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Powers;
using Sts2Headless.Domain.Content.Relics;
using Potions = Sts2Headless.Domain.Content.Potions;

namespace Sts2Headless.Domain.Content;

/// <summary>
/// One-stop registration of every Phase-1 content model (smoke set + S12 expansion)
/// into the five Q4-tracked catalogs plus the encounter catalog. Mirrors
/// <see cref="SmokeContent"/> but extends every bucket with the full S12 port.
///
/// <para>
/// <b>Iteration-order contract:</b> registration order matches the per-catalog
/// declaration order in the Q4 manifest fixture
/// (<c>test/fixtures/q4-manifest-phase1.json</c>). That order is the source of truth
/// for the M1 State Codec's per-content enumeration; flipping ids breaks the coverage
/// gate and could break replay determinism downstream.
/// </para>
///
/// <para>
/// <b>Why a separate file from <see cref="SmokeContent"/>:</b> S5 fenced
/// <c>SmokeContent.cs</c> from modification by S12. This file re-uses the smoke
/// builders by composition then appends the new ids — net effect: every smoke entry
/// stays at its original index, S12 entries follow.
/// </para>
/// </summary>
public static class Phase1Content
{
    /// <summary>Build a CardCatalog with smoke + S12 Silent + colorless/status/curse cards.</summary>
    public static CardCatalog BuildCardCatalog()
    {
        CardCatalog cards = SmokeContent.BuildCardCatalog();

        // === Silent expansion (alphabetical except smoke) ===
        cards.Register(Abrasive.CanonicalId, new Abrasive());
        cards.Register(Accelerant.CanonicalId, new Accelerant());
        cards.Register(Accuracy.CanonicalId, new Accuracy());
        cards.Register(Adrenaline.CanonicalId, new Adrenaline());
        cards.Register(Afterimage.CanonicalId, new Afterimage());
        cards.Register(Anticipate.CanonicalId, new Anticipate());
        cards.Register(Assassinate.CanonicalId, new Assassinate());
        cards.Register(Backstab.CanonicalId, new Backstab());
        cards.Register(BladeOfInk.CanonicalId, new BladeOfInk());
        cards.Register(BladeDance.CanonicalId, new BladeDance());
        cards.Register(Blur.CanonicalId, new Blur());
        cards.Register(BouncingFlask.CanonicalId, new BouncingFlask());
        cards.Register(BubbleBubble.CanonicalId, new BubbleBubble());
        cards.Register(BulletTime.CanonicalId, new BulletTime());
        cards.Register(Burst.CanonicalId, new Burst());
        cards.Register(CalculatedGamble.CanonicalId, new CalculatedGamble());
        cards.Register(CloakAndDagger.CanonicalId, new CloakAndDagger());
        cards.Register(CorrosiveWave.CanonicalId, new CorrosiveWave());
        cards.Register(DaggerSpray.CanonicalId, new DaggerSpray());
        cards.Register(DaggerThrow.CanonicalId, new DaggerThrow());
        cards.Register(Dash.CanonicalId, new Dash());
        cards.Register(Deflect.CanonicalId, new Deflect());
        cards.Register(EchoingSlash.CanonicalId, new EchoingSlash());
        cards.Register(Envenom.CanonicalId, new Envenom());
        cards.Register(EscapePlan.CanonicalId, new EscapePlan());
        cards.Register(Expertise.CanonicalId, new Expertise());
        cards.Register(Expose.CanonicalId, new Expose());
        cards.Register(FanOfKnives.CanonicalId, new FanOfKnives());
        cards.Register(Finisher.CanonicalId, new Finisher());
        cards.Register(Flanking.CanonicalId, new Flanking());
        cards.Register(Flechettes.CanonicalId, new Flechettes());
        cards.Register(FlickFlack.CanonicalId, new FlickFlack());
        cards.Register(FollowThrough.CanonicalId, new FollowThrough());
        cards.Register(Footwork.CanonicalId, new Footwork());
        cards.Register(GrandFinale.CanonicalId, new GrandFinale());
        cards.Register(HandTrick.CanonicalId, new HandTrick());
        cards.Register(Haze.CanonicalId, new Haze());
        cards.Register(HiddenDaggers.CanonicalId, new HiddenDaggers());
        cards.Register(InfiniteBlades.CanonicalId, new InfiniteBlades());
        cards.Register(KnifeTrap.CanonicalId, new KnifeTrap());
        cards.Register(LeadingStrike.CanonicalId, new LeadingStrike());
        cards.Register(LegSweep.CanonicalId, new LegSweep());
        cards.Register(Malaise.CanonicalId, new Malaise());
        cards.Register(MasterPlanner.CanonicalId, new MasterPlanner());
        cards.Register(MementoMori.CanonicalId, new MementoMori());
        cards.Register(Mirage.CanonicalId, new Mirage());
        cards.Register(Murder.CanonicalId, new Murder());
        cards.Register(Nightmare.CanonicalId, new Nightmare());
        cards.Register(NoxiousFumes.CanonicalId, new NoxiousFumes());
        cards.Register(Outbreak.CanonicalId, new Outbreak());
        cards.Register(PhantomBlades.CanonicalId, new PhantomBlades());
        cards.Register(PiercingWail.CanonicalId, new PiercingWail());
        cards.Register(Pinpoint.CanonicalId, new Pinpoint());
        cards.Register(PoisonedStab.CanonicalId, new PoisonedStab());
        cards.Register(Pounce.CanonicalId, new Pounce());
        cards.Register(PreciseCut.CanonicalId, new PreciseCut());
        cards.Register(Predator.CanonicalId, new Predator());
        cards.Register(Prepared.CanonicalId, new Prepared());
        cards.Register(Reflex.CanonicalId, new Reflex());
        cards.Register(Ricochet.CanonicalId, new Ricochet());
        cards.Register(SerpentForm.CanonicalId, new SerpentForm());
        cards.Register(ShadowStep.CanonicalId, new ShadowStep());
        cards.Register(Shadowmeld.CanonicalId, new Shadowmeld());
        cards.Register(Shiv.CanonicalId, new Shiv());
        cards.Register(Skewer.CanonicalId, new Skewer());
        cards.Register(Snakebite.CanonicalId, new Snakebite());
        cards.Register(Sneaky.CanonicalId, new Sneaky());
        cards.Register(Speedster.CanonicalId, new Speedster());
        cards.Register(StormOfSteel.CanonicalId, new StormOfSteel());
        cards.Register(Strangle.CanonicalId, new Strangle());
        cards.Register(SuckerPunch.CanonicalId, new SuckerPunch());
        cards.Register(Suppress.CanonicalId, new Suppress());
        cards.Register(Tactician.CanonicalId, new Tactician());
        cards.Register(TheHunt.CanonicalId, new TheHunt());
        cards.Register(ToolsOfTheTrade.CanonicalId, new ToolsOfTheTrade());
        cards.Register(Tracking.CanonicalId, new Tracking());
        cards.Register(Untouchable.CanonicalId, new Untouchable());
        cards.Register(UpMySleeve.CanonicalId, new UpMySleeve());
        cards.Register(WellLaidPlans.CanonicalId, new WellLaidPlans());
        cards.Register(WraithForm.CanonicalId, new WraithForm());

        // === Status cards ===
        cards.Register(Wound.CanonicalId, new Wound());
        cards.Register(Slimed.CanonicalId, new Slimed());
        cards.Register(Burn.CanonicalId, new Burn());
        cards.Register(Dazed.CanonicalId, new Dazed());

        // === Curse cards ===
        cards.Register(AscendersBane.CanonicalId, new AscendersBane());
        cards.Register(Clumsy.CanonicalId, new Clumsy());
        cards.Register(Doubt.CanonicalId, new Doubt());
        cards.Register(Regret.CanonicalId, new Regret());
        cards.Register(Injury.CanonicalId, new Injury());

        return cards;
    }

    /// <summary>Build a RelicCatalog with smoke + S12 Silent-relevant relics.</summary>
    public static RelicCatalog BuildRelicCatalog()
    {
        RelicCatalog relics = SmokeContent.BuildRelicCatalog();
        relics.Register(Akabeko.CanonicalId, new Akabeko());
        relics.Register(BagOfMarbles.CanonicalId, new BagOfMarbles());
        relics.Register(BronzeScales.CanonicalId, new BronzeScales());
        relics.Register(CentennialPuzzle.CanonicalId, new CentennialPuzzle());
        relics.Register(HappyFlower.CanonicalId, new HappyFlower());
        relics.Register(OddlySmoothStone.CanonicalId, new OddlySmoothStone());
        relics.Register(RedSkull.CanonicalId, new RedSkull());
        relics.Register(Whetstone.CanonicalId, new Whetstone());
        relics.Register(DataDisk.CanonicalId, new DataDisk());
        relics.Register(MealTicket.CanonicalId, new MealTicket());
        relics.Register(PenNib.CanonicalId, new PenNib());
        relics.Register(Pantograph.CanonicalId, new Pantograph());
        relics.Register(MercuryHourglass.CanonicalId, new MercuryHourglass());
        relics.Register(PaperPhrog.CanonicalId, new PaperPhrog());
        relics.Register(GamblingChip.CanonicalId, new GamblingChip());
        relics.Register(Mango.CanonicalId, new Mango());
        relics.Register(Pocketwatch.CanonicalId, new Pocketwatch());
        relics.Register(Shovel.CanonicalId, new Shovel());
        relics.Register(ArtOfWar.CanonicalId, new ArtOfWar());
        relics.Register(Bellows.CanonicalId, new Bellows());
        relics.Register(Bookmark.CanonicalId, new Bookmark());
        relics.Register(MeatOnTheBone.CanonicalId, new MeatOnTheBone());
        relics.Register(BeautifulBracelet.CanonicalId, new BeautifulBracelet());
        relics.Register(BiiigHug.CanonicalId, new BiiigHug());
        relics.Register(CallingBell.CanonicalId, new CallingBell());
        relics.Register(Ectoplasm.CanonicalId, new Ectoplasm());
        relics.Register(Cauldron.CanonicalId, new Cauldron());
        relics.Register(DollysMirror.CanonicalId, new DollysMirror());
        relics.Register(LeesWaffle.CanonicalId, new LeesWaffle());
        relics.Register(TheBoot.CanonicalId, new TheBoot());
        relics.Register(SneckosEye.CanonicalId, new SneckosEye());
        relics.Register(SnakeSkull.CanonicalId, new SnakeSkull());
        relics.Register(NinjaScroll.CanonicalId, new NinjaScroll());
        relics.Register(PaintedFan.CanonicalId, new PaintedFan());
        relics.Register(TwistedFunnel.CanonicalId, new TwistedFunnel());
        relics.Register(WristBlade.CanonicalId, new WristBlade());
        relics.Register(Tingsha.CanonicalId, new Tingsha());
        relics.Register(TheTotem.CanonicalId, new TheTotem());
        relics.Register(TornCard.CanonicalId, new TornCard());
        relics.Register(HoveringKite.CanonicalId, new HoveringKite());
        relics.Register(CeramicFish.CanonicalId, new CeramicFish());
        relics.Register(Strawberry.CanonicalId, new Strawberry());
        relics.Register(Pear.CanonicalId, new Pear());
        relics.Register(Anchovy.CanonicalId, new Anchovy());
        relics.Register(Lantern.CanonicalId, new Lantern());
        relics.Register(Cookbook.CanonicalId, new Cookbook());
        relics.Register(GremlinHorn.CanonicalId, new GremlinHorn());
        relics.Register(HornCleat.CanonicalId, new HornCleat());
        relics.Register(LetterOpener.CanonicalId, new LetterOpener());
        relics.Register(OrnamentalFan.CanonicalId, new OrnamentalFan());
        relics.Register(Orichalcum.CanonicalId, new Orichalcum());
        relics.Register(Kunai.CanonicalId, new Kunai());
        relics.Register(Shuriken.CanonicalId, new Shuriken());
        relics.Register(ToyOrnithopter.CanonicalId, new ToyOrnithopter());
        return relics;
    }

    /// <summary>Build a PowerCatalog with smoke + S12 powers.</summary>
    public static PowerCatalog BuildPowerCatalog()
    {
        PowerCatalog powers = SmokeContent.BuildPowerCatalog();
        powers.Register(PowerIds.Thorns, new ThornsPower());
        powers.Register(PowerIds.Dexterity, new DexterityPower());
        powers.Register(PowerIds.Intangible, new IntangiblePower());
        powers.Register(PowerIds.Artifact, new ArtifactPower());
        powers.Register(PowerIds.Frail, new FrailPower());
        powers.Register(PowerIds.Accelerant, new AccelerantPower());
        powers.Register(PowerIds.Accuracy, new AccuracyPower());
        powers.Register(PowerIds.Afterimage, new AfterimagePower());
        powers.Register(PowerIds.Envenom, new EnvenomPower());
        powers.Register(PowerIds.SerpentForm, new SerpentFormPower());
        powers.Register(PowerIds.Outbreak, new OutbreakPower());
        powers.Register(PowerIds.PhantomBlades, new PhantomBladesPower());
        powers.Register(PowerIds.Strangle, new StranglePower());
        powers.Register(PowerIds.Sneaky, new SneakyPower());
        powers.Register(PowerIds.Speedster, new SpeedsterPower());
        powers.Register(PowerIds.WraithForm, new WraithFormPower());
        powers.Register(PowerIds.Burst, new BurstPower());
        powers.Register(PowerIds.MasterPlanner, new MasterPlannerPower());
        powers.Register(PowerIds.InfiniteBlades, new InfiniteBladesPower());
        powers.Register(PowerIds.FanOfKnives, new FanOfKnivesPower());
        powers.Register(PowerIds.Tracking, new TrackingPower());
        powers.Register(PowerIds.ToolsOfTheTrade, new ToolsOfTheTradePower());
        powers.Register(PowerIds.CurlUp, new CurlUpPower());
        powers.Register(PowerIds.AngerPower, new AngerPower());
        powers.Register(PowerIds.Enrage, new EnragePower());
        powers.Register(PowerIds.Metallicize, new MetallicizePower());
        powers.Register(PowerIds.Plated, new PlatedArmorPower());
        powers.Register(PowerIds.Regen, new RegenPower());
        powers.Register(PowerIds.ModeShift, new ModeShiftPower());
        powers.Register(PowerIds.PenNib, new PenNibPower());
        powers.Register(PowerIds.Energized, new EnergizedPower());
        powers.Register(PowerIds.Block, new BlockPower());
        powers.Register(PowerIds.DexterityLoss, new DexterityLossPower());
        powers.Register(PowerIds.StrengthDown, new StrengthDownPower());
        powers.Register(PowerIds.Entangled, new EntangledPower());
        powers.Register(PowerIds.NoBlock, new NoBlockPower());
        powers.Register(PowerIds.NoDraw, new NoDrawPower());
        powers.Register(PowerIds.Confused, new ConfusedPower());
        powers.Register("BarricadePower", new BarricadePower());
        powers.Register("BlurPower", new BlurPower());
        // B.1-gamma-T4: relic-applied buff powers (Akabeko Vigor, DataDisk Focus).
        powers.Register(PowerIds.Vigor, new VigorPower());
        powers.Register(PowerIds.Focus, new FocusPower());
        return powers;
    }

    /// <summary>Build a MonsterCatalog with smoke + S12 monsters.</summary>
    public static MonsterCatalog BuildMonsterCatalog()
    {
        MonsterCatalog monsters = SmokeContent.BuildMonsterCatalog();
        monsters.Register(Chomper.CanonicalId, new Chomper());
        monsters.Register(Exoskeleton.CanonicalId, new Exoskeleton());
        monsters.Register(FuzzyWurmCrawler.CanonicalId, new FuzzyWurmCrawler());
        monsters.Register(LouseProgenitor.CanonicalId, new LouseProgenitor());
        monsters.Register(GremlinMerc.CanonicalId, new GremlinMerc());
        monsters.Register(HauntedShip.CanonicalId, new HauntedShip());
        monsters.Register(LivingFog.CanonicalId, new LivingFog());
        monsters.Register(CeremonialBeast.CanonicalId, new CeremonialBeast());
        monsters.Register(JawWorm.CanonicalId, new JawWorm());
        monsters.Register(RedLouse.CanonicalId, new RedLouse());
        monsters.Register(GreenLouse.CanonicalId, new GreenLouse());
        monsters.Register(AcidSlimeS.CanonicalId, new AcidSlimeS());
        monsters.Register(AcidSlimeM.CanonicalId, new AcidSlimeM());
        monsters.Register(AcidSlimeL.CanonicalId, new AcidSlimeL());
        monsters.Register(SpikeSlimeS.CanonicalId, new SpikeSlimeS());
        monsters.Register(SpikeSlimeM.CanonicalId, new SpikeSlimeM());
        monsters.Register(SpikeSlimeL.CanonicalId, new SpikeSlimeL());
        monsters.Register(FungalBoss.CanonicalId, new FungalBoss());
        monsters.Register(SnakePlant.CanonicalId, new SnakePlant());
        monsters.Register(Sentry.CanonicalId, new Sentry());
        monsters.Register(LagavulinMatriarch.CanonicalId, new LagavulinMatriarch());
        monsters.Register(CenturyGuard.CanonicalId, new CenturyGuard());
        monsters.Register(SilverMage.CanonicalId, new SilverMage());
        // B.1-final-T2b: KaiserCrab class deleted (Q1-invented). Replaced by
        // upstream-aligned Crusher + Rocket spawned together in KaiserCrabBoss.
        monsters.Register(Crusher.CanonicalId, new Crusher());
        monsters.Register(Rocket.CanonicalId, new Rocket());
        monsters.Register(BowlbugEgg.CanonicalId, new BowlbugEgg());
        monsters.Register(BowlbugNectar.CanonicalId, new BowlbugNectar());
        monsters.Register(BowlbugRock.CanonicalId, new BowlbugRock());
        monsters.Register(BowlbugSilk.CanonicalId, new BowlbugSilk());
        monsters.Register(FossilStalker.CanonicalId, new FossilStalker());
        monsters.Register(FrogKnight.CanonicalId, new FrogKnight());
        return monsters;
    }

    /// <summary>Build a PotionCatalog with S12 combat potions.</summary>
    public static PotionCatalog BuildPotionCatalog()
    {
        PotionCatalog potions = SmokeContent.BuildPotionCatalog();
        potions.Register(Potions.BlockPotion.CanonicalId, new Potions.BlockPotion());
        potions.Register(Potions.FirePotion.CanonicalId, new Potions.FirePotion());
        potions.Register(Potions.EnergyPotion.CanonicalId, new Potions.EnergyPotion());
        potions.Register(Potions.ExplosiveAmpoule.CanonicalId, new Potions.ExplosiveAmpoule());
        potions.Register(Potions.FlexPotion.CanonicalId, new Potions.FlexPotion());
        potions.Register(Potions.DexterityPotion.CanonicalId, new Potions.DexterityPotion());
        potions.Register(Potions.StrengthPotion.CanonicalId, new Potions.StrengthPotion());
        potions.Register(Potions.SkillPotion.CanonicalId, new Potions.SkillPotion());
        potions.Register(Potions.AttackPotion.CanonicalId, new Potions.AttackPotion());
        potions.Register(Potions.PowerPotion.CanonicalId, new Potions.PowerPotion());
        potions.Register(Potions.SwiftPotion.CanonicalId, new Potions.SwiftPotion());
        potions.Register(Potions.BloodPotion.CanonicalId, new Potions.BloodPotion());
        potions.Register(Potions.PoisonPotion.CanonicalId, new Potions.PoisonPotion());
        potions.Register(Potions.FocusPotion.CanonicalId, new Potions.FocusPotion());
        potions.Register(Potions.CunningPotion.CanonicalId, new Potions.CunningPotion());
        potions.Register(Potions.LiquidBronze.CanonicalId, new Potions.LiquidBronze());
        potions.Register(Potions.GamblersBrew.CanonicalId, new Potions.GamblersBrew());
        potions.Register(Potions.HeartOfIron.CanonicalId, new Potions.HeartOfIron());
        potions.Register(Potions.FairyInABottle.CanonicalId, new Potions.FairyInABottle());
        potions.Register(Potions.LiquidMemories.CanonicalId, new Potions.LiquidMemories());
        potions.Register(Potions.EntropicBrew.CanonicalId, new Potions.EntropicBrew());
        return potions;
    }

    /// <summary>Build an EncounterCatalog with smoke + S12 encounters.</summary>
    public static EncounterCatalog BuildEncounterCatalog()
    {
        EncounterCatalog encounters = SmokeContent.BuildEncounterCatalog();
        Phase1EncountersRegistration.RegisterAll(encounters);
        return encounters;
    }

    // Per-category registration extension points — populated below; each subsequent
    // S12-T<k> commit adds its bucket's Register lines to the corresponding method body.
}

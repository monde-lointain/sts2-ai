using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Byte-faithful HP envelope checks for S12 Phase-1 monsters. Each monster's
/// (MinHp, MaxHp) at Ascension 0 matches upstream
/// <c>~/development/projects/godot/sts2/src/Core/Models/Monsters/*.cs</c>'s
/// <c>AscensionHelper.GetValueIfAscension(...)</c> lower bounds.
/// </summary>
public class Phase1MonsterTests
{
    /// <summary>
    /// Theory rows: (type, expectedId, minHp, maxHp, expectedFirstIntent).
    /// expectedFirstIntent describes the OPENING intent for byte-faithful
    /// upstream rotations — Attack-value when the first move is an attack,
    /// "Buff" / "Sleep" / "Defend" when it isn't. B.1-gamma-T3 ported some
    /// of these monsters' rotations to non-attack openers (Lagavulin opens
    /// with SLEEP; Ceremonial Beast opens with STAMP buff; etc.).
    /// </summary>
    [Theory]
    // Chomper's first intent is CLAMP (MultiAttack 8x2). Per-hit value=8.
    [InlineData(typeof(Chomper), "Chomper", 60, 64, "attack:8")]
    // Exoskeleton's first intent is SKITTER (MultiAttack 1x3). Per-hit value=1.
    [InlineData(typeof(Exoskeleton), "Exoskeleton", 24, 28, "attack:1")]
    [InlineData(typeof(FuzzyWurmCrawler), "FuzzyWurmCrawler", 55, 57, "attack:14")]
    // Louse opens with WEB_CANNON (Attack 9). Upstream initial machine state.
    [InlineData(typeof(LouseProgenitor), "LouseProgenitor", 134, 136, "attack:9")]
    [InlineData(typeof(GremlinMerc), "GremlinMerc", 47, 49, "attack:9")]
    [InlineData(typeof(HauntedShip), "HauntedShip", 63, 63, "attack:12")]
    [InlineData(typeof(LivingFog), "LivingFog", 80, 80, "attack:14")]
    // Ceremonial Beast opens with STAMP (Buff intent — apply PlowPower).
    [InlineData(typeof(CeremonialBeast), "CeremonialBeast", 252, 252, "buff")]
    [InlineData(typeof(JawWorm), "JawWorm", 40, 44, "attack:11")]
    [InlineData(typeof(RedLouse), "RedLouse", 10, 15, "attack:6")]
    [InlineData(typeof(GreenLouse), "GreenLouse", 11, 17, "attack:5")]
    [InlineData(typeof(AcidSlimeS), "AcidSlimeS", 8, 12, "attack:3")]
    [InlineData(typeof(AcidSlimeM), "AcidSlimeM", 28, 32, "attack:7")]
    [InlineData(typeof(AcidSlimeL), "AcidSlimeL", 65, 69, "attack:11")]
    // Wave 14 / B.1-ε: SpikeSlimeS/M removed; TwigSlimeS/M ported from upstream.
    // TwigSlimeS opens with TACKLE_MOVE (Attack 4, single-state self-loop).
    // TwigSlimeM opens with STICKY_SHOT_MOVE (Status 1 Slimed, upstream initial state).
    // LeafSlimeS opens with TACKLE_MOVE (resolver picks TACKLE or GOOP on turn 1).
    // LeafSlimeM opens with STICKY_SHOT (strict alternation, upstream initial state).
    [InlineData(typeof(LeafSlimeS), "LeafSlimeS", 11, 15, "attack:3")]
    [InlineData(typeof(LeafSlimeM), "LeafSlimeM", 32, 35, "status:2")]
    [InlineData(typeof(TwigSlimeS), "TwigSlimeS", 7, 11, "attack:4")]
    [InlineData(typeof(TwigSlimeM), "TwigSlimeM", 26, 28, "status:1")]
    [InlineData(typeof(SpikeSlimeL), "SpikeSlimeL", 64, 70, "attack:16")]
    [InlineData(typeof(FungalBoss), "FungalBoss", 200, 200, "attack:18")]
    [InlineData(typeof(SnakePlant), "SnakePlant", 75, 79, "attack:6")]
    [InlineData(typeof(Sentry), "Sentry", 38, 42, "attack:9")]
    // LagavulinMatriarch opens with SLEEP (Buff intent — placeholder, no engine payload).
    // B.1-final-T1: renamed Lagavulin → LagavulinMatriarch + HP 222 (single-value
    // envelope per upstream AscensionHelper.GetValueIfAscension(ToughEnemies, 233, 222)).
    [InlineData(typeof(LagavulinMatriarch), "LagavulinMatriarch", 222, 222, "buff")]
    [InlineData(typeof(CenturyGuard), "CenturyGuard", 240, 240, "attack:12")]
    [InlineData(typeof(SilverMage), "SilverMage", 86, 86, "attack:13")]
    // B.1-final-T2b: KaiserCrab class deleted; Crusher + Rocket ported from upstream
    // KaiserCrabBoss spawn (left-arm + right-arm). HP single-value envelope at A0.
    [InlineData(typeof(Crusher), "Crusher", 209, 209, "attack:12")]
    [InlineData(typeof(Rocket), "Rocket", 199, 199, "attack:3")]
    // RC-5 (B.1-beta-T1): HP envelopes corrected to upstream A0 values.
    [InlineData(typeof(BowlbugEgg), "BowlbugEgg", 21, 22, "buff")]
    [InlineData(typeof(BowlbugNectar), "BowlbugNectar", 35, 38, "attack:5")]
    [InlineData(typeof(BowlbugRock), "BowlbugRock", 45, 48, "attack:7")]
    [InlineData(typeof(BowlbugSilk), "BowlbugSilk", 40, 43, "attack:4")]
    // FossilStalker opens with LATCH (Attack 12 — upstream's initial state).
    [InlineData(typeof(FossilStalker), "FossilStalker", 51, 53, "attack:12")]
    [InlineData(typeof(FrogKnight), "FrogKnight", 191, 191, "attack:15")]
    // Wave-24/K.q1: Nibbit opens with BUTT_MOVE (Attack 12).
    [InlineData(typeof(Nibbit), "Nibbit", 42, 46, "attack:12")]
    public void Monster_canonical_hp_and_intent(
        System.Type t,
        string expectedId,
        int minHp,
        int maxHp,
        string expectedFirstIntent
    )
    {
        MonsterModel m = (MonsterModel)System.Activator.CreateInstance(t)!;
        Assert.Equal(expectedId, m.Id);
        Assert.Equal(minHp, m.MinInitialHp);
        Assert.Equal(maxHp, m.MaxInitialHp);

        if (expectedFirstIntent.StartsWith("attack:"))
        {
            int expectedAttack = int.Parse(expectedFirstIntent.Substring("attack:".Length));
            Assert.Equal(IntentKind.Attack, m.InitialIntent.Kind);
            Assert.Equal(expectedAttack, m.InitialIntent.Value);
        }
        else if (expectedFirstIntent == "buff")
        {
            Assert.Equal(IntentKind.Buff, m.InitialIntent.Kind);
        }
        else if (expectedFirstIntent.StartsWith("status:"))
        {
            // Status intent: monster's opening move adds N status cards to discard.
            int expectedCards = int.Parse(expectedFirstIntent.Substring("status:".Length));
            Assert.Equal(IntentKind.Status, m.InitialIntent.Kind);
            Assert.Equal(expectedCards, m.InitialIntent.Value);
        }
        else
        {
            Assert.Fail($"Unknown intent spec: {expectedFirstIntent}");
        }
    }

    [Fact]
    public void Phase1Content_monster_catalog_contains_all_smoke_plus_s12_monsters()
    {
        MonsterCatalog catalog = Phase1Content.BuildMonsterCatalog();
        Assert.True(catalog.Count >= 30, $"expected >=30 monsters, got {catalog.Count}");
        Assert.True(catalog.Contains("CalcifiedCultist"));
        Assert.True(catalog.Contains("Chomper"));
        Assert.True(catalog.Contains("Crusher"));
        Assert.True(catalog.Contains("Rocket"));
    }
}

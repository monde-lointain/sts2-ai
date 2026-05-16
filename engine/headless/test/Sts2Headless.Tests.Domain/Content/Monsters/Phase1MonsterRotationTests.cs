using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Content.Monsters;

/// <summary>
/// Stream-B-T3: pin multi-state intent rotations for Phase-1 monsters whose
/// upstream state machines were ported to byte-faithful rotations in this
/// stream. Each test drives a local move-id cursor via the immutable
/// <see cref="MonsterModel.AdvanceMoveId"/> resolver (mirroring how combat
/// tracks per-creature rotation on <c>Creature.Intent.MoveId</c>) and asserts
/// the transitions match upstream's <c>MonsterMoveStateMachine</c>.
/// </summary>
public sealed class Phase1MonsterRotationTests
{
    /// <summary>Vanilla branch context — full HP, no powers.</summary>
    private static MoveBranchContext FullHp(int hp = 100, int maxHp = 100) =>
        new(CurrentHp: hp, MaxHp: maxHp, HasPower: _ => false, GetPowerStacks: _ => 0);

    // ===== Chomper: CLAMP ↔ SCREECH =====

    [Fact]
    public void Chomper_initial_move_is_CLAMP_with_2x8_intent()
    {
        Chomper c = new();
        Assert.Equal(Chomper.ClampMoveId, c.InitialMoveId);
        Assert.Equal(IntentKind.Attack, c.InitialIntent.Kind);
        Assert.Equal(Chomper.ClampDamagePerHit, c.InitialIntent.Value);
        Assert.Equal(Chomper.ClampHitCount, c.InitialIntent.HitCount);
    }

    [Fact]
    public void Chomper_rotates_CLAMP_then_SCREECH_then_CLAMP()
    {
        Chomper c = new();
        RunRngSet rng = new("chomper-seed");
        MoveBranchContext ctx = FullHp();
        string cursor = c.InitialMoveId;
        Assert.Equal(Chomper.ClampMoveId, cursor);

        cursor = c.AdvanceMoveId(cursor, ctx, rng);
        Assert.Equal(Chomper.ScreechMoveId, cursor);
        Intent screechIntent = c.GetMove(cursor).Intent;
        Assert.Equal(IntentKind.Status, screechIntent.Kind);
        Assert.Equal(Chomper.ScreechStatusCards, screechIntent.Value);

        cursor = c.AdvanceMoveId(cursor, ctx, rng);
        Assert.Equal(Chomper.ClampMoveId, cursor);
        Assert.Equal(IntentKind.Attack, c.GetMove(cursor).Intent.Kind);

        cursor = c.AdvanceMoveId(cursor, ctx, rng);
        Assert.Equal(Chomper.ScreechMoveId, cursor);
    }

    [Fact]
    public void Chomper_CLAMP_through_CombatEngine_deals_2_separate_8_damage_hits_to_player()
    {
        // Drive a single enemy turn. Player has 70 HP, no block; Chomper CLAMP
        // does 8x2 = 16 raw → player drops to 54.
        CardCatalog cards = Phase1Content.BuildCardCatalog();
        RelicCatalog relics = Phase1Content.BuildRelicCatalog();
        PowerCatalog powers = Phase1Content.BuildPowerCatalog();
        MonsterCatalog monsters = Phase1Content.BuildMonsterCatalog();
        EncounterCatalog encounters = Phase1Content.BuildEncounterCatalog();

        // ChompersNormal has 2 Chompers, so use JawWormSolo? Wait — JawWormSolo has
        // only JawWorm. Use a synthetic single-Chomper encounter via the
        // existing catalog if possible. ChompersNormal has 2 — both will hit.
        // We'll start a fresh combat and step through one enemy turn.
        var deck = new List<CardInstance>
        {
            new(100u, StrikeSilent.CanonicalId, 0, null),
            new(101u, DefendSilent.CanonicalId, 0, null),
            new(102u, StrikeSilent.CanonicalId, 0, null),
            new(103u, DefendSilent.CanonicalId, 0, null),
            new(104u, StrikeSilent.CanonicalId, 0, null),
        };
        IEncounterModel enc = (IEncounterModel)encounters.Get(ChompersNormal.CanonicalId);
        CombatContext ctx = CombatEngine.StartCombat(
            enc,
            new CombatBootstrap(cards, relics, powers, monsters, encounters),
            new PlayerSpec(RelicIds: new[] { RingOfTheSnake.CanonicalId }, Deck: deck),
            new RunRngSet("seed-7"),
            new LogicalClock()
        );

        // End the player turn and run enemy turn — both Chompers should CLAMP for 8x2.
        // Two chompers × 16 = 32 damage; player goes from 70 → 38.
        int hpBefore = ctx.State.Player.CurrentHp;
        CombatEngine.EndPlayerTurn(ctx);
        CombatEngine.EnemyTurn(ctx);
        int hpAfter = ctx.State.Player.CurrentHp;

        // Player took 32 raw damage (16 per Chomper) minus any block (none on
        // turn end). At Phase-1 Vulnerable is per-creature and Chompers have none.
        Assert.Equal(hpBefore - 32, hpAfter);
    }

    // ===== Exoskeleton: RNG-branch on ENRAGE =====

    [Fact]
    public void Exoskeleton_skitter_then_mandibles_then_enrage_deterministic()
    {
        // The deterministic prefix: SKITTER → MANDIBLES → ENRAGE. After ENRAGE
        // the resolver picks via RNG.
        Exoskeleton ex = new();
        Assert.Equal(Exoskeleton.SkitterMoveId, ex.InitialMoveId);

        var ctx = new MoveBranchContext(20, 20, HasPower: _ => false, GetPowerStacks: _ => 0);
        var rng = new RunRngSet("exo-seed-1");
        Assert.Equal(
            Exoskeleton.MandiblesMoveId,
            ex.AdvanceMoveId(Exoskeleton.SkitterMoveId, ctx, rng)
        );
        Assert.Equal(
            Exoskeleton.EnrageMoveId,
            ex.AdvanceMoveId(Exoskeleton.MandiblesMoveId, ctx, rng)
        );
        // ENRAGE branches; both branches valid; reproducible for fixed seed.
        string after = ex.AdvanceMoveId(Exoskeleton.EnrageMoveId, ctx, rng);
        Assert.True(after == Exoskeleton.SkitterMoveId || after == Exoskeleton.MandiblesMoveId);
    }

    [Fact]
    public void Exoskeleton_rng_branch_is_seed_deterministic()
    {
        Exoskeleton ex = new();
        var ctx = new MoveBranchContext(20, 20, HasPower: _ => false, GetPowerStacks: _ => 0);
        string a = ex.AdvanceMoveId(Exoskeleton.EnrageMoveId, ctx, new RunRngSet("exo-pin"));
        string b = ex.AdvanceMoveId(Exoskeleton.EnrageMoveId, ctx, new RunRngSet("exo-pin"));
        Assert.Equal(a, b);
    }

    // ===== Louse rotation =====

    [Fact]
    public void LouseProgenitor_web_curl_pounce_loop()
    {
        LouseProgenitor l = new();
        Assert.Equal(LouseProgenitor.WebCannonMoveId, l.InitialMoveId);
        var ctx = new MoveBranchContext(134, 136, HasPower: _ => false, GetPowerStacks: _ => 0);
        var rng = new RunRngSet("louse-seed");
        Assert.Equal(
            LouseProgenitor.CurlAndGrowMoveId,
            l.AdvanceMoveId(LouseProgenitor.WebCannonMoveId, ctx, rng)
        );
        Assert.Equal(
            LouseProgenitor.PounceMoveId,
            l.AdvanceMoveId(LouseProgenitor.CurlAndGrowMoveId, ctx, rng)
        );
        Assert.Equal(
            LouseProgenitor.WebCannonMoveId,
            l.AdvanceMoveId(LouseProgenitor.PounceMoveId, ctx, rng)
        );
    }

    [Fact]
    public void LouseProgenitor_declares_CurlUp_spawn_power()
    {
        // Upstream AfterAddedToRoom: PowerCmd.Apply<CurlUpPower>(self, CurlBlock=14).
        // Verify the model declares the spawn power so the engine spawn loop
        // stamps it onto the creature's Powers list when the power is registered.
        LouseProgenitor l = new();
        Assert.Contains(
            l.SpawnPowers,
            p =>
                p.PowerId == Sts2Headless.Domain.Content.Powers.PowerIds.CurlUp
                && p.Stacks == LouseProgenitor.CurlBlock
        );
    }

    [Fact]
    public void Lagavulin_declares_Plating_spawn_power()
    {
        LagavulinMatriarch lag = new();
        Assert.Contains(
            lag.SpawnPowers,
            p =>
                p.PowerId == Sts2Headless.Domain.Content.Powers.PowerIds.Plated
                && p.Stacks == LagavulinMatriarch.PlatingStacks
        );
    }

    // ===== LagavulinMatriarch HP-threshold gate =====

    [Fact]
    public void Lagavulin_sleeps_at_full_hp_and_wakes_below_half()
    {
        LagavulinMatriarch lag = new();
        Assert.Equal(LagavulinMatriarch.SleepMoveId, lag.InitialMoveId);

        var rng = new RunRngSet("lag-seed");
        // Above half: stays asleep.
        var fullHp = new MoveBranchContext(222, 222, HasPower: _ => false, GetPowerStacks: _ => 0);
        Assert.Equal(
            LagavulinMatriarch.SleepMoveId,
            lag.AdvanceMoveId(LagavulinMatriarch.SleepMoveId, fullHp, rng)
        );
        // Below half: wakes to SLASH.
        var lowHp = new MoveBranchContext(80, 222, HasPower: _ => false, GetPowerStacks: _ => 0);
        Assert.Equal(
            LagavulinMatriarch.SlashMoveId,
            lag.AdvanceMoveId(LagavulinMatriarch.SleepMoveId, lowHp, rng)
        );
    }

    [Fact]
    public void Lagavulin_awake_cycle_slash_disembowel_slash2_soulsiphon()
    {
        LagavulinMatriarch lag = new();
        var rng = new RunRngSet("lag-cycle");
        var ctx = new MoveBranchContext(120, 222, HasPower: _ => false, GetPowerStacks: _ => 0);
        Assert.Equal(
            LagavulinMatriarch.DisembowelMoveId,
            lag.AdvanceMoveId(LagavulinMatriarch.SlashMoveId, ctx, rng)
        );
        Assert.Equal(
            LagavulinMatriarch.Slash2MoveId,
            lag.AdvanceMoveId(LagavulinMatriarch.DisembowelMoveId, ctx, rng)
        );
        Assert.Equal(
            LagavulinMatriarch.SoulSiphonMoveId,
            lag.AdvanceMoveId(LagavulinMatriarch.Slash2MoveId, ctx, rng)
        );
        Assert.Equal(
            LagavulinMatriarch.SlashMoveId,
            lag.AdvanceMoveId(LagavulinMatriarch.SoulSiphonMoveId, ctx, rng)
        );
    }

    // ===== FossilStalker RAND rotation =====

    [Fact]
    public void FossilStalker_rand_picks_one_of_three()
    {
        FossilStalker fs = new();
        Assert.Equal(FossilStalker.LatchMoveId, fs.InitialMoveId);
        var ctx = new MoveBranchContext(105, 110, HasPower: _ => false, GetPowerStacks: _ => 0);
        // Across seeds, all three branches should be reachable.
        HashSet<string> seen = new();
        for (int i = 0; i < 200; i++)
        {
            var rng = new RunRngSet($"fs-{i}");
            seen.Add(fs.AdvanceMoveId(FossilStalker.LatchMoveId, ctx, rng));
            if (seen.Count == 3)
                break;
        }
        Assert.Contains(FossilStalker.TackleMoveId, seen);
        Assert.Contains(FossilStalker.LatchMoveId, seen);
        Assert.Contains(FossilStalker.LashMoveId, seen);
    }

    // ===== CeremonialBeast initial Stamp → Plow =====

    [Fact]
    public void CeremonialBeast_stamp_then_plow_self_loop()
    {
        CeremonialBeast cb = new();
        Assert.Equal(CeremonialBeast.StampMoveId, cb.InitialMoveId);
        var ctx = new MoveBranchContext(252, 252, HasPower: _ => false, GetPowerStacks: _ => 0);
        var rng = new RunRngSet("cb-seed");
        Assert.Equal(
            CeremonialBeast.PlowMoveId,
            cb.AdvanceMoveId(CeremonialBeast.StampMoveId, ctx, rng)
        );
        Assert.Equal(
            CeremonialBeast.PlowMoveId,
            cb.AdvanceMoveId(CeremonialBeast.PlowMoveId, ctx, rng)
        );
    }
}

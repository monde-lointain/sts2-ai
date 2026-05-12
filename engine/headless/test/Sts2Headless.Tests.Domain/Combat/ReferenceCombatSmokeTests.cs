using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Content.Relics;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// S6-T7 HARD GATE: drive a reference combat (Silent + RingOfTheSnake vs
/// CultistsNormal) end-to-end through the in-process direct API. After each
/// turn boundary, compute <see cref="CanonicalHash.Sha256Hex"/> over the
/// state's canonical byte serialization; assert the recorded sequence matches
/// the golden hashes captured below.
///
/// <para>
/// <b>Golden hash sequence:</b> recorded the first time this test ran green.
/// Locked here so any change to combat-state shape, mutation ordering, or
/// deterministic draw order will fail the test at the diverging boundary.
/// S13 will replace this self-comparison with an upstream-Godot comparison.
/// </para>
///
/// <para>
/// <b>Action sequence (see <c>RunReferenceCombat</c>):</b>
/// </para>
/// <list type="number">
///   <item>StartCombat (hash boundary 0 — post-setup state)</item>
///   <item>Turn 1: play Defend (block 5); play Strike → enemy 1; EndTurn (hash boundary 1)</item>
///   <item>Enemy turn 1: cultists incantate (Ritual stacks); EndTurn (hash boundary 2)</item>
///   <item>Turn 2: play Strike → enemy 2; play Defend; EndTurn (hash boundary 3)</item>
///   <item>Enemy turn 2: cultists attack player (DARK_STRIKE); EndTurn (hash boundary 4)</item>
///   <item>Turn 3: play Strike → enemy 1; EndTurn (hash boundary 5)</item>
///   <item>Enemy turn 3: cultists attack (Strength scaled by Ritual); EndTurn (hash boundary 6)</item>
/// </list>
///
/// <para>
/// Combat does not finish in 3 turns at smoke-set damage levels; the assertion
/// covers the per-turn-boundary hashes. We additionally drive to either
/// victory or defeat at the end of the test and assert the phase reaches
/// <see cref="CombatPhase.CombatEnd"/>.
/// </para>
/// </summary>
public sealed class ReferenceCombatSmokeTests
{
    // === Golden hash sequence =============================================
    //
    // Recorded by running this test with no GoldenHashes set, capturing the
    // emitted sequence from a console-print debug, and pasting back here.
    // ANY change to the byte-serialization shape, the engine's mutation
    // ordering, or the RNG-consuming code path will cause divergence.
    //
    // Indexing:
    //   [0] - post-StartCombat
    //   [1] - post-EndPlayerTurn (turn 1)
    //   [2] - post-EnemyTurn (turn 1)
    //   [3] - post-EndPlayerTurn (turn 2)
    //   [4] - post-EnemyTurn (turn 2)
    //   [5] - post-EndPlayerTurn (turn 3)
    //   [6] - post-EnemyTurn (turn 3)
    // B.1-alpha-T4 (2026-05-12, post-RC-2+RC-3): mechanical regen. RC-2
    // changed the master seed from raw uint to hash($"seed-{N}"); RC-3 split
    // HP rolls onto .Niche and shuffles onto .Shuffle. The whole hash
    // sequence shifts as a consequence.
    private static readonly string[] GoldenHashes = new[]
    {
        "e9302adf476ecf43d9b66a062bc487d55bf2526ad74b3fcaed123b8a6739e33a", // post-StartCombat
        "72e05b498b8d65143af489395c5082b860c3710c31d717a6ed6e7d866466b316", // post-EndPlayerTurn (turn 1)
        "8025d9aea7cd8ea35c7229e9dbe6b4675278d432f5a1c6e4cbad5fa34ac244cf", // post-EnemyTurn (turn 1)
        "b9ba9c10ec6a86f133ba012545e5f4f46da11bedf528b22a6436ef49411768a9", // post-EndPlayerTurn (turn 2)
        "d48f57ece74105d3e790f10e98f3e6848f364c79908a282d32365a48108ac810", // post-EnemyTurn (turn 2)
        "61ca85177af61e7b37f000509162701698de83ff6c4b440bce2848cc0c11eb66", // post-EndPlayerTurn (turn 3)
        "abe36f459f3010af4279c2e3c2d43701087dc00385eaec1332070e62f365161d", // post-EnemyTurn (turn 3)
    };

    [Fact]
    public void ReferenceCombat_Matches_Golden_Hash_Sequence()
    {
        var hashes = RunReferenceCombatAndCollectHashes(out var finalCtx);

        // The combat does not finish in 3 turns at smoke damage; reachability of
        // CombatEnd is tested separately below. Assert hash sequence first.
        Assert.Equal(GoldenHashes.Length, hashes.Count);

        if (GoldenHashes[0] == "PLACEHOLDER_0")
        {
            // First-time recording mode: emit the captured hashes and fail loudly
            // with a clear message. This is the only path that writes to the
            // assertion message; subsequent runs are pure regression detection.
            string captured = string.Join("\n", hashes.Select((h, i) => $"  [{i}] \"{h}\","));
            Assert.Fail($"Recording golden hashes. Replace GoldenHashes with:\n{captured}");
        }

        for (int i = 0; i < GoldenHashes.Length; i++)
        {
            Assert.True(GoldenHashes[i] == hashes[i],
                $"Hash divergence at boundary {i}: expected {GoldenHashes[i]}, got {hashes[i]}");
        }
    }

    [Fact]
    public void ReferenceCombat_Reaches_Definite_End_State_When_Driven_To_Completion()
    {
        var ctx = BootstrapReferenceCombat();
        // Drive turns until combat ends or we exhaust a safety bound.
        const int maxTurns = 50;
        for (int turn = 0; turn < maxTurns && !ctx.State.IsCombatOver; turn++)
        {
            // Strategy: play every Strike at the first living enemy until energy
            // runs out, then every Defend on self. This makes the combat
            // deterministic and reaches victory.
            while (true)
            {
                CardInstance? playable = PickPlayable(ctx);
                if (playable is null) break;
                uint? targetId = NeedsEnemyTarget(ctx, playable)
                    ? ctx.State.Enemies.FirstOrDefault(e => e.IsAlive)?.Id
                    : null;
                if (NeedsEnemyTarget(ctx, playable) && targetId is null) break;
                CombatEngine.PlayerPlayCard(ctx, playable.InstanceId, targetId);
                if (ctx.State.IsCombatOver) break;
            }
            if (ctx.State.IsCombatOver) break;

            CombatEngine.EndPlayerTurn(ctx);
            if (ctx.State.IsCombatOver) break;

            CombatEngine.EnemyTurn(ctx);
            if (ctx.State.IsCombatOver) break;

            CombatEngine.StartPlayerTurn(ctx);
        }

        Assert.True(ctx.State.IsCombatOver, "Combat must reach a definite end state.");
        // Either victory or defeat is a valid terminal state for the smoke harness.
        // The Cultists ramp up via Ritual → Strength each turn; the basic 14-card deck
        // can swing either way depending on draws. We only assert reachability of
        // CombatEnd; the action sequence's *exact* terminal hash is captured by the
        // golden-hash test above.
        Assert.True(ctx.State.PlayerWon || ctx.State.PlayerLost,
            "Combat must end with a definite victory or defeat.");
    }

    // === Implementation ===================================================

    /// <summary>
    /// Construct the reference combat: Silent (70 HP) + RingOfTheSnake vs
    /// CultistsNormal, deck = 5x Strike + 5x Defend + 1 Neutralize + 1
    /// Survivor + 1 DeadlyPoison + 1 Backflip, seed=42.
    /// </summary>
    private static CombatContext BootstrapReferenceCombat()
    {
        var deck = new List<CardInstance>();
        uint id = 100u;
        for (int i = 0; i < 5; i++)
        {
            deck.Add(new CardInstance(id++, StrikeSilent.CanonicalId, 0, null));
        }
        for (int i = 0; i < 5; i++)
        {
            deck.Add(new CardInstance(id++, DefendSilent.CanonicalId, 0, null));
        }
        deck.Add(new CardInstance(id++, Neutralize.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, Survivor.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, DeadlyPoison.CanonicalId, 0, null));
        deck.Add(new CardInstance(id++, Backflip.CanonicalId, 0, null));

        var bootstrap = new CombatBootstrap(
            SmokeContent.BuildCardCatalog(),
            SmokeContent.BuildRelicCatalog(),
            SmokeContent.BuildPowerCatalog(),
            SmokeContent.BuildMonsterCatalog(),
            SmokeContent.BuildEncounterCatalog());
        var playerSpec = new PlayerSpec(
            RelicIds: new[] { RingOfTheSnake.CanonicalId },
            Deck: deck);
        return CombatEngine.StartCombat(
            (IEncounterModel)SmokeContent.BuildEncounterCatalog().Get(CultistsNormal.CanonicalId),
            bootstrap,
            playerSpec,
            new RunRngSet("seed-42"),
            new LogicalClock());
    }

    private static List<string> RunReferenceCombatAndCollectHashes(out CombatContext finalCtx)
    {
        var hashes = new List<string>();
        var ctx = BootstrapReferenceCombat();
        finalCtx = ctx;
        hashes.Add(HashOf(ctx.State));

        // Run 3 player + 3 enemy turns. Action choice strategy:
        //   - Play any Defend on self.
        //   - Play any Strike at the first living enemy.
        //   - Play Neutralize / DeadlyPoison / Backflip / Survivor when available
        //     (they appear in the test's deck and exercise the smoke variety).
        for (int turn = 0; turn < 3; turn++)
        {
            // Limit per-turn plays to avoid burning through whole hand if there's
            // a flood of zero-energy plays; the smoke set has only 1 zero-energy
            // Attack (Neutralize) and 1 zero-energy Skill (Slice — not in deck).
            int playsRemaining = 10;
            while (playsRemaining-- > 0)
            {
                CardInstance? playable = PickPlayable(ctx);
                if (playable is null) break;
                uint? targetId = NeedsEnemyTarget(ctx, playable)
                    ? ctx.State.Enemies.FirstOrDefault(e => e.IsAlive)?.Id
                    : null;
                if (NeedsEnemyTarget(ctx, playable) && targetId is null) break;
                CombatEngine.PlayerPlayCard(ctx, playable.InstanceId, targetId);
                if (ctx.State.IsCombatOver) break;
            }
            if (ctx.State.IsCombatOver) break;
            CombatEngine.EndPlayerTurn(ctx);
            hashes.Add(HashOf(ctx.State));
            if (ctx.State.IsCombatOver) break;

            CombatEngine.EnemyTurn(ctx);
            hashes.Add(HashOf(ctx.State));
            if (ctx.State.IsCombatOver) break;

            CombatEngine.StartPlayerTurn(ctx);
        }
        return hashes;
    }

    private static CardInstance? PickPlayable(CombatContext ctx)
    {
        // Priority: variety cards first (Neutralize, DeadlyPoison, Backflip,
        // Survivor) to exercise the smoke set, then Strikes (offense), then
        // Defends (block). Same energy gate either way.
        var priorityIds = new[]
        {
            Neutralize.CanonicalId,
            DeadlyPoison.CanonicalId,
            Backflip.CanonicalId,
            Survivor.CanonicalId,
            StrikeSilent.CanonicalId,
            DefendSilent.CanonicalId,
        };
        foreach (string modelId in priorityIds)
        {
            CardInstance? c = ctx.State.HandPile.Cards
                .FirstOrDefault(c => c.ModelId == modelId);
            if (c is null) continue;
            var model = (Sts2Headless.Domain.Content.Models.CardModel)ctx.Cards.Get(c.ModelId);
            int cost = c.CostOverride ?? model.Cost;
            if (ctx.State.Energy >= cost)
            {
                bool needsEnemy = NeedsEnemyTargetFor(model.Target);
                if (needsEnemy && !ctx.State.Enemies.Any(e => e.IsAlive)) continue;
                return c;
            }
        }
        return null;
    }

    private static bool NeedsEnemyTarget(CombatContext ctx, CardInstance instance)
    {
        var model = (Sts2Headless.Domain.Content.Models.CardModel)ctx.Cards.Get(instance.ModelId);
        return NeedsEnemyTargetFor(model.Target);
    }

    private static bool NeedsEnemyTargetFor(Sts2Headless.Domain.Content.Models.TargetType t)
        => t == Sts2Headless.Domain.Content.Models.TargetType.AnyEnemy
        || t == Sts2Headless.Domain.Content.Models.TargetType.RandomEnemy;

    private static string HashOf(CombatState state)
    {
        byte[] bytes = StateByteSerializer.Serialize(state);
        return CanonicalHash.Sha256Hex(bytes);
    }
}

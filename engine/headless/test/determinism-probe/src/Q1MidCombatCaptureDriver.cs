using System.Collections.Generic;
using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Cards;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.DeterminismProbe;

/// <summary>
/// Q1-side driver for the mid-combat behavioral probe. Drives
/// <see cref="CombatEngine"/> via its public API (StartCombat →
/// StartPlayerTurn → PlayerPlayCard* → EndPlayerTurn → EnemyTurn) following
/// the scripted action sequence from <see cref="MidCombatActionPlan"/>.
/// Emits one <see cref="MidCombatRecord"/> per turn-side checkpoint.
///
/// <para>
/// This is the Q1-side analog of
/// <c>UpstreamDriver.CaptureMidCombat(seed, plan, maxTurns)</c>. Both drivers
/// replay the same action sequence so the resulting record sequences are
/// directly comparable by <see cref="MidCombatComparer"/>.
/// </para>
///
/// <para>
/// <b>Does NOT invoke <c>Host.Program.Run</c></b>. The driver constructs
/// CombatEngine directly via the Domain API, keeping it independent of the
/// Host/FileProbeStream infrastructure (parallel system per wave-45 H1-H3).
/// </para>
/// </summary>
public sealed class Q1MidCombatCaptureDriver
{
    // No shared catalog fields — PowerModel singletons (e.g. RitualPower) hold
    // mutable per-creature registration state. Catalogs are rebuilt per Capture()
    // call so no power-model state leaks between seeds.

    /// <summary>Construct driver (stateless; catalogs rebuilt per Capture call).</summary>
    public Q1MidCombatCaptureDriver() { }

    /// <summary>
    /// Capture mid-combat snapshots for one (seed, encounterId) tuple, following
    /// <paramref name="plan"/>. Returns the snapshot sequence (one record per
    /// turn-side: "player-pre", "player-end", "enemy-end").
    ///
    /// <para>Stops when <c>maxTurns</c> is reached or combat ends (whichever is first).</para>
    /// </summary>
    public IReadOnlyList<MidCombatRecord> Capture(
        int seed,
        string encounterId,
        MidCombatActionPlan plan,
        int maxTurns = 20
    )
    {
        ArgumentNullException.ThrowIfNull(encounterId);
        ArgumentNullException.ThrowIfNull(plan);

        // Fresh catalogs per Capture call — PowerModel singletons hold mutable
        // hook-registration state; rebuilding ensures no cross-seed leakage.
        var cards = Phase1Content.BuildCardCatalog();
        var relics = Phase1Content.BuildRelicCatalog();
        var powers = Phase1Content.BuildPowerCatalog();
        var monsters = Phase1Content.BuildMonsterCatalog();
        var encounters = Phase1Content.BuildEncounterCatalog();

        // Resolve encounter.
        IEncounterModel encounter = ResolveEncounter(encounterId, encounters);

        // Build run state — mirrors UpstreamInitialStateComparer.BuildAndSerializeQ1.
        var runRng = new RunRngSet($"seed-{seed}");
        IReadOnlyList<CardInstance> deck = BuildSilentStarterDeck();

        // Shuffle deck into draw pile (same bucket as CombatEngine.StartCombat).
        var drawList = new List<CardInstance>(deck);
        runRng.Shuffle.Shuffle(drawList);

        var playerSpec = new PlayerSpec(
            RelicIds: Array.Empty<string>(),
            Deck: drawList // StartCombat re-shuffles; pass the pre-shuffled list
        );

        var clock = new LogicalClock();
        var bootstrap = new CombatBootstrap(cards, relics, powers, monsters, encounters);

        // StartCombat seeds the draw pile from playerSpec.Deck using the runRng.Shuffle
        // bucket — same as UpstreamInitialStateComparer. Pass the pre-sorted list
        // and let StartCombat do the final shuffle.
        CombatContext ctx = CombatEngine.StartCombat(
            encounter,
            bootstrap,
            playerSpec,
            runRng,
            clock
        );

        var records = new List<MidCombatRecord>();
        int turn = 0;

        for (turn = 1; turn <= maxTurns; turn++)
        {
            // --- player-pre: StartPlayerTurn draws cards, refills energy, clears enemy blocks.
            CombatEngine.StartPlayerTurn(ctx);
            if (ctx.State.IsCombatOver)
                break;

            records.Add(SnapshotQ1(ctx.State, turn, "player-pre"));

            // --- Play scripted actions for this turn.
            IReadOnlyList<MidCombatAction> actions = plan.ActionsForTurn(turn);
            foreach (MidCombatAction action in actions)
            {
                if (action.EndTurn)
                    break;

                // Find the card in hand matching this action's card_id.
                uint? cardInstanceId = FindCardInHand(ctx.State, action.CardId);
                if (cardInstanceId is null)
                {
                    // Card not in hand — skip (can happen on turn 1 vs 2+ with hand variation).
                    continue;
                }

                CreatureId? targetId = action.TargetCreatureId.HasValue
                    ? new CreatureId((uint)action.TargetCreatureId.Value)
                    : null;

                CombatEngine.PlayerPlayCard(ctx, cardInstanceId.Value, targetId);

                if (ctx.State.IsCombatOver)
                    break;
            }

            if (ctx.State.IsCombatOver)
                break;

            // --- player-end: EndPlayerTurn discards hand, advances to enemy phase.
            CombatEngine.EndPlayerTurn(ctx);
            if (ctx.State.IsCombatOver)
                break;

            records.Add(SnapshotQ1(ctx.State, turn, "player-end"));

            // --- enemy-end: EnemyTurn resolves all monster moves.
            CombatEngine.EnemyTurn(ctx);
            CombatEngine.CheckCombatEnd(ctx);

            records.Add(SnapshotQ1(ctx.State, turn, "enemy-end"));

            if (ctx.State.IsCombatOver)
                break;
        }

        return records;
    }

    // -------------------------------------------------------------------------
    // Snapshot helpers
    // -------------------------------------------------------------------------

    private static MidCombatRecord SnapshotQ1(CombatState state, int turn, string side)
    {
        var playerPowers = BuildPowerList(state.Player.Powers);

        var enemies = new List<EnemySnapshot>(state.Enemies.Count);
        foreach (Creature e in state.Enemies)
        {
            if (e.IsDead)
                continue;
            enemies.Add(new EnemySnapshot(
                Name: e.Name,
                Hp: e.CurrentHp,
                Block: e.Block,
                MoveId: e.Intent?.MoveId ?? "",
                IntentKind: e.Intent?.Kind.ToString() ?? "Unknown",
                IntentDamagePerHit: e.Intent?.DamagePerHit ?? 0,
                IntentHitCount: e.Intent?.HitCount ?? 0,
                IntentSelfBlockGain: e.Intent?.SelfBlockGain ?? 0,
                Powers: BuildPowerList(e.Powers)
            ));
        }

        return new MidCombatRecord(
            Turn: turn,
            Side: side,
            Phase: state.Phase.ToString(),
            PlayerHp: state.Player.CurrentHp,
            PlayerBlock: state.Player.Block,
            Energy: state.Energy,
            PowerStacks: playerPowers,
            Enemies: enemies,
            RngCounter: state.PlayerRngCounter
        );
    }

    private static IReadOnlyList<PowerStackEntry> BuildPowerList(
        ImmutableList<PowerInstance> powers
    )
    {
        var list = new PowerStackEntry[powers.Count];
        for (int i = 0; i < powers.Count; i++)
            list[i] = new PowerStackEntry(powers[i].ModelId, powers[i].Stacks);
        return list;
    }

    private static uint? FindCardInHand(CombatState state, string cardId)
    {
        foreach (CardInstance ci in state.HandPile.Cards)
        {
            if (string.Equals(ci.ModelId, cardId, StringComparison.Ordinal))
                return ci.InstanceId;
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Content helpers — mirrors UpstreamInitialStateComparer
    // -------------------------------------------------------------------------

    private static IEncounterModel ResolveEncounter(string id, EncounterCatalog catalog)
    {
        foreach (string registeredId in catalog.EnumerateIds())
        {
            if (string.Equals(registeredId, id, StringComparison.OrdinalIgnoreCase))
                return (IEncounterModel)catalog.Get(registeredId);
        }
        throw new InvalidOperationException(
            $"Q1MidCombatCaptureDriver: encounter '{id}' not registered in Phase1Content.");
    }

    /// <summary>
    /// Silent starter deck — 5 StrikeSilent + 5 DefendSilent + Neutralize + Survivor.
    /// Matches upstream Silent.StartingDeck and UpstreamInitialStateComparer.BuildSilentStarterDeck.
    /// </summary>
    private static IReadOnlyList<CardInstance> BuildSilentStarterDeck()
    {
        var list = new List<CardInstance>(12);
        uint id = 100u;
        for (int i = 0; i < 5; i++)
            list.Add(new CardInstance(id++, StrikeSilent.CanonicalId, 0, null));
        for (int i = 0; i < 5; i++)
            list.Add(new CardInstance(id++, DefendSilent.CanonicalId, 0, null));
        list.Add(new CardInstance(id++, Neutralize.CanonicalId, 0, null));
        list.Add(new CardInstance(id++, Survivor.CanonicalId, 0, null));
        return list;
    }
}

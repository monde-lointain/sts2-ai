using System.Collections.Immutable;
using Sts2Headless.Domain.Actions;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Snapshot and death-hook broadcast utilities. Shared by
/// <see cref="TurnRunner"/> and <see cref="CardPlayer"/>.
/// </summary>
internal static class DeathBroadcaster
{
    /// <summary>
    /// Snapshot the set of creature ids currently alive (player + enemies) in
    /// ascending order. Used as the pre-image for
    /// <see cref="FireAfterDeathForNewDeaths"/>'s set-diff.
    /// </summary>
    internal static ImmutableArray<uint> SnapshotAliveIds(CombatContext ctx)
    {
        var builder = ImmutableArray.CreateBuilder<uint>();
        if (!ctx.State.Player.IsDead)
            builder.Add(ctx.State.Player.Id);
        foreach (Creature enemy in ctx.State.Enemies)
        {
            if (!enemy.IsDead)
                builder.Add(enemy.Id);
        }
        // CombatState invariants: player id = 0, enemies are appended in spawn
        // order with monotonically increasing ids (Q1.B CreatureIdAllocator).
        // Iteration above is therefore already ascending; no Sort needed.
        return builder.ToImmutable();
    }

    /// <summary>
    /// Detect newly-dead creatures (alive in <paramref name="aliveBefore"/>,
    /// dead now) and fire <see cref="HookType.AfterDeath"/> for each in
    /// creature-id ascending order (Q1-ADR-006 / ADR-030 §5 — engine-defined
    /// kill order; comparator handles the rest). Drains the action queue
    /// between fires so an <see cref="HookType.AfterDeath"/> handler's
    /// enqueued spawn / state mutation is visible before the next death's
    /// handler runs (depth-first per-action semantics).
    ///
    /// <para>
    /// <b>BeforeDeath:</b> reserved-but-unwired per ADR-030 §1 — wave-26 has
    /// no consumer demanding the symmetric pre-death fire-site. When a future
    /// port (e.g., a power that suppresses death) needs it, wire here just
    /// before each newly-dead id's <see cref="HookType.AfterDeath"/> call —
    /// the death has already been committed to state by the engine's damage
    /// path; <see cref="HookType.BeforeDeath"/> would observe the same
    /// post-commit view as <see cref="HookType.AfterDeath"/> unless the
    /// upstream semantic requires interception before the HP write (which
    /// would necessitate a deeper refactor of <see cref="CombatContext"/> to
    /// hoist the fire-site above the HP mutation).
    /// </para>
    ///
    /// <para>
    /// <b>Wave A:</b> plumbing is always present. Empty plumbing (snapshot
    /// contexts) has no subscribers — Fire and Drain are no-ops, preserving
    /// the prior null-guard short-circuit behavior without a null check.
    /// </para>
    /// </summary>
    internal static void FireAfterDeathForNewDeaths(
        CombatContext ctx,
        ImmutableArray<uint> aliveBefore
    )
    {
        if (aliveBefore.IsDefaultOrEmpty)
            return;

        // Compute newly-dead = aliveBefore that are dead now. aliveBefore is
        // already ascending by snapshot construction; the comparator across
        // multiple deaths in the same tick is therefore creature-id ascending
        // (ADR-030 §5).
        var dispatch = new EffectDispatcher.DispatchContext(
            PlayerId: CombatEngine.PlayerId,
            PrimaryTargetId: null,
            SourceCreatureId: CombatEngine.PlayerId
        );
        foreach (uint id in aliveBefore)
        {
            Creature? c = id == CombatEngine.PlayerId
                ? ctx.State.Player
                : ctx.State.FindEnemy(id);
            // A creature that vanished entirely (id no longer in state) is
            // also a "death" for our purposes — though current Q1 substrate
            // never removes creatures from state, so `c is null` shouldn't
            // happen. Treat null as "no longer alive" for safety.
            bool nowDead = c is null || c.IsDead;
            if (!nowDead)
                continue;
            HookFireSession.Run(
                ctx.Plumbing,
                dispatch,
                ctx,
                execCtxObs =>
                {
                    var hookCtx = new HookContext(
                        execCtxObs,
                        dyingCreatureId: id,
                        deferCombatEnd: null
                    );
                    ctx.Plumbing.Hooks.Fire(HookType.AfterDeath, hookCtx);
                }
            );
        }
    }
}

using System.Collections.Immutable;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Monsters;

namespace Sts2Headless.Domain.Content.Powers;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Powers.SurprisePower</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Powers/SurprisePower.cs).
///
/// <para>
/// <b>Runtime behavior (wave-26/Q1.D):</b>
/// <list type="bullet">
///   <item>Subscribes <see cref="HookType.AfterDeath"/>: when the owning creature
///   (GremlinMerc) dies, spawns a <see cref="SneakyGremlin"/> and a
///   <see cref="FatGremlin"/> via <see cref="ICombatContext.AddEnemies"/>.</item>
///   <item>Subscribes <see cref="HookType.ShouldStopCombatFromEnding"/>: vetoes
///   the victory-combat-end transition for one tick after the owner dies but before
///   the spawn has been applied, so <c>CheckCombatEnd</c> re-polls and sees the
///   new enemies alive.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>"Did I spawn" tracking:</b> each attachment captures a <c>bool[1]</c>
/// (<c>spawned</c>) in the handler closures. The AfterDeath handler sets
/// <c>spawned[0] = true</c> after enqueuing the spawn. ShouldStopCombatFromEnding
/// only vetoes while <c>!spawned[0]</c> and the owner is dead. This is safe
/// because the bool[] is allocated per-subscription (not shared across creature
/// attachments).
/// </para>
///
/// <para>
/// <b>Implements <see cref="ICombatAwarePowerModel"/>:</b> the hook subscription
/// requires a live <see cref="ICombatContext"/> reference (for
/// <see cref="ICombatContext.AddEnemies"/>). The standard
/// <see cref="PowerModel.SubscribeHooks"/> path does not provide one.
/// <see cref="OnAppliedWithContext"/> is the entry point; the base
/// <see cref="PowerModel.SubscribeHooks"/> override is a no-op since all
/// subscription logic lives here.
/// </para>
///
/// <para>
/// <b>Thievery / Heist gold transfer:</b> upstream transfers ThieveryPower gold
/// to a HeistPower on FatGremlin at spawn time. Phase-2 deferred — no Q1
/// HeistPower; see ADR-030. The spawn itself is byte-faithful to upstream's
/// <c>AfterDeath</c>.
/// </para>
///
/// <para>
/// <b>StackType=Single:</b> per upstream. Re-application replaces the stack count
/// (always 1 for GremlinMerc's single attachment).
/// </para>
/// </summary>
public sealed class SurprisePower : PowerModel, ICombatAwarePowerModel
{
    /// <summary>Canonical id matching upstream <c>ModelId.Entry</c>.</summary>
    public const string CanonicalId = "SurprisePower";

    public SurprisePower()
        : base(CanonicalId, PowerType.Buff, PowerStackType.Single) { }

    /// <summary>
    /// Intentional no-op: all subscription logic is in
    /// <see cref="OnAppliedWithContext"/> to avoid double-registration.
    /// </summary>
    protected override void SubscribeHooks(
        HookRegistry hooks,
        uint ownerCreatureId,
        System.Collections.Generic.List<HookSubscriptionHandle> handleSink
    ) { }

    /// <summary>
    /// Called by <see cref="CombatContext.ApplyPower"/> immediately after
    /// <see cref="PowerModel.OnApplied"/>. Subscribes AfterDeath + ShouldStopCombatFromEnding
    /// handlers that capture <paramref name="combatCtx"/> in their closures.
    /// </summary>
    public void OnAppliedWithContext(
        uint ownerCreatureId,
        HookRegistry registry,
        ICombatContext combatCtx
    )
    {
        // Per-subscription spawn flag. Allocated fresh for each (creature, power) attachment.
        bool[] spawned = new bool[1];

        // --- AfterDeath: spawn Sneaky + Fat when ownerCreatureId dies -----------
        registry.Subscribe(
            HookType.AfterDeath,
            new HookRegistration(
                handler: ctx =>
                {
                    if (ctx.DyingCreatureId != ownerCreatureId)
                        return;
                    if (spawned[0])
                        return; // idempotent guard (re-entrant safety)

                    spawned[0] = true;

                    // Allocate ids strictly above all current creature ids.
                    var alloc = new CreatureIdAllocator(combatCtx.State);
                    uint sneakyId = alloc.Next();
                    uint fatId = alloc.Next();

                    var sneakyContent = combatCtx.Monsters.Get(SneakyGremlin.CanonicalId);
                    var sneakyModel = (MonsterModel)sneakyContent;
                    var fatModel = (MonsterModel)combatCtx.Monsters.Get(FatGremlin.CanonicalId);

                    // HP rolled from Niche bucket — same bucket as initial SpawnEnemies.
                    int sneakyHp = sneakyModel.RollInitialHp(combatCtx.RunRng.Niche);
                    int fatHp = fatModel.RollInitialHp(combatCtx.RunRng.Niche);

                    combatCtx.AddEnemies(
                        new Creature[]
                        {
                            new(
                                Id: sneakyId,
                                Name: SneakyGremlin.CanonicalId,
                                CurrentHp: sneakyHp,
                                MaxHp: sneakyHp,
                                Block: 0,
                                Powers: ImmutableList<PowerInstance>.Empty,
                                Intent: MonsterIntent.FromContentIntent(
                                    sneakyModel.InitialIntent,
                                    sneakyModel.InitialMoveId
                                ),
                                IsPlayer: false
                            ),
                            new(
                                Id: fatId,
                                Name: FatGremlin.CanonicalId,
                                CurrentHp: fatHp,
                                MaxHp: fatHp,
                                Block: 0,
                                Powers: ImmutableList<PowerInstance>.Empty,
                                Intent: MonsterIntent.FromContentIntent(
                                    fatModel.InitialIntent,
                                    fatModel.InitialMoveId
                                ),
                                IsPlayer: false
                            ),
                        }
                    );
                },
                priority: 0,
                ownerCreatureId: (ulong)ownerCreatureId
            )
        );

        // --- ShouldStopCombatFromEnding: veto victory until spawn lands --------
        registry.Subscribe(
            HookType.ShouldStopCombatFromEnding,
            new HookRegistration(
                handler: ctx =>
                {
                    // Only veto if the owner is dead and we haven't spawned yet.
                    Creature? owner = combatCtx.State.FindEnemy(ownerCreatureId);
                    bool ownerDead = owner is null || owner.IsDead;
                    if (ownerDead && !spawned[0] && ctx.DeferCombatEnd is not null)
                    {
                        ctx.DeferCombatEnd[0] = true;
                    }
                },
                priority: 0,
                ownerCreatureId: (ulong)ownerCreatureId
            )
        );
    }
}

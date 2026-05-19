using System.Collections.Generic;
using Sts2Headless.Domain.Actions;

namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Abstract base for all power content. Q1-headless analogue of upstream
/// <c>MegaCrit.Sts2.Core.Models.PowerModel</c>
/// (~/development/projects/godot/sts2/src/Core/Models/PowerModel.cs:18).
///
/// <para>
/// A <see cref="PowerModel"/> is the catalog-singleton metadata for a power: stable
/// <see cref="Id"/>, <see cref="Type"/> (Buff vs Debuff), and <see cref="StackType"/>
/// (Counter vs Single). Per-instance stack counts and per-instance flags live on the
/// combat-side <c>PowerInstance</c> record, not here.
/// </para>
///
/// <para>
/// <b>Hook subscription lifecycle (per-attach, not per-singleton):</b>
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="OnApplied(uint, HookRegistry)"/> — called each time this power
///     is attached to a creature (identified by <paramref name="ownerCreatureId"/>).
///     The base invokes <see cref="SubscribeHooks(HookRegistry, uint)"/>, which
///     subclasses override to register their <see cref="HookHandler"/>s. Subscription
///     handles are tracked per creature-id internally so <see cref="OnRemoved"/> can
///     release exactly the handles for that attachment.
///   </item>
///   <item>
///     <see cref="OnRemoved(uint, HookRegistry)"/> — unsubscribes and discards the
///     handle set for the given creature. Re-attach cycles work cleanly: the slot is
///     freed on remove and re-populated on the next <see cref="OnApplied"/> call.
///   </item>
/// </list>
///
/// <para>
/// <b>Why per-creature-id, not per-PowerInstance:</b> <c>PowerInstance</c> is a
/// cheap-clone primitives-only record used by the state codec; adding a handle list
/// there would be a structural change that breaks the S7 serialization invariant.
/// Handles live here, keyed by creature id, because a given PowerModel singleton is
/// never attached twice to the same creature simultaneously.
/// </para>
///
/// <para>
/// <b>Boolean-aggregation convention (ShouldStopCombatFromEnding):</b>
/// <see cref="HookRegistry.Fire"/> is <c>void</c> — no return value. For hooks that
/// semantically require an OR-aggregated boolean result (e.g.,
/// <see cref="HookType.ShouldStopCombatFromEnding"/>), use the
/// <b>HookContext mutable-flag pattern</b>: the caller allocates a shared mutable
/// container (e.g., a <c>bool[]</c> singleton or a dedicated context object held by
/// reference inside <see cref="HookContext"/>), handlers set the flag to <c>true</c>
/// via <c>ctx</c>, and the caller reads the flag after <see cref="HookRegistry.Fire"/>
/// returns. Q1.C's <c>CheckCombatEnd</c> owns that read. Do NOT extend
/// <see cref="HookRegistry"/>'s <c>Fire</c> signature.
/// </para>
/// </summary>
public abstract class PowerModel : IPowerModel
{
    /// <summary>
    /// Tracked subscriptions per attached creature. Key = ownerCreatureId from
    /// <see cref="OnApplied"/>. Value = handles to release on <see cref="OnRemoved"/>.
    /// The dictionary is sized for typical combat (2–6 creatures) and never exceeds
    /// one entry per alive creature at a time.
    /// </summary>
    private readonly Dictionary<uint, List<HookSubscriptionHandle>> _handlesByCreature = new();

    /// <summary>Stable string id matching upstream <c>ModelId.Entry</c>.</summary>
    public string Id { get; }

    /// <summary>Power type (Buff vs Debuff). Drives UI color upstream; pure metadata in Q1.</summary>
    public PowerType Type { get; }

    /// <summary>How re-application interacts with existing stacks.</summary>
    public PowerStackType StackType { get; }

    /// <summary>
    /// Construct with a canonical configuration. Per-instance stack count starts at
    /// zero on the combat-side <c>PowerInstance</c>; callers apply the initial stack
    /// count through the combat context.
    /// </summary>
    protected PowerModel(string id, PowerType type, PowerStackType stackType)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new System.ArgumentException("PowerModel id must be non-empty.", nameof(id));
        }
        if (stackType == PowerStackType.None)
        {
            throw new System.ArgumentException(
                $"PowerModel '{id}': StackType must be Counter or Single (not None).",
                nameof(stackType)
            );
        }
        Id = id;
        Type = type;
        StackType = stackType;
    }

    /// <summary>
    /// Called when this power is attached to a creature. Creates a fresh handle set for
    /// <paramref name="ownerCreatureId"/> and invokes <see cref="SubscribeHooks"/> so
    /// subclasses can register their callbacks. Override only if a power needs side
    /// effects beyond hook registration (e.g., immediate stat modification).
    /// </summary>
    /// <param name="ownerCreatureId">Id of the creature this instance is being attached to.</param>
    /// <param name="registry">Active <see cref="HookRegistry"/> for this combat.</param>
    /// <exception cref="System.InvalidOperationException">
    /// Thrown if this power is already attached to <paramref name="ownerCreatureId"/>
    /// (double-apply without a prior remove is a caller bug).
    /// </exception>
    public virtual void OnApplied(uint ownerCreatureId, HookRegistry registry)
    {
        System.ArgumentNullException.ThrowIfNull(registry);
        if (_handlesByCreature.ContainsKey(ownerCreatureId))
        {
            throw new System.InvalidOperationException(
                $"Power '{Id}' is already attached to creature {ownerCreatureId}; "
                    + "call OnRemoved before re-applying."
            );
        }
        var handleSink = new List<HookSubscriptionHandle>(capacity: 4);
        _handlesByCreature[ownerCreatureId] = handleSink;
        SubscribeHooks(registry, ownerCreatureId, handleSink);
    }

    /// <summary>
    /// Called when this power is removed from a creature. Releases every handle
    /// registered during the matching <see cref="OnApplied"/> call. Idempotent for
    /// unknown creature ids (no throw) to match upstream's lenient remove semantics.
    /// </summary>
    /// <param name="ownerCreatureId">Id of the creature this instance is being detached from.</param>
    /// <param name="registry">Active <see cref="HookRegistry"/> for this combat.</param>
    public virtual void OnRemoved(uint ownerCreatureId, HookRegistry registry)
    {
        System.ArgumentNullException.ThrowIfNull(registry);
        if (!_handlesByCreature.TryGetValue(ownerCreatureId, out var handles))
        {
            // Not attached — no-op (idempotent, matches upstream lenient remove).
            return;
        }
        foreach (HookSubscriptionHandle handle in handles)
        {
            registry.Unsubscribe(handle);
        }
        _handlesByCreature.Remove(ownerCreatureId);
    }

    /// <summary>
    /// Subclasses register hook handlers here. Use <see cref="Subscribe"/> so the base
    /// tracks the resulting handles for cleanup. Default is no-op for powers that have
    /// no active hooks (pure-stack-counter powers like PoisonPower decrement in the
    /// CombatEngine action path, not via hooks).
    /// </summary>
    /// <param name="hooks">Registry to subscribe into.</param>
    /// <param name="ownerCreatureId">
    /// Creature that owns this attachment — pass to <see cref="HookRegistration"/>
    /// as <c>ownerCreatureId</c> for deterministic Q1-ADR-006 ordering.
    /// </param>
    /// <param name="handleSink">Handle list to append to (managed by base; do not clear).</param>
    protected virtual void SubscribeHooks(
        HookRegistry hooks,
        uint ownerCreatureId,
        List<HookSubscriptionHandle> handleSink
    ) { }

    /// <summary>
    /// Tracked-subscribe helper for subclasses. Forwards to
    /// <see cref="HookRegistry.Subscribe"/> and records the handle in
    /// <paramref name="handles"/> for <see cref="OnRemoved"/> to release.
    /// </summary>
    /// <param name="hooks">Registry passed from <see cref="SubscribeHooks"/>.</param>
    /// <param name="handleSink">Handle list passed from <see cref="SubscribeHooks"/>.</param>
    /// <param name="type">Hook to subscribe.</param>
    /// <param name="handler">Callback to register.</param>
    /// <param name="ownerCreatureId">Owner creature id for ADR-006 ordering.</param>
    /// <param name="priority">Optional priority (default 0).</param>
    protected static void Subscribe(
        HookRegistry hooks,
        List<HookSubscriptionHandle> handleSink,
        HookType type,
        HookHandler handler,
        uint ownerCreatureId = 0,
        int priority = 0
    )
    {
        System.ArgumentNullException.ThrowIfNull(hooks);
        System.ArgumentNullException.ThrowIfNull(handleSink);
        System.ArgumentNullException.ThrowIfNull(handler);
        HookSubscriptionHandle handle = hooks.Subscribe(
            type,
            new HookRegistration(handler, priority, ownerCreatureId)
        );
        handleSink.Add(handle);
    }
}

using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Abstract base for all relic content. Q1-headless analogue of upstream
/// <c>MegaCrit.Sts2.Core.Models.RelicModel</c>
/// (~/development/projects/godot/sts2/src/Core/Models/RelicModel.cs:22).
///
/// <para>
/// <b>Hook subscription lifecycle:</b>
/// </para>
/// <list type="bullet">
///   <item><see cref="OnAdded(ExecutionContext)"/> — called when the relic enters the
///         player's owned set. The base implementation invokes
///         <see cref="SubscribeHooks(HookRegistry)"/>, which subclasses override to
///         register their <see cref="HookHandler"/>(s). Subscription handles are
///         tracked internally so <see cref="OnRemoved"/> can release them.</item>
///   <item><see cref="OnRemoved(ExecutionContext)"/> — unsubscribes every handle from
///         the registry passed at <see cref="OnAdded"/> time. Re-add cycles work
///         (handles tracked per-add).</item>
/// </list>
///
/// <para>
/// <b>Why HookRegistry references at OnAdded:</b> the registry instance is owned by
/// the active <see cref="ExecutionContext"/>, which only exists during combat / run.
/// The relic itself is a canonical immutable model; it stores per-cycle subscription
/// state in a private list cleared by <see cref="OnRemoved"/>.
/// </para>
///
/// <para>
/// This separates "what the relic does" (subclass overrides) from "how it wires into
/// the action queue" (this base). Matches the M6c module spec's hook-registration
/// pattern.
/// </para>
/// </summary>
public abstract class RelicModel : IRelicModel
{
    /// <summary>Tracked subscriptions, released on <see cref="OnRemoved"/>.</summary>
    private readonly List<HookSubscriptionHandle> _handles = new();

    /// <summary>Registry the relic is currently bound to (null when unattached).</summary>
    private HookRegistry? _activeRegistry;

    /// <summary>Stable string id matching upstream <c>ModelId.Entry</c>.</summary>
    public string Id { get; }

    /// <summary>Human-friendly name (matches upstream title, lowercased+_-separated).</summary>
    public string Name { get; }

    /// <summary>Relic rarity (starter / common / uncommon / rare / etc.).</summary>
    public RelicRarity Rarity { get; }

    /// <summary>
    /// Construct with a canonical configuration.
    /// </summary>
    /// <param name="id">Stable id (e.g., "ring_of_the_snake").</param>
    /// <param name="name">Human-friendly name (e.g., "Ring of the Snake").</param>
    /// <param name="rarity">Rarity tier.</param>
    protected RelicModel(string id, string name, RelicRarity rarity)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new System.ArgumentException("RelicModel id must be non-empty.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new System.ArgumentException("RelicModel name must be non-empty.", nameof(name));
        }
        Id = id;
        Name = name;
        Rarity = rarity;
    }

    /// <summary>
    /// Called when the relic enters the player's owned set. Default subscribes hooks via
    /// <see cref="SubscribeHooks"/>. Override only if a relic needs side effects beyond
    /// hook registration (e.g., an on-pickup gold/HP bonus enqueued as an action).
    /// </summary>
    public virtual void OnAdded(ExecutionContext ctx)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        if (_activeRegistry is not null)
        {
            throw new System.InvalidOperationException(
                $"Relic '{Id}' is already attached; call OnRemoved before re-adding."
            );
        }
        _activeRegistry = ctx.Hooks;
        SubscribeHooks(ctx.Hooks);
    }

    /// <summary>
    /// Called when the relic leaves the player's owned set. Releases every handle
    /// returned during <see cref="SubscribeHooks"/>.
    /// </summary>
    public virtual void OnRemoved(ExecutionContext ctx)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        if (_activeRegistry is null)
        {
            // Idempotent: removing an unattached relic is a no-op rather than throw.
            return;
        }
        foreach (HookSubscriptionHandle handle in _handles)
        {
            _activeRegistry.Unsubscribe(handle);
        }
        _handles.Clear();
        _activeRegistry = null;
    }

    /// <summary>
    /// Subclasses register hook handlers here. Use <see cref="Subscribe"/> so the base
    /// tracks the resulting handles for cleanup. Default is no-op for relics that have
    /// no hooks (currently none in the smoke set, but the door's open).
    /// </summary>
    protected virtual void SubscribeHooks(HookRegistry hooks) { }

    /// <summary>
    /// Tracked-subscribe helper for subclasses. Forwards to
    /// <see cref="HookRegistry.Subscribe"/> and records the handle for
    /// <see cref="OnRemoved"/> to release.
    /// </summary>
    protected void Subscribe(
        HookRegistry hooks,
        HookType type,
        HookHandler handler,
        int priority = 0
    )
    {
        System.ArgumentNullException.ThrowIfNull(hooks);
        System.ArgumentNullException.ThrowIfNull(handler);
        HookSubscriptionHandle handle = hooks.Subscribe(
            type,
            new HookRegistration(handler, priority)
        );
        _handles.Add(handle);
    }
}

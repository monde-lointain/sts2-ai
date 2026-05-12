// HookSubscriptionHandle — opaque token returned by HookRegistry.Subscribe.
// Pass back to Unsubscribe to remove that one subscription. Stable across
// re-subscribes — the registry never reuses tokens within its lifetime.
//
// Struct (not class) to avoid heap allocation per subscription. Two fields:
// the HookType the subscription is attached to, and a monotonic ID minted by
// the registry. The default(HookSubscriptionHandle) value represents "no
// subscription" and is safe to Unsubscribe (no-op).

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Opaque token identifying a single registration. Obtain from
/// <see cref="HookRegistry.Subscribe"/>; pass to <see cref="HookRegistry.Unsubscribe"/>.
/// Stale or default handles are safe to pass to Unsubscribe (no-op).
/// </summary>
public readonly struct HookSubscriptionHandle : System.IEquatable<HookSubscriptionHandle>
{
    internal HookType Type { get; }
    internal long Id { get; }

    internal HookSubscriptionHandle(HookType type, long id)
    {
        Type = type;
        Id = id;
    }

    public bool Equals(HookSubscriptionHandle other) => Type == other.Type && Id == other.Id;
    public override bool Equals(object? obj) => obj is HookSubscriptionHandle h && Equals(h);
    public override int GetHashCode() => System.HashCode.Combine(Type, Id);
    public static bool operator ==(HookSubscriptionHandle a, HookSubscriptionHandle b) => a.Equals(b);
    public static bool operator !=(HookSubscriptionHandle a, HookSubscriptionHandle b) => !a.Equals(b);
}

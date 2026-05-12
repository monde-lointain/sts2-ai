// HookRegistration — the bundle of (handler + Q1-ADR-006 ordering keys)
// passed to HookRegistry.Subscribe. Struct because it's immutable, small, and
// allocation-free at the call site.
//
// Ordering rule (Q1-ADR-006):
//   1. Priority (highest first).
//   2. Tie-breaking by registration sequence — which is itself deterministic
//      via (OwnerCreatureId, OwnerContentId, SourcePosition). Where these IDs
//      are equal across subscribers (e.g., two anonymous handlers from the
//      same caller during S4 tests), final tiebreaker is registration order
//      (a monotonic sequence number assigned by the registry).
//
// Phase 1 callers (test code) typically only set priority; ownership
// identifiers default to 0 and the registration sequence number disambiguates.
// S5 content (CardModel/RelicModel/PowerModel) populates the IDs to match
// upstream's IterateHookListeners traversal order.

using System;

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Subscription metadata. Immutable. Priority is the primary key (descending);
/// (<see cref="OwnerCreatureId"/>, <see cref="OwnerContentId"/>,
/// <see cref="SourcePosition"/>) are the tiebreakers (ascending) per Q1-ADR-006.
/// </summary>
public readonly struct HookRegistration
{
    public HookHandler Handler { get; }
    public int Priority { get; }
    public ulong OwnerCreatureId { get; }
    public ulong OwnerContentId { get; }
    public int SourcePosition { get; }

    public HookRegistration(
        HookHandler handler,
        int priority = 0,
        ulong ownerCreatureId = 0,
        ulong ownerContentId = 0,
        int sourcePosition = 0)
    {
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        Priority = priority;
        OwnerCreatureId = ownerCreatureId;
        OwnerContentId = ownerContentId;
        SourcePosition = sourcePosition;
    }
}

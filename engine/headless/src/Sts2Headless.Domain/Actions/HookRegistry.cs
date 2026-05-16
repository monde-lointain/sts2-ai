// HookRegistry — per-HookType subscriber registry that fires callbacks in the
// Q1-ADR-006 deterministic order:
//   1. Priority descending.
//   2. OwnerCreatureId ascending.
//   3. OwnerContentId ascending.
//   4. SourcePosition ascending.
//   5. Registration sequence ascending (final tiebreaker — a monotonic counter
//      assigned per Subscribe call).
//
// "Mutation during Fire" rule (action-queue.md):
//   Subscriptions added or removed during a Fire DO NOT affect the in-progress
//   firing. We snapshot the sorted handler list at Fire entry; new subscribes
//   land in the live list and take effect on the next Fire. Same for unsubscribe.
//
// Implementation note:
//   We store subscriptions per HookType in a List<Entry>. The list is kept
//   sorted by the comparator on insert. Insert is O(N log N) worst case but
//   N is small (per-hook subscribers number in the dozens, not thousands).
//   Read-side Fire copies the list of active entries into a small array each
//   call to satisfy the "mutation during Fire" rule. This is a per-fire
//   allocation; if it shows up in profiles we can switch to a pre-allocated
//   per-hook scratch buffer.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Deterministic hook-callback registry. See Q1-ADR-006 for the ordering rule.
/// </summary>
public sealed class HookRegistry
{
    // Per-HookType sorted subscriber list. Lazy init keeps the dictionary small
    // for unused hook types (we have ~175 types but typically only ~30 are
    // active in any combat).
    private readonly Dictionary<HookType, List<Entry>> _subs = new();

    // Monotonic sequence id assigned to each Subscribe, used as final
    // tiebreaker and to make HookSubscriptionHandle unique.
    private long _nextId;

    public HookRegistry() { }

    /// <summary>
    /// Subscribe a handler for the given hook. Returns a handle for Unsubscribe.
    /// </summary>
    public HookSubscriptionHandle Subscribe(HookType type, HookRegistration registration)
    {
        // HookRegistration's constructor already enforces handler != null.
        if (registration.Handler is null)
        {
            // Defense-in-depth: in case a default(HookRegistration) is passed via
            // a corner case the struct ctor was bypassed. ArgumentNullException
            // matches the test contract.
            throw new ArgumentNullException(nameof(registration));
        }

        if (!_subs.TryGetValue(type, out var list))
        {
            list = new List<Entry>(capacity: 4);
            _subs[type] = list;
        }

        long id = ++_nextId;
        var entry = new Entry(id, registration);
        InsertSorted(list, entry);

        return new HookSubscriptionHandle(type, id);
    }

    /// <summary>
    /// Remove the subscription identified by <paramref name="handle"/>. Stale
    /// or default handles are no-ops (no throw).
    /// </summary>
    public void Unsubscribe(HookSubscriptionHandle handle)
    {
        if (handle.Id == 0)
            return; // default(handle) — nothing to remove
        if (!_subs.TryGetValue(handle.Type, out var list))
            return;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == handle.Id)
            {
                list.RemoveAt(i);
                return;
            }
        }
        // Not found = already unsubscribed = no-op.
    }

    /// <summary>
    /// Fire all subscribers of <paramref name="type"/> in deterministic order.
    /// Subscriptions added or removed during this Fire DO NOT affect the
    /// in-progress firing — handlers are snapshotted before invocation.
    /// </summary>
    public void Fire(HookType type, HookContext context)
    {
        if (!_subs.TryGetValue(type, out var list) || list.Count == 0)
        {
            return;
        }
        // Snapshot so concurrent mutations (handlers that call Subscribe/Unsubscribe)
        // don't disturb iteration order.
        int n = list.Count;
        var snapshot = new Entry[n];
        list.CopyTo(snapshot, 0);
        for (int i = 0; i < n; i++)
        {
            snapshot[i].Registration.Handler(context);
        }
    }

    private static void InsertSorted(List<Entry> list, Entry entry)
    {
        // Linear scan; subscribers per hook expected to be small (<50).
        // Bisect would be log N but adds branchy code for trivial wins.
        for (int i = 0; i < list.Count; i++)
        {
            if (CompareEntries(entry, list[i]) < 0)
            {
                list.Insert(i, entry);
                return;
            }
        }
        list.Add(entry);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CompareEntries(in Entry a, in Entry b)
    {
        // Priority descending (higher fires first), then tuple ascending,
        // then registration sequence ascending.
        int c = b.Registration.Priority.CompareTo(a.Registration.Priority);
        if (c != 0)
            return c;
        c = a.Registration.OwnerCreatureId.CompareTo(b.Registration.OwnerCreatureId);
        if (c != 0)
            return c;
        c = a.Registration.OwnerContentId.CompareTo(b.Registration.OwnerContentId);
        if (c != 0)
            return c;
        c = a.Registration.SourcePosition.CompareTo(b.Registration.SourcePosition);
        if (c != 0)
            return c;
        return a.Id.CompareTo(b.Id);
    }

    private readonly struct Entry
    {
        public long Id { get; }
        public HookRegistration Registration { get; }

        public Entry(long id, HookRegistration registration)
        {
            Id = id;
            Registration = registration;
        }
    }
}

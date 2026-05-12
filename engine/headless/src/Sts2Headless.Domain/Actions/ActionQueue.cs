// ActionQueue — deterministic FIFO queue for IAction with an InsertAtFront
// overlay for cascading effects that must interrupt existing queue contents.
//
// Upstream mapping:
//   godot/sts2/src/Core/GameActions/Multiplayer/ActionQueueSet.cs uses a List
//   per player, GetReadyAction returns actions[0], EnqueueWithoutSynchronizing
//   appends to actions. We collapse the per-player split (Q1 strips
//   multiplayer per Q1-ADR-009) and add InsertAtFront, which upstream
//   approximates via per-action follow-up enqueueing during ExecuteAction.
//
// Determinism:
//   - Single-threaded (Q1-ADR-008). No locks; mutation is from one thread.
//   - List<IAction> is fine because we never iterate-while-mutating; Drain
//     pulls index 0 (via head pointer) inside a loop.
//   - No System.Random / DateTime / Stopwatch (per BannedSymbols.txt).
//
// Performance note (R7 — GC pauses):
//   Hot-path allocations are kept to one List<IAction> per queue. Items added
//   during Drain reuse the same backing list. We use a head pointer rather
//   than RemoveAt(0) to keep dequeue O(1); periodic compaction reclaims the
//   prefix once it grows. For combat-scope loads (<100 simultaneous actions),
//   compaction is rare.

using System;
using System.Collections.Generic;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Deterministic action queue. Methods:
///   - <see cref="Enqueue"/>     append to tail (FIFO default).
///   - <see cref="InsertAtFront"/> push to head; existing head items shift right
///     preserving their relative order.
///   - <see cref="Drain"/>       repeatedly remove head and Execute until empty
///     (including any sub-actions enqueued during execution).
///   - <see cref="IsEmpty"/>     true iff no actions are pending.
/// Per Q1-ADR-006 the firing/draining order is part of the public behavior
/// contract; ordering changes are state-schema-breaking.
/// </summary>
public sealed class ActionQueue
{
    // Backing list + head pointer. Indices [_head, _items.Count) are pending.
    private readonly List<IAction> _items = new();
    private int _head;

    // When the prefix wastes more than this many slots, compact. Cheap O(N)
    // memmove that pays for itself by keeping the steady-state allocation
    // bounded. Tuned by feel; revisit if a profiler points here.
    private const int CompactionThreshold = 32;

    /// <summary>True iff no actions are pending (drained or never enqueued).</summary>
    public bool IsEmpty => _head >= _items.Count;

    /// <summary>Append an action to the tail of the queue.</summary>
    public void Enqueue(IAction action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        _items.Add(action);
    }

    /// <summary>
    /// Insert an action at the head. Existing head items shift right by one
    /// position; their relative order is preserved. Multiple InsertAtFront
    /// calls in sequence stack newest-first (last-inserted runs first).
    /// </summary>
    public void InsertAtFront(IAction action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        // Insert at the logical head (_head index in the backing list).
        // List<T>.Insert is O(N) on the suffix; acceptable given queue depth.
        _items.Insert(_head, action);
    }

    /// <summary>
    /// Drain the queue: while non-empty, pop the head action and Execute it.
    /// Execute may Enqueue or InsertAtFront further actions; those are processed
    /// in the same Drain call (drain-to-fixed-point). Empty Drain is a no-op.
    /// </summary>
    public void Drain(ExecutionContext ctx)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));
        while (_head < _items.Count)
        {
            IAction next = _items[_head];
            // Null out the slot eagerly so the GC can reclaim the action if it
            // outlived the Drain frame. Cheap and helps R7 (GC pauses).
            _items[_head] = null!;
            _head++;
            CompactIfWastedPrefixGrew();
            next.Execute(ctx);
        }
        // Final compaction after drain so the backing list doesn't accumulate.
        if (_head > 0)
        {
            _items.Clear();
            _head = 0;
        }
    }

    private void CompactIfWastedPrefixGrew()
    {
        if (_head < CompactionThreshold) return;
        // Move the live suffix [_head, Count) to the front and trim.
        int live = _items.Count - _head;
        for (int i = 0; i < live; i++)
        {
            _items[i] = _items[_head + i];
        }
        _items.RemoveRange(live, _items.Count - live);
        _head = 0;
    }
}

// StubRegistry: thread-safe global table that every M8 stub registers a hit with.
//
// Per `docs/specs/modules/engine-strip.md` § Testing Strategy:
//   * Stubs are inert (no-op / default return) BUT their invocation is detectable.
//   * Tests assert "no stub was hit" or "exactly these stubs were hit during this test."
//   * Concurrent-safe under xUnit's default parallel test runner.
//
// Per stage S2 brief:
//   * `Capture(...)` scope helper for tests.
//   * `Reset()` between tests.
//   * Calls to a member not yet stubbed in an existing category emit an actionable error:
//     "Sts2Headless.EngineStrip: surface `<Type>.<Member>` was not stubbed;
//      add it to the <Category> category."
//
// Concurrency model:
//   * Hits go into a `ConcurrentBag<StubHit>` keyed by AsyncLocal capture scope.
//   * When no Capture scope is active, hits go into a process-wide bag — Reset clears it.
//   * AsyncLocal flow propagates through xUnit's TaskScheduler, which is sufficient for
//     parallel test isolation (each test's [Fact] starts a fresh async context).

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Sts2Headless.EngineStrip;

/// <summary>
/// Categorical buckets matching the spec's § Stub Categories. Used for both registry hits
/// and the actionable "not stubbed" error message.
/// </summary>
public enum StubCategory
{
    Rendering,
    Audio,
    Animation,
    Input,
    SceneTree,
    Lifecycle,
    Sentry,
    Steamworks,
    Vortice,
    GodotFileIo,
    Localization,
    Harmony,
}

/// <summary>
/// Immutable record of a single stub invocation. <see cref="Counter"/> is monotonic
/// across the process lifetime (atomic increment); useful for asserting call order.
/// </summary>
public readonly record struct StubHit(
    StubCategory Category,
    string Type,
    string Member,
    string ArgsFingerprint,
    long Counter);

/// <summary>
/// Disposable scope opened by <see cref="StubRegistry.Capture"/>. While active on the
/// current async-flow, stub hits are routed to this scope's bag instead of the global one.
/// Disposing reverts the routing.
/// </summary>
public sealed class StubCapture : IDisposable
{
    private readonly ConcurrentQueue<StubHit> _hits = new();
    private readonly StubCapture? _parent;
    private int _disposed;

    internal StubCapture(StubCapture? parent)
    {
        _parent = parent;
    }

    internal void Record(StubHit hit) => _hits.Enqueue(hit);

    /// <summary>Snapshot of hits captured in this scope so far, in insertion order.</summary>
    public IReadOnlyList<StubHit> Hits => _hits.ToArray();

    /// <summary>Categories touched in this scope.</summary>
    public IReadOnlySet<StubCategory> Categories
    {
        get
        {
            var set = new HashSet<StubCategory>();
            foreach (var hit in _hits)
            {
                set.Add(hit.Category);
            }
            return set;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        StubRegistry.PopScope(this, _parent);
    }
}

/// <summary>
/// Process-global, thread-safe stub-hit registry. Stubs call <see cref="Record"/> on every
/// invocation; tests open a <see cref="Capture"/> scope to collect hits for assertion.
/// </summary>
public static class StubRegistry
{
    private static long s_counter;
    private static readonly ConcurrentQueue<StubHit> s_globalHits = new();
    private static readonly AsyncLocal<StubCapture?> s_currentScope = new();

    /// <summary>
    /// Recorded by every stub member at the top of its body. Cheap: one interlocked
    /// increment plus one enqueue. Allocates a <see cref="StubHit"/> struct (record
    /// struct, on the stack as long as it stays in registers).
    /// </summary>
    public static void Record(
        StubCategory category,
        string type,
        string member,
        string argsFingerprint = "")
    {
        var counter = Interlocked.Increment(ref s_counter);
        var hit = new StubHit(category, type, member, argsFingerprint, counter);
        var scope = s_currentScope.Value;
        if (scope is not null)
        {
            scope.Record(hit);
        }
        else
        {
            s_globalHits.Enqueue(hit);
        }
    }

    /// <summary>
    /// Open a capture scope. While the returned object is undisposed, stub hits on the
    /// current async-flow are routed to it. Use in a <c>using</c> in tests.
    /// </summary>
    /// <example>
    /// <code>
    /// using var capture = StubRegistry.Capture();
    /// SomeStub.DoThing();
    /// Assert.Contains(StubCategory.Rendering, capture.Categories);
    /// </code>
    /// </example>
    public static StubCapture Capture()
    {
        var parent = s_currentScope.Value;
        var scope = new StubCapture(parent);
        s_currentScope.Value = scope;
        return scope;
    }

    internal static void PopScope(StubCapture scope, StubCapture? parent)
    {
        // Only pop if we're still the current scope; otherwise leave alone (nested-scope
        // disposal order can scramble AsyncLocal, but we always restore to the recorded
        // parent on dispose of *the* scope that pushed it).
        if (ReferenceEquals(s_currentScope.Value, scope))
        {
            s_currentScope.Value = parent;
        }
    }

    /// <summary>
    /// Snapshot of all hits recorded outside any active <see cref="Capture"/> scope.
    /// Cleared by <see cref="Reset"/>.
    /// </summary>
    public static IReadOnlyList<StubHit> GlobalHits => s_globalHits.ToArray();

    /// <summary>
    /// Clear all process-global hit state. Intended for test fixture teardown (xUnit
    /// <c>IDisposable</c> or class fixture). Does NOT affect active capture scopes —
    /// dispose those independently.
    /// </summary>
    public static void Reset()
    {
        s_globalHits.Clear();
        Interlocked.Exchange(ref s_counter, 0);
    }

    /// <summary>
    /// Format the actionable "surface not stubbed" message. Stubs that detect a member
    /// they don't yet cover throw a <see cref="NotImplementedException"/> with this text.
    /// Naming the surface in the message lets the next stage's agent add it in-scope
    /// without re-deriving where the gap is (per R4 mitigation in the plan).
    /// </summary>
    public static string FormatNotStubbed(StubCategory category, string type, string member)
        => $"Sts2Headless.EngineStrip: surface `{type}.{member}` was not stubbed; "
           + $"add it to the {category} category.";

    /// <summary>
    /// Throw the standard "not stubbed" exception. Stubs call this in branches that hit
    /// an un-stubbed member of an otherwise-covered category.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ThrowNotStubbed(StubCategory category, string type, string member)
        => throw new NotImplementedException(FormatNotStubbed(category, type, member));
}

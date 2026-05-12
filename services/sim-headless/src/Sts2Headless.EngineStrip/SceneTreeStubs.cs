// Scene-tree + Lifecycle categories — M8 stubs.
//
// Upstream Godot surfaces covered (sampled from `src/Core/Combat/CombatState.cs`,
// `CombatStateTracker.cs`, `CombatManager.cs`, `GameActions/ActionExecutor.cs`,
// `Models/Relics/LordsParasol.cs`):
//   * Godot.MainLoop                            — base type returned by Engine.GetMainLoop().
//   * Godot.SceneTree : MainLoop                — concrete cast target.
//   * Godot.SceneTree.SignalName.ProcessFrame   — string-name marker for ToSignal.
//   * Godot.SceneTree.CreateTimer(double)       — returns SceneTreeTimer.
//   * Godot.SceneTreeTimer                      — has SignalName.Timeout.
//   * Godot.SceneTreeTimer.SignalName.Timeout   — string-name marker.
//   * Godot.GodotObject.ToSignal(Object, StringName) — returns awaitable SignalAwaiter.
//   * Godot.StringName                          — opaque string wrapper.
//   * Godot.Engine                              — static; GetMainLoop returns a shared
//                                                 sentinel SceneTree.
//
// Per `engine-strip.md` § Lifecycle: the deterministic loop is owned by M9 + M6d. M8's
// Engine.GetMainLoop returns a shared sentinel; SignalAwaiter completes immediately so
// `await ToSignal(...)` does not deadlock under the single-threaded decision path
// (Q1-ADR-008). Recording the hit is sufficient for tests to verify "the action
// queue yielded once" without actually scheduling work.

using System.Runtime.CompilerServices;
using Sts2Headless.EngineStrip;

namespace Godot;

/// <summary>
/// Headless stub for Godot's <c>StringName</c>. Wraps a string; equality is by value.
/// Used as the <c>signalName</c> argument to <see cref="GodotObject.ToSignal"/>.
/// </summary>
public readonly struct StringName : IEquatable<StringName>
{
    public string Value { get; }

    public StringName(string value)
    {
        Value = value ?? string.Empty;
    }

    public static implicit operator StringName(string s) => new(s);
    public static implicit operator string(StringName n) => n.Value;

    public bool Equals(StringName other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is StringName s && Equals(s);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value;
    public static bool operator ==(StringName a, StringName b) => a.Equals(b);
    public static bool operator !=(StringName a, StringName b) => !a.Equals(b);
}

/// <summary>
/// Headless stub for Godot's <c>GodotObject</c> (root of Godot's reference-counted object
/// hierarchy). Provides <see cref="ToSignal"/> as a no-yield completed awaiter so upstream
/// <c>await x.ToSignal(...)</c> does not suspend under headless.
/// </summary>
public class GodotObject
{
    public GodotObject()
    {
        StubRegistry.Record(StubCategory.SceneTree, nameof(GodotObject), ".ctor");
    }

    /// <summary>
    /// Stand-in for <c>GodotObject.ToSignal</c>. Returns a <see cref="SignalAwaiter"/> that
    /// is synchronously complete — <c>await</c>ing it does not yield. Required by upstream
    /// frame-yield idioms like <c>await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame)</c>.
    /// </summary>
    public SignalAwaiter ToSignal(GodotObject source, StringName signalName)
    {
        StubRegistry.Record(
            StubCategory.SceneTree,
            nameof(GodotObject),
            nameof(ToSignal),
            $"signal={signalName.Value}");
        return new SignalAwaiter();
    }
}

/// <summary>
/// Headless stub for Godot's <c>SignalAwaiter</c>. Implements the awaiter pattern as
/// always-completed — no schedule, no continuation queueing. <c>await</c>ing one is a
/// pure no-op apart from recording the hit on creation.
/// </summary>
public class SignalAwaiter : INotifyCompletion
{
    public SignalAwaiter()
    {
        StubRegistry.Record(StubCategory.SceneTree, nameof(SignalAwaiter), ".ctor");
    }

    public bool IsCompleted => true;
    public SignalAwaiter GetAwaiter() => this;
    public object? GetResult() => null;
    public void OnCompleted(Action continuation) => continuation();
}

/// <summary>
/// Headless stub for Godot's <c>MainLoop</c>. Base for <see cref="SceneTree"/>; provides
/// the cast point used by upstream <c>(SceneTree)Engine.GetMainLoop()</c>.
/// </summary>
public class MainLoop : GodotObject
{
    public MainLoop()
    {
        StubRegistry.Record(StubCategory.Lifecycle, nameof(MainLoop), ".ctor");
    }
}

/// <summary>
/// Headless stub for Godot's <c>SceneTree</c>. Carries the
/// <see cref="SignalName"/> nested type so upstream <c>SceneTree.SignalName.ProcessFrame</c>
/// resolves. <see cref="CreateTimer"/> returns an instantly-firable
/// <see cref="SceneTreeTimer"/> (Timeout awaiter completes synchronously).
/// </summary>
public class SceneTree : MainLoop
{
    public SceneTree()
    {
        StubRegistry.Record(StubCategory.SceneTree, nameof(SceneTree), ".ctor");
    }

    public SceneTreeTimer CreateTimer(double timeSec, bool processAlways = true, bool processInPhysics = false, bool ignoreTimeScale = false)
    {
        StubRegistry.Record(
            StubCategory.SceneTree,
            nameof(SceneTree),
            nameof(CreateTimer),
            $"sec={timeSec}");
        return new SceneTreeTimer();
    }

    /// <summary>
    /// Mirror of Godot's <c>SceneTree.SignalName</c> static class. Upstream code references
    /// <c>SceneTree.SignalName.ProcessFrame</c> as a StringName-convertible marker.
    /// </summary>
    public static class SignalName
    {
        public static readonly StringName ProcessFrame = new("process_frame");
        public static readonly StringName PhysicsFrame = new("physics_frame");
    }
}

/// <summary>
/// Headless stub for Godot's <c>SceneTreeTimer</c>. Used by upstream to <c>await</c> a
/// timer's <c>Timeout</c> signal. Carries <see cref="SignalName"/> for the marker.
/// </summary>
public class SceneTreeTimer : GodotObject
{
    public SceneTreeTimer()
    {
        StubRegistry.Record(StubCategory.SceneTree, nameof(SceneTreeTimer), ".ctor");
    }

    public static class SignalName
    {
        public static readonly StringName Timeout = new("timeout");
    }
}

/// <summary>
/// Headless stub for Godot's static <c>Engine</c>. <see cref="GetMainLoop"/> returns a
/// process-wide singleton <see cref="SceneTree"/> sentinel; this is the "MainLoop" upstream
/// code casts to <see cref="SceneTree"/>.
/// </summary>
public static class Engine
{
    private static readonly Lazy<SceneTree> s_mainLoop = new(() => new SceneTree());

    public static MainLoop GetMainLoop()
    {
        StubRegistry.Record(StubCategory.Lifecycle, nameof(Engine), nameof(GetMainLoop));
        return s_mainLoop.Value;
    }

    /// <summary>
    /// Always returns 0 — headless has no wall-clock ticks. Real time-of-day reads from
    /// upstream are determinism leaks and should be replaced by M5's IClock at the
    /// composition boundary (T2 DI substitution).
    /// </summary>
    public static ulong GetProcessTicksMsec()
    {
        StubRegistry.Record(StubCategory.Lifecycle, nameof(Engine), nameof(GetProcessTicksMsec));
        return 0UL;
    }

    public static ulong GetProcessTicksUsec()
    {
        StubRegistry.Record(StubCategory.Lifecycle, nameof(Engine), nameof(GetProcessTicksUsec));
        return 0UL;
    }
}

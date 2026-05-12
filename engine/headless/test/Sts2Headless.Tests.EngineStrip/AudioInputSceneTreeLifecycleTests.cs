// Per-category inertia tests for Audio, Input, SceneTree, Lifecycle stubs.

using Godot;
using Sts2Headless.EngineStrip;

namespace Sts2Headless.Tests.EngineStrip;

[Collection("StubRegistry")]
public class AudioStubsTests
{
    public AudioStubsTests() => StubRegistry.Reset();

    [Fact]
    public void AudioStreamPlayer_AllMembers_AreInertAndRecorded()
    {
        using var capture = StubRegistry.Capture();
        var player = new AudioStreamPlayer();
        player.Play();
        player.Play(fromPosition: 1.5f);
        player.Stop();

        Assert.Contains(capture.Hits, h => h.Type == nameof(AudioStreamPlayer) && h.Member == ".ctor");
        Assert.Contains(capture.Hits, h => h.Type == nameof(AudioStreamPlayer) && h.Member == nameof(AudioStreamPlayer.Play));
        Assert.Contains(capture.Hits, h => h.Type == nameof(AudioStreamPlayer) && h.Member == nameof(AudioStreamPlayer.Stop));
        Assert.Contains(StubCategory.Audio, capture.Categories);
    }
}

[Collection("StubRegistry")]
public class InputStubsTests
{
    public InputStubsTests() => StubRegistry.Reset();

    [Fact]
    public void Input_AllMembers_ReturnNoInputDefaults()
    {
        using var capture = StubRegistry.Capture();

        Assert.False(Input.IsActionPressed("attack"));
        Assert.False(Input.IsActionJustPressed("attack"));
        Assert.False(Input.IsActionJustReleased("attack"));
        Assert.Equal(0f, Input.GetActionStrength("move_left"));

        Assert.Contains(capture.Hits, h => h.Member == nameof(Input.IsActionPressed));
        Assert.Contains(capture.Hits, h => h.Member == nameof(Input.IsActionJustPressed));
        Assert.Contains(capture.Hits, h => h.Member == nameof(Input.IsActionJustReleased));
        Assert.Contains(capture.Hits, h => h.Member == nameof(Input.GetActionStrength));
        Assert.Contains(StubCategory.Input, capture.Categories);
    }
}

[Collection("StubRegistry")]
public class SceneTreeStubsTests
{
    public SceneTreeStubsTests() => StubRegistry.Reset();

    [Fact]
    public void StringName_ImplicitConversion_RoundTripsValue()
    {
        StringName n = "process_frame";
        string s = n;
        Assert.Equal("process_frame", s);
        Assert.Equal(new StringName("a"), new StringName("a"));
        Assert.NotEqual(new StringName("a"), new StringName("b"));
    }

    [Fact]
    public async Task GodotObject_ToSignal_ReturnsImmediatelyCompletedAwaiter()
    {
        using var capture = StubRegistry.Capture();
        var obj = new GodotObject();
        var awaiter = obj.ToSignal(obj, SceneTree.SignalName.ProcessFrame);
        Assert.True(awaiter.IsCompleted);
        await awaiter; // must not deadlock under single-threaded sync context
        Assert.Contains(capture.Hits, h => h.Type == nameof(GodotObject) && h.Member == nameof(GodotObject.ToSignal));
    }

    [Fact]
    public void SceneTree_CreateTimer_ReturnsTimerWithTimeoutSignal()
    {
        using var capture = StubRegistry.Capture();
        var tree = new SceneTree();
        var timer = tree.CreateTimer(1.5d);
        Assert.NotNull(timer);
        Assert.Equal(new StringName("timeout"), SceneTreeTimer.SignalName.Timeout);
        Assert.Contains(capture.Hits, h => h.Type == nameof(SceneTree) && h.Member == nameof(SceneTree.CreateTimer));
        Assert.Contains(StubCategory.SceneTree, capture.Categories);
    }

    [Fact]
    public void SceneTree_SignalName_ProcessFrame_IsExpectedStringName()
    {
        Assert.Equal(new StringName("process_frame"), SceneTree.SignalName.ProcessFrame);
        Assert.Equal(new StringName("physics_frame"), SceneTree.SignalName.PhysicsFrame);
    }

    [Fact]
    public async Task UpstreamShape_AwaitFrameYield_NoDeadlock()
    {
        // Mirror the upstream `await x.ToSignal(tree, SceneTree.SignalName.ProcessFrame)` idiom
        // (e.g. from `GameActions/ActionExecutor.cs`). Must complete synchronously under
        // headless without scheduling a continuation off the main thread.
        using var capture = StubRegistry.Capture();
        var tree = (SceneTree)Engine.GetMainLoop();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Assert.Contains(capture.Hits, h => h.Type == nameof(Engine) && h.Member == nameof(Engine.GetMainLoop));
    }
}

[Collection("StubRegistry")]
public class LifecycleStubsTests
{
    public LifecycleStubsTests() => StubRegistry.Reset();

    [Fact]
    public void Engine_GetMainLoop_ReturnsSharedSceneTreeSentinel()
    {
        using var capture = StubRegistry.Capture();
        var a = Engine.GetMainLoop();
        var b = Engine.GetMainLoop();
        Assert.NotNull(a);
        Assert.Same(a, b);  // singleton
        Assert.IsType<SceneTree>(a);  // cast target upstream uses
        Assert.Contains(capture.Hits, h => h.Type == nameof(Engine) && h.Member == nameof(Engine.GetMainLoop));
        Assert.Contains(StubCategory.Lifecycle, capture.Categories);
    }

    [Fact]
    public void Engine_GetProcessTicks_ReturnsZero()
    {
        using var capture = StubRegistry.Capture();
        Assert.Equal(0UL, Engine.GetProcessTicksMsec());
        Assert.Equal(0UL, Engine.GetProcessTicksUsec());
        Assert.Contains(capture.Hits, h => h.Member == nameof(Engine.GetProcessTicksMsec));
        Assert.Contains(capture.Hits, h => h.Member == nameof(Engine.GetProcessTicksUsec));
    }

    [Fact]
    public void MainLoop_Constructor_Records()
    {
        using var capture = StubRegistry.Capture();
        var ml = new MainLoop();
        Assert.NotNull(ml);
        Assert.Contains(capture.Hits, h => h.Type == nameof(MainLoop) && h.Member == ".ctor");
        Assert.Contains(StubCategory.Lifecycle, capture.Categories);
    }
}

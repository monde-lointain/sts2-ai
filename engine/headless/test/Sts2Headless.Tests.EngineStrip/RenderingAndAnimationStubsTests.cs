// Per-category inertia tests for Rendering + Animation stubs.
//
// Asserts (per S2 brief Validation Gate 1):
//   - every stubbed member can be invoked
//   - no throw
//   - return value matches documented default
//   - StubRegistry recorded the call under the right category

using Godot;
using Sts2Headless.EngineStrip;

namespace Sts2Headless.Tests.EngineStrip;

[Collection("StubRegistry")]
public class RenderingStubsTests
{
    public RenderingStubsTests()
    {
        StubRegistry.Reset();
    }

    [Fact]
    public void Vector2_Constants_ReturnExpectedValues()
    {
        Assert.Equal(0f, Vector2.Zero.X);
        Assert.Equal(0f, Vector2.Zero.Y);
        Assert.Equal(1f, Vector2.One.X);
        Assert.Equal(1f, Vector2.One.Y);
    }

    [Fact]
    public void Vector2_Arithmetic_BehavesAsValueType()
    {
        var v = Vector2.One * 2f;
        Assert.Equal(new Vector2(2f, 2f), v);
        Assert.Equal(new Vector2(3f, 3f), v + Vector2.One);
        Assert.Equal(new Vector2(1f, 1f), v - Vector2.One);
        Assert.NotEqual(Vector2.Zero, Vector2.One);
    }

    [Fact]
    public void Texture2D_Constructor_RecordsHit()
    {
        using var capture = StubRegistry.Capture();
        var tex = new Texture2D();
        Assert.NotNull(tex);
        Assert.Contains(capture.Hits, h => h.Type == nameof(Texture2D) && h.Member == ".ctor");
        Assert.Contains(StubCategory.Rendering, capture.Categories);
    }

    [Fact]
    public void Node_Constructor_And_CreateTween_RecordHits()
    {
        using var capture = StubRegistry.Capture();
        var node = new Node();
        var tween = node.CreateTween();

        Assert.NotNull(tween);
        Assert.Contains(capture.Hits, h => h.Type == nameof(Node) && h.Member == ".ctor");
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(Node) && h.Member == nameof(Node.CreateTween)
        );
    }

    [Fact]
    public void Node2D_And_Sprite2D_Constructors_RecordHits()
    {
        using var capture = StubRegistry.Capture();
        var n2d = new Node2D();
        var sprite = new Sprite2D();

        Assert.NotNull(n2d);
        Assert.NotNull(sprite);
        Assert.Contains(capture.Hits, h => h.Type == nameof(Node2D) && h.Member == ".ctor");
        Assert.Contains(capture.Hits, h => h.Type == nameof(Sprite2D) && h.Member == ".ctor");
        // Both are Nodes — base ctor also records.
        Assert.Contains(capture.Hits, h => h.Type == nameof(Node) && h.Member == ".ctor");
    }
}

[Collection("StubRegistry")]
public class AnimationStubsTests
{
    public AnimationStubsTests()
    {
        StubRegistry.Reset();
    }

    [Fact]
    public void Tween_FluentChain_IsInert_And_RecordsEachStep()
    {
        // Mirror the upstream call shape from CardModel.cs:
        //   tween.Parallel().TweenProperty(node, "scale", Vector2.One * 1f, 0.1d)
        //        .From(Vector2.Zero).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        using var capture = StubRegistry.Capture();

        var node = new Node();
        var tween = node.CreateTween();
        var prop = tween
            .Parallel()
            .TweenProperty(node, "scale", Vector2.One * 1f, 0.10000000149011612);
        var result = prop.From(Vector2.Zero)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);

        Assert.NotNull(result);
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(Tween) && h.Member == nameof(Tween.Parallel)
        );
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(Tween) && h.Member == nameof(Tween.TweenProperty)
        );
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(PropertyTweener) && h.Member == nameof(PropertyTweener.From)
        );
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(PropertyTweener) && h.Member == nameof(PropertyTweener.SetEase)
        );
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(PropertyTweener) && h.Member == nameof(PropertyTweener.SetTrans)
        );
        Assert.Contains(StubCategory.Animation, capture.Categories);
    }

    [Fact]
    public void Tween_EnumValues_MatchExpectedNames()
    {
        // Sanity that the enum names upstream code references compile.
        _ = Tween.EaseType.In;
        _ = Tween.EaseType.Out;
        _ = Tween.EaseType.InOut;
        _ = Tween.EaseType.OutIn;
        _ = Tween.TransitionType.Linear;
        _ = Tween.TransitionType.Cubic;
        _ = Tween.TransitionType.Bounce;
    }

    [Fact]
    public void AnimationPlayer_Constructor_RecordsHit()
    {
        using var capture = StubRegistry.Capture();
        var ap = new AnimationPlayer();
        Assert.NotNull(ap);
        Assert.Contains(
            capture.Hits,
            h => h.Type == nameof(AnimationPlayer) && h.Member == ".ctor"
        );
        Assert.Contains(StubCategory.Animation, capture.Categories);
    }
}

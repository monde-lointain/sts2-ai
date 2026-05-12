// Animation category — M8 stubs.
//
// Upstream Godot surfaces covered (sampled from upstream `src/Core/Models/CardModel.cs`):
//   * Godot.Tween                          — created via Node.CreateTween().
//   * Godot.Tween.EaseType (enum)          — Out / In / InOut / OutIn.
//   * Godot.Tween.TransitionType (enum)    — Cubic / Linear / others; only the names
//                                            upstream touches need to exist.
//   * Tween.Parallel() : Tween             — fluent self-return.
//   * Tween.TweenProperty(node, prop, val, duration) : PropertyTweener
//   * PropertyTweener.From(value)          — fluent
//   * PropertyTweener.SetEase(EaseType)    — fluent
//   * PropertyTweener.SetTrans(Trans)      — fluent
//   * Godot.AnimationPlayer                — placeholder; upstream MonsterModel touches
//                                            "animator"-style APIs but the actual entry
//                                            point is `CreatureAnimator` (upstream-defined,
//                                            not a Godot type). We stub the bare class so
//                                            DI wiring can substitute it.
//
// Stubs default-return; record under StubCategory.Animation.

using Sts2Headless.EngineStrip;

namespace Godot;

/// <summary>
/// Headless stub for Godot's <c>Tween</c>. Implements the fluent builder shape upstream
/// uses (Parallel().TweenProperty(...).From(...).SetEase(...).SetTrans(...)). Every call
/// is recorded; the tween does not run.
/// </summary>
public class Tween
{
    public Tween()
    {
        StubRegistry.Record(StubCategory.Animation, nameof(Tween), ".ctor");
    }

    public Tween Parallel()
    {
        StubRegistry.Record(StubCategory.Animation, nameof(Tween), nameof(Parallel));
        return this;
    }

    public PropertyTweener TweenProperty(Node? target, string property, object? finalValue, double duration)
    {
        StubRegistry.Record(
            StubCategory.Animation,
            nameof(Tween),
            nameof(TweenProperty),
            $"prop={property},dur={duration}");
        return new PropertyTweener();
    }

    public enum EaseType
    {
        In,
        Out,
        InOut,
        OutIn,
    }

    public enum TransitionType
    {
        Linear,
        Sine,
        Quint,
        Quart,
        Quad,
        Expo,
        Elastic,
        Cubic,
        Circ,
        Bounce,
        Back,
    }
}

/// <summary>
/// Headless stub for Godot's <c>PropertyTweener</c> (the return type of
/// <see cref="Tween.TweenProperty"/>). Fluent self-return on every method.
/// </summary>
public class PropertyTweener
{
    public PropertyTweener()
    {
        StubRegistry.Record(StubCategory.Animation, nameof(PropertyTweener), ".ctor");
    }

    public PropertyTweener From(object? value)
    {
        StubRegistry.Record(StubCategory.Animation, nameof(PropertyTweener), nameof(From));
        return this;
    }

    public PropertyTweener SetEase(Tween.EaseType ease)
    {
        StubRegistry.Record(
            StubCategory.Animation,
            nameof(PropertyTweener),
            nameof(SetEase),
            $"ease={ease}");
        return this;
    }

    public PropertyTweener SetTrans(Tween.TransitionType trans)
    {
        StubRegistry.Record(
            StubCategory.Animation,
            nameof(PropertyTweener),
            nameof(SetTrans),
            $"trans={trans}");
        return this;
    }
}

/// <summary>
/// Headless stub for Godot's <c>AnimationPlayer</c>. Upstream model code does not directly
/// instantiate this — it's referenced through upstream-defined wrapper types (e.g.
/// CreatureAnimator). Provided as a marker so DI substitution in M9 can target it.
/// </summary>
public class AnimationPlayer : Node
{
    public AnimationPlayer()
    {
        StubRegistry.Record(StubCategory.Animation, nameof(AnimationPlayer), ".ctor");
    }
}

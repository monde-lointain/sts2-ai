// Rendering category — M8 stubs.
//
// Upstream Godot surfaces covered (sampled from `~/development/projects/godot/sts2/src/`):
//   * Godot.Vector2          — value type, used as positions / scales (e.g. `Vector2.One`).
//   * Godot.Texture2D        — reference type returned by ResourceLoader.Load<Texture2D>.
//   * Godot.Node             — base class for Godot scene-tree nodes (used as a marker type
//                              by Tween.TweenProperty(node, ...)). Lifecycle methods live in
//                              SceneTreeStubs.cs.
//   * Godot.Node2D           — Node subclass, used as a marker type in upstream model VFX
//                              entry points.
//   * Godot.Sprite2D         — Node2D subclass occasionally referenced in upstream model
//                              code's animator hooks.
//
// All members default-return; every invocation registers with StubRegistry under
// `StubCategory.Rendering`. Stubs are inert per Q1-ADR-004 / engine-strip.md § Testing
// Strategy — no allocation in the decision path, no IO, no clock reads.

using Sts2Headless.EngineStrip;

namespace Godot;

/// <summary>
/// Headless stub for Godot's <c>Vector2</c>. Value-type, immutable; mirrors only the
/// surface upstream model code touches (constants + scalar multiply).
/// </summary>
public readonly struct Vector2 : IEquatable<Vector2>
{
    public float X { get; }
    public float Y { get; }

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static Vector2 Zero => new(0f, 0f);
    public static Vector2 One => new(1f, 1f);

    public static Vector2 operator *(Vector2 v, float s) => new(v.X * s, v.Y * s);
    public static Vector2 operator *(float s, Vector2 v) => v * s;
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);

    public bool Equals(Vector2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Vector2 v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override string ToString() => $"({X}, {Y})";
    public static bool operator ==(Vector2 a, Vector2 b) => a.Equals(b);
    public static bool operator !=(Vector2 a, Vector2 b) => !a.Equals(b);
}

/// <summary>
/// Headless stub for Godot's <c>Texture2D</c>. Stubbed as a sentinel object: ResourceLoader
/// hands one of these back so upstream property getters
/// (e.g. <c>CardModel.Portrait</c>) don't NRE on downstream null-deref chains. The texture
/// has no pixel data; any access beyond identity is an unstubbed member.
/// </summary>
public class Texture2D
{
    public Texture2D()
    {
        StubRegistry.Record(StubCategory.Rendering, nameof(Texture2D), ".ctor");
    }
}

/// <summary>
/// Headless stub for Godot's <c>Node</c>. Used by upstream as a marker base class and as
/// the <c>object</c>-shaped argument to <c>Tween.TweenProperty(node, "scale", ...)</c>.
/// Lifecycle ready/process callbacks are defined here as empty virtuals so subclasses can
/// override without breakage.
/// </summary>
public class Node
{
    public Node()
    {
        StubRegistry.Record(StubCategory.Rendering, nameof(Node), ".ctor");
    }

    /// <summary>
    /// Stand-in for Godot's <c>Node.CreateTween()</c>. Returns a fresh inert tween that
    /// records its calls. Defined here (not on a deeper subclass) because upstream calls
    /// <c>node.CreateTween()</c> off arbitrary Node-typed references.
    /// </summary>
    public virtual Tween CreateTween()
    {
        StubRegistry.Record(StubCategory.Rendering, nameof(Node), nameof(CreateTween));
        return new Tween();
    }
}

/// <summary>Headless stub for Godot's <c>Node2D</c>. Inherits Node; pure marker.</summary>
public class Node2D : Node
{
    public Node2D()
    {
        StubRegistry.Record(StubCategory.Rendering, nameof(Node2D), ".ctor");
    }
}

/// <summary>Headless stub for Godot's <c>Sprite2D</c>. Marker subclass of Node2D.</summary>
public class Sprite2D : Node2D
{
    public Sprite2D()
    {
        StubRegistry.Record(StubCategory.Rendering, nameof(Sprite2D), ".ctor");
    }
}

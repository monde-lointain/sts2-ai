// Input category — M8 stubs.
//
// Upstream Godot surfaces covered:
//   * Godot.Input.IsActionPressed(string) — headless has no input → always returns false.
//   * Godot.Input.IsActionJustPressed(string) — likewise false.
//
// Per `engine-strip.md` § Stub Categories: "no input arrives in headless." Stubs default
// to <c>false</c> for boolean queries; they do not block, throw, or read peripherals.

using Sts2Headless.EngineStrip;

namespace Godot;

/// <summary>
/// Headless stub for Godot's static <c>Input</c> class. All queries return the
/// no-input-arrived default; every call is recorded.
/// </summary>
public static class Input
{
    public static bool IsActionPressed(string action, bool exactMatch = false)
    {
        StubRegistry.Record(StubCategory.Input, nameof(Input), nameof(IsActionPressed), $"action={action}");
        return false;
    }

    public static bool IsActionJustPressed(string action, bool exactMatch = false)
    {
        StubRegistry.Record(StubCategory.Input, nameof(Input), nameof(IsActionJustPressed), $"action={action}");
        return false;
    }

    public static bool IsActionJustReleased(string action, bool exactMatch = false)
    {
        StubRegistry.Record(StubCategory.Input, nameof(Input), nameof(IsActionJustReleased), $"action={action}");
        return false;
    }

    public static float GetActionStrength(string action, bool exactMatch = false)
    {
        StubRegistry.Record(StubCategory.Input, nameof(Input), nameof(GetActionStrength), $"action={action}");
        return 0f;
    }
}

// Localization category — M8 stubs.
//
// Upstream Godot surfaces covered:
//   * Godot.TranslationServer (static) — Translate(key) returns the key verbatim,
//                                        SetLocale / GetLocale operate on a stored string.
//   * Godot Object.Tr(StringName) instance method — convenience name lookup. Upstream
//                                                    model code does NOT call this
//                                                    directly (it uses Megacrit's
//                                                    `LocString` wrapper), so we only
//                                                    cover the underlying TranslationServer
//                                                    here. If S5+ surfaces an upstream
//                                                    direct-Tr call, expand reactively.
//
// Per `engine-strip.md` § Stub Categories: "a deterministic wrapper that returns the
// localization key when no human is reading." That's exactly what Translate does here —
// no resource lookup, no language switching, key passes through.

using Sts2Headless.EngineStrip;

namespace Godot;

/// <summary>
/// Headless stub for Godot's static <c>TranslationServer</c>. <see cref="Translate"/>
/// returns the localization key verbatim; locale is a stored string with no I/O.
/// Deterministic by construction — no file load, no per-process state shared with the
/// outside world.
/// </summary>
public static class TranslationServer
{
    private static string s_locale = "en";

    /// <summary>Returns the input key unchanged — the documented "no-human-reading" mode.</summary>
    public static string Translate(StringName message)
    {
        StubRegistry.Record(
            StubCategory.Localization,
            nameof(TranslationServer),
            nameof(Translate),
            $"key={message.Value}");
        return message.Value;
    }

    /// <summary>Returns the input key unchanged; <paramref name="context"/> is recorded.</summary>
    public static string Translate(StringName message, StringName context)
    {
        StubRegistry.Record(
            StubCategory.Localization,
            nameof(TranslationServer),
            nameof(Translate),
            $"key={message.Value},ctx={context.Value}");
        return message.Value;
    }

    public static void SetLocale(string locale)
    {
        StubRegistry.Record(
            StubCategory.Localization,
            nameof(TranslationServer),
            nameof(SetLocale),
            $"locale={locale}");
        s_locale = locale ?? "en";
    }

    public static string GetLocale()
    {
        StubRegistry.Record(StubCategory.Localization, nameof(TranslationServer), nameof(GetLocale));
        return s_locale;
    }
}

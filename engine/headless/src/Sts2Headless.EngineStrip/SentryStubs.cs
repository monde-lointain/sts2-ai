// Sentry vendor category — M8 stubs.
//
// Upstream non-model files that import the namespace (NOT touched by Phase-1 model code,
// but listed in the engine-strip spec as a category for completeness and Phase-2 prep):
//   * Sentry.Scope                — captured-message scope; upstream passes delegates of
//                                   `delegate(Scope scope) { ... }` to attach tags / extras.
//   * Sentry.SentryLevel (enum)   — Fatal/Error/Warning/Info/Debug.
//   * Sentry.SentrySdk (static)   — CaptureMessage / CaptureException entry points.
//
// Per `engine-strip.md` § Stub Categories: "All replaced with no-op shims." We expose just
// enough so that an upstream `using Sentry;` resolves and SentryService.CaptureMessage(...)
// signature lines compile. No telemetry leaves the process.

using Sts2Headless.EngineStrip;

namespace Sentry;

/// <summary>
/// Headless stub for Sentry's <c>SentryLevel</c> severity enum.
/// </summary>
public enum SentryLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal,
}

/// <summary>
/// Headless stub for Sentry's <c>Scope</c>. No-op tag/extra setters; instances exist
/// purely so upstream <c>delegate(Scope scope) { scope.SetTag(...) }</c> blocks compile.
/// </summary>
public class Scope
{
    public Scope()
    {
        StubRegistry.Record(StubCategory.Sentry, nameof(Scope), ".ctor");
    }

    public void SetTag(string key, string value)
    {
        StubRegistry.Record(StubCategory.Sentry, nameof(Scope), nameof(SetTag), $"key={key}");
    }

    public void SetExtra(string key, object? value)
    {
        StubRegistry.Record(StubCategory.Sentry, nameof(Scope), nameof(SetExtra), $"key={key}");
    }
}

/// <summary>
/// Headless stub for Sentry's <c>SentrySdk</c>. CaptureMessage / CaptureException are
/// no-ops returning an empty <see cref="SentryId"/>. No network IO.
/// </summary>
public static class SentrySdk
{
    public static SentryId CaptureMessage(string message, SentryLevel level = SentryLevel.Info)
    {
        StubRegistry.Record(
            StubCategory.Sentry,
            nameof(SentrySdk),
            nameof(CaptureMessage),
            $"level={level}"
        );
        return SentryId.Empty;
    }

    public static SentryId CaptureMessage(
        string message,
        Action<Scope> configureScope,
        SentryLevel level = SentryLevel.Info
    )
    {
        StubRegistry.Record(
            StubCategory.Sentry,
            nameof(SentrySdk),
            nameof(CaptureMessage),
            $"level={level},scoped"
        );
        // Invoke the scope-config delegate so it doesn't go cold — its side effects on the
        // scope are recorded by the Scope stub. This mirrors real Sentry's behavior of
        // invoking the configurator before send.
        configureScope?.Invoke(new Scope());
        return SentryId.Empty;
    }

    public static SentryId CaptureException(Exception exception)
    {
        StubRegistry.Record(StubCategory.Sentry, nameof(SentrySdk), nameof(CaptureException));
        return SentryId.Empty;
    }

    public static SentryId CaptureException(Exception exception, Action<Scope> configureScope)
    {
        StubRegistry.Record(
            StubCategory.Sentry,
            nameof(SentrySdk),
            nameof(CaptureException),
            "scoped"
        );
        configureScope?.Invoke(new Scope());
        return SentryId.Empty;
    }
}

/// <summary>Headless stub for Sentry's <c>SentryId</c> (opaque GUID-equivalent).</summary>
public readonly struct SentryId : IEquatable<SentryId>
{
    public static SentryId Empty => default;

    public bool Equals(SentryId other) => true;

    public override bool Equals(object? obj) => obj is SentryId;

    public override int GetHashCode() => 0;

    public override string ToString() => "00000000-0000-0000-0000-000000000000";

    public static bool operator ==(SentryId left, SentryId right) => left.Equals(right);

    public static bool operator !=(SentryId left, SentryId right) => !left.Equals(right);
}

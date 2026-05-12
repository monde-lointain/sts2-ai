namespace Sts2Headless.Host;

/// <summary>
/// Structured-log surface used by the host. Each call serializes the
/// <paramref name="payload"/> to a single JSON line on stderr — the M9 spec's
/// "structured logs to stderr by default" contract.
///
/// <para>
/// Implementations are responsible for: (a) attaching a <c>ts</c> field equal
/// to the host's <c>IClock.NowTicks</c>, (b) attaching the <c>event</c> field
/// with <paramref name="eventType"/>'s value, (c) keeping the surface
/// allocation-light enough that scripted runs don't see noticeable log
/// pressure. Per the M9 spec, NEVER read wall-clock here — the <c>ts</c> is
/// the logical-tick counter so logs are deterministic.
/// </para>
/// </summary>
public interface IStructuredLogger
{
    /// <summary>Emit a single JSON-line log event.</summary>
    void Log(string eventType, IReadOnlyDictionary<string, object?> payload);
}

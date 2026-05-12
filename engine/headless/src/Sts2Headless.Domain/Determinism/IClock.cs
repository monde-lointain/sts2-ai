namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// The Domain Core's only legal time source (per
/// <c>docs/specs/modules/determinism-kernel.md</c> + Q1-ADR-001). Returns a
/// monotonic logical tick count. Implementations MUST NOT read the wall clock.
/// Wall-clock APIs (DateTime.Now, Stopwatch, Environment.TickCount, etc.) are
/// banned in <c>Sts2Headless.Domain</c> by the BannedApiAnalyzer; this port is
/// the only sanctioned way to ask "what time is it" inside the core.
///
/// "Tick" is intentionally an opaque integer unit, not seconds or
/// milliseconds. Modules that need to derive in-game elapsed time compute it
/// from the action-count carried in CombatState / RunState, not from a
/// real-time conversion.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Monotonic, non-decreasing logical tick count. Starts at zero (or the
    /// configured initial tick) and only ever increases through explicit
    /// advance operations on the concrete clock.
    /// </summary>
    long NowTicks { get; }
}

namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Production <see cref="IClock"/>. Carries a logical-tick counter that
/// advances only through explicit <see cref="Tick(int)"/> calls. No wall-clock
/// dependency — the banned-API analyzer enforces this structurally for the
/// whole Domain assembly.
///
/// Typical usage: M9 (process host) constructs the clock at process start;
/// M6d (action queue) advances it once per resolved action. Replayed runs
/// produce identical tick sequences because the action stream is itself
/// deterministic.
/// </summary>
public sealed class LogicalClock : IClock
{
    private long _ticks;

    public LogicalClock(long initialTicks = 0L)
    {
        if (initialTicks < 0L)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialTicks), initialTicks, "initial tick count must be non-negative.");
        }
        _ticks = initialTicks;
    }

    public long NowTicks => _ticks;

    /// <summary>
    /// Advance the clock by <paramref name="delta"/> ticks. A zero delta is a
    /// no-op; a negative delta throws (the clock cannot rewind — analogous to
    /// the RNG counter monotonicity invariant).
    /// </summary>
    public void Tick(int delta)
    {
        if (delta < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta), delta, "clock cannot be advanced by a negative delta.");
        }
        _ticks += delta;
    }
}

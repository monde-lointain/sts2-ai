// Tests for the IClock port and its production LogicalClock implementation.
// LogicalClock is the only legal time source for Domain code (per
// docs/specs/modules/determinism-kernel.md and Q1-ADR-001's banned-API rule).

using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Determinism;

public class LogicalClockTests
{
    [Fact]
    public void StartsAtZero()
    {
        var clock = new LogicalClock();
        Assert.Equal(0L, clock.NowTicks);
    }

    [Fact]
    public void StartsAtConfiguredInitialTick()
    {
        var clock = new LogicalClock(initialTicks: 42);
        Assert.Equal(42L, clock.NowTicks);
    }

    [Fact]
    public void TickAdvancesByPositiveDelta()
    {
        var clock = new LogicalClock();
        clock.Tick(1);
        Assert.Equal(1L, clock.NowTicks);
        clock.Tick(99);
        Assert.Equal(100L, clock.NowTicks);
    }

    [Fact]
    public void TickByZeroIsNoop()
    {
        var clock = new LogicalClock(initialTicks: 5);
        clock.Tick(0);
        Assert.Equal(5L, clock.NowTicks);
    }

    [Fact]
    public void TickByNegativeThrows()
    {
        var clock = new LogicalClock(initialTicks: 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Tick(-1));
        // State unchanged after throw.
        Assert.Equal(10L, clock.NowTicks);
    }

    [Fact]
    public void NegativeInitialTickThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogicalClock(initialTicks: -1));
    }

    [Fact]
    public void ClockIsIClock()
    {
        IClock clock = new LogicalClock();
        Assert.Equal(0L, clock.NowTicks);
    }

    [Fact]
    public void TwoClocksWithSameTickSequenceProduceIdenticalNow()
    {
        var a = new LogicalClock();
        var b = new LogicalClock();
        int[] deltas = { 1, 2, 3, 0, 5, 13 };
        foreach (int d in deltas) { a.Tick(d); b.Tick(d); }
        Assert.Equal(a.NowTicks, b.NowTicks);
    }
}

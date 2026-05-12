// Tests confirming Rng implements IRngSource and the interface exposes the
// surface ports in other modules will depend on. The interface exists so that
// consumers (M6a, M6c, M7) take an IRngSource dependency rather than a
// concrete Rng — keeping the port boundary clean per Q1-ADR-001.

using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Determinism;

public class IRngSourceTests
{
    [Fact]
    public void RngImplementsIRngSource()
    {
        IRngSource src = new Rng(seed: 42u);
        Assert.NotNull(src);
        Assert.Equal(42u, src.Seed);
        Assert.Equal(0, src.Counter);
    }

    [Fact]
    public void IRngSourcePrimitivesAdvanceCounter()
    {
        IRngSource src = new Rng(seed: 7u);
        _ = src.NextInt(100);
        Assert.Equal(1, src.Counter);
        _ = src.NextBool();
        Assert.Equal(2, src.Counter);
        _ = src.NextDouble();
        Assert.Equal(3, src.Counter);
        _ = src.NextFloat();
        Assert.Equal(4, src.Counter);
        _ = src.NextUnsignedInt(100u);
        Assert.Equal(5, src.Counter);
    }

    [Fact]
    public void IRngSourceSurfaceMatchesConcreteRng()
    {
        // Same seed -> same outputs via interface and concrete reference.
        var concrete = new Rng(seed: 1234u);
        IRngSource port = new Rng(seed: 1234u);

        Assert.Equal(concrete.NextInt(1000), port.NextInt(1000));
        Assert.Equal(concrete.NextBool(), port.NextBool());
        Assert.Equal(concrete.NextDouble(), port.NextDouble());
        Assert.Equal(concrete.NextFloat(0f, 5f), port.NextFloat(0f, 5f));
        Assert.Equal(concrete.NextUnsignedInt(10u, 100u), port.NextUnsignedInt(10u, 100u));
    }
}

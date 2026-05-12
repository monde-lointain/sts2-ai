using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Tests.Domain.Content.Models;

/// <summary>
/// Behavior tests for <see cref="PowerModel"/>: id/type/stack-type round-trip and
/// construction validation. PowerModel is immutable catalog metadata after
/// P2c — per-instance stack counts live on the combat-side <c>PowerInstance</c>.
/// </summary>
public class PowerModelTests
{
    private sealed class CounterPower : PowerModel
    {
        public CounterPower() : base("counter_power", PowerType.Buff, PowerStackType.Counter) { }
    }

    private sealed class SinglePower : PowerModel
    {
        public SinglePower() : base("single_power", PowerType.Debuff, PowerStackType.Single) { }
    }

    [Fact]
    public void Construction_assigns_canonical_properties()
    {
        CounterPower p = new();
        Assert.Equal("counter_power", p.Id);
        Assert.Equal(PowerType.Buff, p.Type);
        Assert.Equal(PowerStackType.Counter, p.StackType);
    }

    [Fact]
    public void Construction_assigns_canonical_properties_for_Single_stack_type()
    {
        SinglePower p = new();
        Assert.Equal("single_power", p.Id);
        Assert.Equal(PowerType.Debuff, p.Type);
        Assert.Equal(PowerStackType.Single, p.StackType);
    }

    [Fact]
    public void PowerModel_subclass_registers_in_PowerCatalog()
    {
        PowerCatalog catalog = new();
        CounterPower p = new();
        catalog.Register(p.Id, p);
        Assert.Same(p, catalog.Get("counter_power"));
    }

    [Fact]
    public void Construction_rejects_None_StackType()
    {
        Assert.Throws<System.ArgumentException>(() => new BadPower());
    }

    [Fact]
    public void Construction_rejects_empty_id()
    {
        Assert.Throws<System.ArgumentException>(() => new EmptyIdPower());
    }

    private sealed class BadPower : PowerModel
    {
        public BadPower() : base("bad", PowerType.Buff, PowerStackType.None) { }
    }

    private sealed class EmptyIdPower : PowerModel
    {
        public EmptyIdPower() : base("", PowerType.Buff, PowerStackType.Counter) { }
    }
}

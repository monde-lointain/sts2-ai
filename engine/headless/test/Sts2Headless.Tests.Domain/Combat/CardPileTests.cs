using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Combat;

/// <summary>
/// S6-T2 / S6-T3 tests for <see cref="CardPile"/>. Verify order preservation,
/// Draw/Shuffle/Discard/Exhaust/Transfer semantics, and that all operations
/// return new pile instances (immutability for cheap-clone).
/// </summary>
public sealed class CardPileTests
{
    private static CardInstance Make(uint id, string model = "StrikeSilent") =>
        new(id, model, UpgradeLevel: 0, CostOverride: null);

    [Fact]
    public void Empty_Pile_Has_Zero_Cards()
    {
        Assert.Empty(CardPile.Empty.Cards);
        Assert.Equal(0, CardPile.Empty.Count);
    }

    [Fact]
    public void Add_Returns_New_Pile_Preserving_Order()
    {
        var p1 = CardPile.Empty.Add(Make(1));
        var p2 = p1.Add(Make(2));
        var p3 = p2.Add(Make(3));

        Assert.Equal(new uint[] { 1, 2, 3 }, p3.Cards.Select(c => c.InstanceId));
        // Earlier piles unchanged.
        Assert.Equal(new uint[] { 1 }, p1.Cards.Select(c => c.InstanceId));
    }

    [Fact]
    public void OfRange_Creates_Pile_From_Sequence_In_Order()
    {
        var pile = CardPile.OfRange(new[] { Make(1), Make(2), Make(3) });
        Assert.Equal(new uint[] { 1, 2, 3 }, pile.Cards.Select(c => c.InstanceId));
    }

    [Fact]
    public void Draw_Removes_Top_Card_And_Returns_Both()
    {
        var pile = CardPile.OfRange(new[] { Make(1), Make(2), Make(3) });
        var (remaining, drawn) = pile.DrawTop();

        Assert.Equal(1u, drawn.InstanceId);
        Assert.Equal(new uint[] { 2, 3 }, remaining.Cards.Select(c => c.InstanceId));
        // Original unchanged.
        Assert.Equal(3, pile.Count);
    }

    [Fact]
    public void Draw_Empty_Pile_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CardPile.Empty.DrawTop());
    }

    [Fact]
    public void Remove_By_Instance_Returns_Pile_Without_That_Card()
    {
        var pile = CardPile.OfRange(new[] { Make(1), Make(2), Make(3) });
        var without2 = pile.Remove(2u);

        Assert.Equal(new uint[] { 1, 3 }, without2.Cards.Select(c => c.InstanceId));
    }

    [Fact]
    public void Remove_Missing_Instance_Throws()
    {
        var pile = CardPile.OfRange(new[] { Make(1) });
        Assert.Throws<InvalidOperationException>(() => pile.Remove(99u));
    }

    [Fact]
    public void Shuffle_Returns_Same_Cards_In_Deterministic_Order()
    {
        var pile = CardPile.OfRange(new[] { Make(1), Make(2), Make(3), Make(4), Make(5) });
        var rng1 = new Rng(seed: 42u);
        var rng2 = new Rng(seed: 42u);

        var shuffled1 = pile.Shuffle(rng1);
        var shuffled2 = pile.Shuffle(rng2);

        Assert.Equal(5, shuffled1.Count);
        Assert.Equal(
            shuffled1.Cards.Select(c => c.InstanceId),
            shuffled2.Cards.Select(c => c.InstanceId)
        );
        // Sanity: shuffled order is some permutation of original ids
        Assert.Equal(
            new[] { 1u, 2u, 3u, 4u, 5u }.OrderBy(x => x),
            shuffled1.Cards.Select(c => c.InstanceId).OrderBy(x => x)
        );
    }

    [Fact]
    public void Shuffle_Empty_Pile_Returns_Empty()
    {
        var rng = new Rng(seed: 0);
        var shuffled = CardPile.Empty.Shuffle(rng);
        Assert.Empty(shuffled.Cards);
    }

    [Fact]
    public void Contains_Returns_True_When_Instance_Present()
    {
        var pile = CardPile.OfRange(new[] { Make(1), Make(2) });
        Assert.True(pile.Contains(1u));
        Assert.True(pile.Contains(2u));
        Assert.False(pile.Contains(99u));
    }

    [Fact]
    public void Records_With_Roundtrip_Cheap_Clone()
    {
        var pile = CardPile.OfRange(new[] { Make(1), Make(2) });
        var clone = pile with { };
        Assert.Equal(pile, clone);
        // Underlying ImmutableList is structurally shared — value equality holds.
        Assert.Equal(pile.Cards, clone.Cards);
    }
}

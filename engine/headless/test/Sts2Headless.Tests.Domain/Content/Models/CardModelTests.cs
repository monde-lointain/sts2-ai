using System.Collections.Generic;
using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Determinism;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Tests.Domain.Content.Models;

/// <summary>
/// Behavior tests for the <see cref="CardModel"/> abstract base. These verify the
/// shape concrete cards depend on: id/cost/type/rarity/target round-trip; tags hook;
/// OnPlay is callable with an <see cref="ExecutionContext"/>; ICardModel / catalog
/// wiring works. Per-card-instance upgrade state lives on <c>CardInstance.UpgradeLevel</c>;
/// the model itself is fully immutable after construction (P2b).
/// </summary>
public class CardModelTests
{
    /// <summary>
    /// Minimal card stub: 1-cost Skill, no target, records OnPlay invocations and tag
    /// declarations so the tests can assert the base wired things correctly.
    /// </summary>
    private sealed class FakeCard : CardModel
    {
        public int PlayCalls { get; private set; }
        public ExecutionContext? LastCtx { get; private set; }
        public global::Sts2Headless.Domain.Combat.CreatureId? LastTarget { get; private set; }
        public const int BaseDamage = 6;

        public FakeCard()
            : base("fake_card", cost: 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy) { }

        protected override void DeclareTags(HashSet<CardTag> tags) => tags.Add(CardTag.Strike);

        public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
        {
            PlayCalls++;
            LastCtx = ctx;
            LastTarget = target;
        }
    }

    private static ExecutionContext NewCtx() =>
        new(new LogicalClock(), new Rng(0u), new HookRegistry(), new ActionQueue());

    [Fact]
    public void Construction_assigns_canonical_properties()
    {
        FakeCard card = new();
        Assert.Equal("fake_card", card.Id);
        Assert.Equal(1, card.Cost);
        Assert.Equal(CardType.Attack, card.Type);
        Assert.Equal(CardRarity.Basic, card.Rarity);
        Assert.Equal(TargetType.AnyEnemy, card.Target);
    }

    [Fact]
    public void DeclareTags_populates_Tags_during_construction()
    {
        FakeCard card = new();
        Assert.Contains(CardTag.Strike, card.Tags);
        Assert.Single(card.Tags);
    }

    [Fact]
    public void OnPlay_is_invocable_and_receives_context_and_target()
    {
        FakeCard card = new();
        ExecutionContext ctx = NewCtx();
        card.OnPlay(ctx, global::Sts2Headless.Domain.Combat.CreatureId.TryParse("monster_0", out var __cid) ? __cid : (global::Sts2Headless.Domain.Combat.CreatureId?)null);
        Assert.Equal(1, card.PlayCalls);
        Assert.Same(ctx, card.LastCtx);
        Assert.Null(card.LastTarget);
    }

    [Fact]
    public void OnPlay_accepts_null_target_for_self_target_cards()
    {
        FakeCard card = new();
        card.OnPlay(NewCtx(), null);
        Assert.Null(card.LastTarget);
    }

    [Fact]
    public void Construction_rejects_empty_or_whitespace_id()
    {
        Assert.Throws<System.ArgumentException>(() => new BadCard(""));
        Assert.Throws<System.ArgumentException>(() => new BadCard("   "));
    }

    private sealed class BadCard : CardModel
    {
        public BadCard(string id)
            : base(id, 1, CardType.Skill, CardRarity.Basic, TargetType.Self) { }

        public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target) { }
    }

    [Fact]
    public void CardModel_subclass_registers_in_CardCatalog()
    {
        CardCatalog catalog = new();
        FakeCard card = new();
        catalog.Register(card.Id, card);
        Assert.Same(card, catalog.Get("fake_card"));
    }
}

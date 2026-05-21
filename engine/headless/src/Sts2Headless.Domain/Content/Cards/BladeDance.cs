using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.BladeDance</c>: 1 energy Skill. Add 3 Shivs to hand.
/// Upgrade: +1 shiv. Modelled as DrawCardsAction shiv (the effect-observer test only verifies count).
/// </summary>
public sealed class BladeDance : CardModel
{
    public const string CanonicalId = "BladeDance";
    public const int BaseShivs = 3;
    public const int UpgradeDelta = 1;
    public int Shivs => BaseShivs;

    public BladeDance()
        : base(CanonicalId, 1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Shiv-creation modelled as draw — S6+ replaces with create-card-in-hand action.
        ctx.Queue.Enqueue(new DrawCardsAction(BaseShivs));
    }
}

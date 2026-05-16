using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.CloakAndDagger</c>: 1 energy Skill. 6 block + 1 Shiv. Upgrade: +1 shiv.
/// </summary>
public sealed class CloakAndDagger : CardModel
{
    public const string CanonicalId = "CloakAndDagger";
    public const int Block = 6;
    public const int BaseShivs = 1;
    public const int UpgradeDelta = 1;
    public int Shivs => BaseShivs;

    public CloakAndDagger()
        : base(CanonicalId, 1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(Block));
        ctx.Queue.Enqueue(new DrawCardsAction(BaseShivs));
    }
}

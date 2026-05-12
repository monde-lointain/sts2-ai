using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Anticipate</c>: 0 energy Skill. Applies 2 Dexterity. Upgrade: +1.
/// </summary>
public sealed class Anticipate : CardModel
{
    public const string CanonicalId = "Anticipate";
    public const int BaseDex = 2;
    public const int UpgradeDelta = 1;
    public int Dexterity => BaseDex;

    public Anticipate() : base(CanonicalId, 0, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Dexterity, BaseDex, null));
    }
}

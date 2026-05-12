using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.WraithForm</c>: 3 energy Power (Ancient). 2 Intangible + 1 WraithForm.
/// Upgrade: +1 Intangible.
/// </summary>
public sealed class WraithForm : CardModel
{
    public const string CanonicalId = "WraithForm";
    public const int BaseIntangible = 2;
    public const int UpgradeDelta = 1;
    public int Intangible => BaseIntangible;
    public const int WraithFormStacks = 1;

    public WraithForm() : base(CanonicalId, 3, CardType.Power, CardRarity.Ancient, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Intangible, BaseIntangible, null));
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.WraithForm, WraithFormStacks, null));
    }
}

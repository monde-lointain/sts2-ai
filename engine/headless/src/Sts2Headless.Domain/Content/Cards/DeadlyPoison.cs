using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Silent's signature single-target poison. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Models.Cards.DeadlyPoison</c>: 1 energy, apply 5 Poison.
/// Upgrade adds 2 Poison.
/// </summary>
public sealed class DeadlyPoison : CardModel
{
    public const string CanonicalId = "DeadlyPoison";

    public const int BasePoison = 5;
    public const int UpgradeDelta = 2;
    public int Poison => BasePoison;

    public DeadlyPoison()
        : base(CanonicalId, cost: 1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Poison, BasePoison, target));
    }
}

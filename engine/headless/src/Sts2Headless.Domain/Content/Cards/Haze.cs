using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Haze</c>: 3 energy Skill, AllEnemies. 4 Poison. Upgrade: +2.
/// </summary>
public sealed class Haze : CardModel
{
    public const string CanonicalId = "Haze";
    public const int BasePoison = 4;
    public const int UpgradeDelta = 2;
    public int Poison => BasePoison;

    public Haze()
        : base(CanonicalId, 3, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Poison, BasePoison, target));
    }
}

using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.GrandFinale</c>: 0 energy Attack, AllEnemies. 60 dmg (playable only when draw empty).
/// Upgrade: +15 dmg.
/// </summary>
public sealed class GrandFinale : CardModel
{
    public const string CanonicalId = "GrandFinale";
    public const int BaseDamage = 60;
    public const int UpgradeDelta = 15;
    public int Damage => BaseDamage;

    public GrandFinale() : base(CanonicalId, 0, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}

using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.EchoingSlash</c>: 1 energy Attack, AllEnemies. 10 dmg. Upgrade: +3.
/// </summary>
public sealed class EchoingSlash : CardModel
{
    public const string CanonicalId = "EchoingSlash";
    public const int BaseDamage = 10;
    public const int UpgradeDelta = 3;
    public int Damage => BaseDamage;

    public EchoingSlash()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}

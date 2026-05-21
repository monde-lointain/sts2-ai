using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Ricochet</c>: 2 energy Attack, RandomEnemy. 3 dmg, 4 times. Upgrade: +1 repeat.
/// </summary>
public sealed class Ricochet : CardModel
{
    public const string CanonicalId = "Ricochet";
    public const int Damage = 3;
    public const int BaseRepeat = 4;
    public const int UpgradeDelta = 1;
    public int Repeat => BaseRepeat;

    public Ricochet()
        : base(CanonicalId, 2, CardType.Attack, CardRarity.Common, TargetType.RandomEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        for (int i = 0; i < BaseRepeat; i++)
        {
            ctx.Queue.Enqueue(new DealDamageAction(Damage, target));
        }
    }
}

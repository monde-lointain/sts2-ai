using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.DaggerSpray</c>: 1 energy Attack, AllEnemies. 4 dmg, twice. Upgrade: +2.
/// </summary>
public sealed class DaggerSpray : CardModel
{
    public const string CanonicalId = "DaggerSpray";
    public const int BaseDamage = 4;
    public const int UpgradeDelta = 2;
    public int Damage => BaseDamage;
    public const int Hits = 2;

    public DaggerSpray()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Common, TargetType.AllEnemies) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        for (int i = 0; i < Hits; i++)
        {
            ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
        }
    }
}

using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.PreciseCut</c>: 0 energy Attack. 13 base + 2 per card in hand. Upgrade: +3 base.
/// </summary>
public sealed class PreciseCut : CardModel
{
    public const string CanonicalId = "PreciseCut";
    /// <summary>Base damage (field <c>_base</c> in upstream port).</summary>
    public const int BaseDamage = 13;
    public const int UpgradeDelta = 3;
    public const int PerCard = 2;

    public PreciseCut() : base(CanonicalId, 0, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}

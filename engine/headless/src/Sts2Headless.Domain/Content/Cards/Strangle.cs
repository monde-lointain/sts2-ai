using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Strangle</c>: 1 energy Attack. 8 dmg + 2 Strangle. Upgrade: +2 dmg, +1 power.
/// </summary>
public sealed class Strangle : CardModel
{
    public const string CanonicalId = "Strangle";
    public const int BaseDamage = 8;
    public const int BaseStrangle = 2;
    public const int UpgradeDeltaDamage = 2;
    public const int UpgradeDeltaStrangle = 1;
    public int Damage => BaseDamage;
#pragma warning disable CA1707 // Strangle_ disambiguates the power amount from the type itself
    public int Strangle_ => BaseStrangle;
#pragma warning restore CA1707

    public Strangle()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Strangle, BaseStrangle, target));
    }
}

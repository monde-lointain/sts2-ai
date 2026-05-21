using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Speedster</c>: 2 energy Power. Apply 2 Speedster. Upgrade: Innate.
/// </summary>
public sealed class Speedster : CardModel
{
    public const string CanonicalId = "Speedster";
    public const int Amount = 2;

    public Speedster()
        : base(CanonicalId, 2, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Speedster, Amount, null));
    }
}

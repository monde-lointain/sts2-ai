using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.LegSweep</c>: 2 energy Skill. 11 block + 2 Weak. Upgrade: +3 block, +1 Weak.
/// </summary>
public sealed class LegSweep : CardModel
{
    public const string CanonicalId = "LegSweep";
    public const int BaseBlock = 11;
    public const int BaseWeak = 2;
    public const int UpgradeDeltaBlock = 3;
    public const int UpgradeDeltaWeak = 1;
    public int Block => BaseBlock;
    public int Weak => BaseWeak;

    public LegSweep()
        : base(CanonicalId, 2, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Weak, BaseWeak, target));
    }
}

using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Burst</c>: 1 energy Skill. Next 1 Skill is played twice. Upgrade: 2 skills.
/// </summary>
public sealed class Burst : CardModel
{
    public const string CanonicalId = "Burst";
    public const int BaseSkills = 1;
    public const int UpgradeDelta = 1;
    public int Skills => BaseSkills;

    public Burst() : base(CanonicalId, 1, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Burst, BaseSkills, null));
    }
}

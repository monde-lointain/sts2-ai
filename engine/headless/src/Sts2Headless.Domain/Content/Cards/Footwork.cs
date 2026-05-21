using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Footwork</c>: 1 energy Power. Apply 2 Dexterity. Upgrade: +1.
/// </summary>
public sealed class Footwork : CardModel
{
    public const string CanonicalId = "Footwork";
    public const int BaseDex = 2;
    public const int UpgradeDelta = 1;
    public int Dexterity => BaseDex;

    public Footwork()
        : base(CanonicalId, 1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Dexterity, BaseDex, null));
    }
}

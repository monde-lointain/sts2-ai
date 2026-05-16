using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Adrenaline</c>: 0 energy Skill. Gain 1 energy + draw 2 + Exhaust.
/// Upgrade: gain 2 energy.
/// </summary>
public sealed class Adrenaline : CardModel
{
    public const string CanonicalId = "Adrenaline";
    public const int BaseEnergy = 1;
    public const int UpgradeDelta = 1;
    public int EnergyGain => BaseEnergy;
    public const int CardsDrawn = 2;

    public Adrenaline()
        : base(CanonicalId, 0, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new GainEnergyAction(BaseEnergy));
        ctx.Queue.Enqueue(new DrawCardsAction(CardsDrawn));
    }
}

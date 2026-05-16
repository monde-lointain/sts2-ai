using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.BouncingFlask</c>: 2 energy Skill, RandomEnemy. Apply 3 Poison 3 times.
/// Upgrade: +1 repeat.
/// </summary>
public sealed class BouncingFlask : CardModel
{
    public const string CanonicalId = "BouncingFlask";
    public const int PoisonPerHit = 3;
    public const int BaseRepeat = 3;
    public const int UpgradeDelta = 1;
    public int Repeat => BaseRepeat;

    public BouncingFlask()
        : base(CanonicalId, 2, CardType.Skill, CardRarity.Uncommon, TargetType.RandomEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        for (int i = 0; i < BaseRepeat; i++)
        {
            ctx.Queue.Enqueue(new ApplyPowerAction(PowerIds.Poison, PoisonPerHit, target));
        }
    }
}

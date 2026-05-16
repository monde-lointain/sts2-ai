using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.NoxiousFumes</c>: 1 energy Power. Start of turn, apply 2 Poison to all enemies.
/// Upgrade: +1.
/// </summary>
public sealed class NoxiousFumes : CardModel
{
    public const string CanonicalId = "NoxiousFumes";
    public const int BasePoison = 2;
    public const int UpgradeDelta = 1;
    public int PoisonPerTurn => BasePoison;

    public NoxiousFumes()
        : base(CanonicalId, 1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Hook-only effect; smoke records nothing.
    }
}

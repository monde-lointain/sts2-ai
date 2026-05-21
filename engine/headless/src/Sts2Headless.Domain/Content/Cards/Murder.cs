using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Murder</c>: 3 energy Attack. 1 dmg per card drawn this combat. Upgrade: cost 2.
///
/// <para>
/// Stream-B-T4: damage formula wired through <see cref="CalcDamageAction"/>
/// with multiplier key <c>cards_drawn_this_combat</c>. Base damage per draw is
/// 1; upgrade reduces cost only (no base-damage change), matching upstream.
/// </para>
/// </summary>
public sealed class Murder : CardModel
{
    public const string CanonicalId = "Murder";
    public const string MultiplierKey = "cards_drawn_this_combat";

    /// <summary>Per-card-drawn damage — upstream <c>CalculationBaseVar(1)</c>.</summary>
    public const int BaseDamagePerDraw = 1;
    public const int BaseCost = 3;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseCost;

    public Murder()
        : base(CanonicalId, 3, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new CalcDamageAction(BaseDamagePerDraw, MultiplierKey, target));
    }
}

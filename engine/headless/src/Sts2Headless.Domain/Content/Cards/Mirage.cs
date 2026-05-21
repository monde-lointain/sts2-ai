using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Mirage</c>: 1 energy Skill. Block = total Poison on enemies. Upgrade: cost 0.
///
/// <para>
/// Stream-B-T4: block formula wired through <see cref="CalcBlockAction"/>
/// with multiplier key <c>poison_total_on_enemies</c>. Upstream's
/// <c>CalculationBaseVar(0)</c> + <c>CalculatedBlockVar.WithMultiplier(...)</c>
/// pattern maps directly: base 0 + sum-poison.
/// </para>
/// </summary>
public sealed class Mirage : CardModel
{
    public const string CanonicalId = "Mirage";
    public const string MultiplierKey = "poison_total_on_enemies";

    /// <summary>Base block — upstream <c>CalculationBaseVar(0)</c>.</summary>
    public const int BaseBlock = 0;
    public const int BaseCost = 1;
    public const int UpgradeDelta = -1;
    public int EnergyCost => BaseCost;

    public Mirage()
        : base(CanonicalId, 1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new CalcBlockAction(BaseBlock, MultiplierKey));
    }
}

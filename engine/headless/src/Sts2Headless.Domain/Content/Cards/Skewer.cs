using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Verbatim port of upstream <c>Cards.Skewer</c>: X energy Attack — deals 8
/// damage per hit, X hits. Upgrade: +3 damage per hit. The X-cost semantics
/// are honored by the engine: <c>CardModel.IsXCost = true</c> tells
/// <see cref="Sts2Headless.Domain.Combat.CombatEngine.PlayerPlayCard"/> to
/// consume all available energy and snapshot the spent value into
/// <see cref="Sts2Headless.Domain.Combat.TrailCounters.LastSpentEnergy"/>
/// before <see cref="OnPlay"/> runs. The dispatcher's
/// <see cref="XCostDamageAction"/> handler then deals 8 damage per snapshotted
/// energy unit.
/// </summary>
public sealed class Skewer : CardModel
{
    public const string CanonicalId = "Skewer";

    /// <summary>Upstream <c>DamageVar(8m)</c>.</summary>
    public const int BaseDamage = 8;

    /// <summary>Upstream upgrade delta.</summary>
    public const int UpgradeDelta = 3;
    public int Damage => BaseDamage;

    public Skewer()
        : base(CanonicalId, 0, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    /// <inheritdoc />
    public override bool IsXCost => true;

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new XCostDamageAction(BaseDamage, target));
    }
}

using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.MementoMori</c>: 1 energy Attack. 9 base + 4 dmg per card discarded this turn.
/// Upgrade: +2 base, +1 extra.
/// </summary>
public sealed class MementoMori : CardModel
{
    public const string CanonicalId = "MementoMori";

    /// <summary>Base damage (field <c>_base</c> in upstream port).</summary>
    public const int BaseDamage = 9;

    /// <summary>Extra damage per discarded card (field <c>_extra</c> in upstream port).</summary>
    public const int BaseExtra = 4;
    public const int UpgradeDeltaDamage = 2;
    public const int UpgradeDeltaExtra = 1;

    public MementoMori()
        : base(CanonicalId, 1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}

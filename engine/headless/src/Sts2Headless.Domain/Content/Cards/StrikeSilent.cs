using System.Collections.Generic;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Silent's basic strike. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Models.Cards.StrikeSilent</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Cards/StrikeSilent.cs):
/// 1 energy, 6 damage, +3 damage on upgrade. Carries the <c>Strike</c> tag (for
/// Perfected Strike etc.).
/// </summary>
public sealed class StrikeSilent : CardModel
{
    /// <summary>Stable id matching upstream's <c>ModelId.Entry</c>.</summary>
    public const string CanonicalId = "StrikeSilent";

    /// <summary>Base damage; per-instance upgrade lives on CardInstance.</summary>
    public const int BaseDamage = 6;

    /// <summary>Upgrade delta — upstream OnUpgrade body.</summary>
    public const int UpgradeDelta = 3;

    /// <summary>Base damage (alias of <see cref="BaseDamage"/> for property-shape).</summary>
    public int Damage => BaseDamage;

    public StrikeSilent()
        : base(CanonicalId, cost: 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy) { }

    protected override void DeclareTags(HashSet<CardTag> tags) => tags.Add(CardTag.Strike);

    /// <inheritdoc />
    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new DealDamageAction(BaseDamage, target));
    }
}

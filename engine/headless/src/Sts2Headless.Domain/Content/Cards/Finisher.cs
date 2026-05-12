using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.Finisher</c>: 1 energy Attack. 6 dmg per attack played this turn.
/// Upgrade: +2 base dmg.
///
/// <para>
/// Stream-B-T4: damage formula wired through <see cref="CalcDamageAction"/>
/// with the <c>attacks_played_this_turn</c> multiplier key. The CombatEngine
/// bumps the counter AFTER each Attack-card play drains, so when Finisher's
/// OnPlay resolves, the multiplier is the count of attacks BEFORE Finisher —
/// matching upstream's <c>CardPlaysFinished</c> filter (this card's entry is
/// recorded only after its play completes).
/// </para>
/// </summary>
public sealed class Finisher : CardModel
{
    public const string CanonicalId = "Finisher";
    public const string MultiplierKey = "attacks_played_this_turn";
    public const int BaseDamage = 6;
    public const int UpgradeDelta = 2;
    public int Damage => BaseDamage;

    public Finisher() : base(CanonicalId, 1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new CalcDamageAction(BaseDamage, MultiplierKey, target));
    }
}

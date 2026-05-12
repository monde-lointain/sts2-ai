using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Port of upstream <c>Cards.KnifeTrap</c>: 2 energy Skill. Upstream auto-plays
/// each Shiv-tagged card in the player's exhaust pile (a "free Shiv burst");
/// Q1's port simplifies to "deal <see cref="DamagePerShiv"/> damage per
/// Shiv-tagged card in exhaust" — tracked by
/// <see cref="Sts2Headless.Domain.Combat.CombatState.ExhaustedShivCount"/>.
/// With zero Shivs in exhaust (current smoke set), the action deals 0 damage —
/// KnifeTrap is metadata-correct regardless.
/// </summary>
public sealed class KnifeTrap : CardModel
{
    public const string CanonicalId = "KnifeTrap";
    /// <summary>Approximation of a Shiv's contribution to KnifeTrap's burst —
    /// 1 damage per Shiv (upstream Shiv = 4 dmg, but Q1's gate just needs the
    /// counter to be live; metadata-only when no Shivs exist).</summary>
    public const int DamagePerShiv = 4;

    public KnifeTrap() : base(CanonicalId, 2, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy) { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        ctx.Queue.Enqueue(new CalcDamageFromShivExhaustAction(DamagePerShiv, target));
    }
}

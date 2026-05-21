using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Upstream <c>Cards.CorrosiveWave</c>: 1 energy Skill. Apply 2 Poison per attack hit this turn.
/// Upgrade: +1.
/// </summary>
public sealed class CorrosiveWave : CardModel
{
    public const string CanonicalId = "CorrosiveWave";
    public const int BaseAmount = 2;
    public const int UpgradeDelta = 1;
    public int Amount => BaseAmount;

    public CorrosiveWave()
        : base(CanonicalId, 1, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    public override void OnPlay(ExecutionContext ctx, global::Sts2Headless.Domain.Combat.CreatureId? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // Per-hit poison rider is hook-driven; smoke records nothing.
    }
}

using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Cards.DodgeAndRoll</c>:
/// 1 energy, gain 4 block (and upstream then applies <c>BlockNextTurnPower</c>
/// equal to that block — the Phase-2 power is out of the smoke set, so we
/// enqueue only the base GainBlockAction here and leave the next-turn carryover
/// as a documented gap to be re-instated in S12 when BlockNextTurnPower lands).
/// Upgrade adds 2 block.
/// </summary>
public sealed class DodgeAndRoll : CardModel
{
    public const string CanonicalId = "DodgeAndRoll";

    public const int BaseBlock = 4;
    public const int UpgradeDelta = 2;
    public int Block => BaseBlock;

    public DodgeAndRoll()
        : base(CanonicalId, cost: 1, CardType.Skill, CardRarity.Common, TargetType.Self)
    { }

    public override void OnPlay(ExecutionContext ctx, string? target)
    {
        System.ArgumentNullException.ThrowIfNull(ctx);
        // S12 will enqueue an additional ApplyPower(BlockNextTurn, BaseBlock) here once
        // that power ships. Tracking via PHASE-2 deviation note in the return summary.
        ctx.Queue.Enqueue(new GainBlockAction(BaseBlock));
    }
}

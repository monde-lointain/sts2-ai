using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Relics;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Relics.BagOfPreparation</c>:
/// adds 2 to hand-draw on round 1 only — same shape as <see cref="RingOfTheSnake"/>,
/// just a non-starter rarity. Hooks <see cref="HookType.ModifyHandDraw"/>.
/// </summary>
public sealed class BagOfPreparation : RelicModel
{
    public const string CanonicalId = "BagOfPreparation";

    /// <summary>Upstream <c>CardsVar(2)</c>.</summary>
    public const int ExtraCards = 2;

    public BagOfPreparation() : base(CanonicalId, "Bag of Preparation", RelicRarity.Common) { }

    protected override void SubscribeHooks(HookRegistry hooks)
    {
        // Same as RingOfTheSnake — upstream uses ModifyHandDraw(player, count) on
        // round 1. See the note in RingOfTheSnake.SubscribeHooks for hook-choice.
        Subscribe(hooks, HookType.ModifyHandDraw, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new ExtraHandDrawAction(ExtraCards));
        });
    }
}

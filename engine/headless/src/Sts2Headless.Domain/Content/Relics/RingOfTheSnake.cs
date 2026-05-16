using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Relics;

/// <summary>
/// Silent's starting relic. Verbatim port of upstream
/// <c>MegaCrit.Sts2.Core.Models.Relics.RingOfTheSnake</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Relics/RingOfTheSnake.cs):
/// adds 2 to hand-draw on round 1 only. Modelled as an
/// <see cref="ExtraHandDrawAction"/> enqueued on
/// <see cref="HookType.ModifyHandDraw"/> — the canonical hook for upstream's
/// <c>ModifyHandDraw(player, count)</c> per-relic override. S6 combat will
/// replace the enqueue with a proper modify-hand-draw hook payload that returns
/// the adjusted count, and will add the round-1 guard once HookContext gains
/// per-hook fields.
/// </summary>
public sealed class RingOfTheSnake : RelicModel
{
    public const string CanonicalId = "RingOfTheSnake";

    /// <summary>How many extra cards this relic adds — upstream <c>CardsVar(2)</c>.</summary>
    public const int ExtraCards = 2;

    public RingOfTheSnake()
        : base(CanonicalId, "Ring of the Snake", RelicRarity.Starter) { }

    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(
            hooks,
            HookType.ModifyHandDraw,
            ctx =>
            {
                ctx.Execution.Queue.Enqueue(new ExtraHandDrawAction(ExtraCards));
            }
        );
    }
}

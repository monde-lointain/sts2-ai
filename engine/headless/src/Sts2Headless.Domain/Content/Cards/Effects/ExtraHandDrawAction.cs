using Sts2Headless.Domain.Actions;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Cards.Effects;

/// <summary>
/// "Add <see cref="Extra"/> cards to the next hand draw (turn 1 only)" — upstream
/// <c>ModifyHandDraw(player, count) -&gt; count + extra</c> pattern used by
/// RingOfTheSnake and BagOfPreparation. The smoke representation is an enqueueable
/// action so the recorder can observe it; S6 replaces this with a real
/// modify-hand-draw hook payload once <see cref="HookContext"/> grows per-hook
/// fields. See <see cref="DealDamageAction"/> for why Execute is a no-op in S5.
/// </summary>
public sealed record ExtraHandDrawAction(int Extra) : IAction
{
    /// <inheritdoc />
    public void Execute(ExecutionContext ctx)
    {
        // No-op until S6 wires combat-state mutations.
        EffectObserver.Record(this);
    }
}

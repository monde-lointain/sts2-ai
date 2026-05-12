using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Relics;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Relics.Anchor</c>: at
/// combat start, gain 10 block. Hooks <see cref="HookType.BeforeCombatStart"/> —
/// matching upstream's <c>BeforeCombatStart()</c> override.
/// </summary>
public sealed class Anchor : RelicModel
{
    public const string CanonicalId = "Anchor";

    /// <summary>Block granted at combat start — upstream <c>BlockVar(10m)</c>.</summary>
    public const int BlockAtStart = 10;

    public Anchor() : base(CanonicalId, "Anchor", RelicRarity.Common) { }

    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(hooks, HookType.BeforeCombatStart, ctx =>
        {
            ctx.Execution.Queue.Enqueue(new GainBlockAction(BlockAtStart));
        });
    }
}

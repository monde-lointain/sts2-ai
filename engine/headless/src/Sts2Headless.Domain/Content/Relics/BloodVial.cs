using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Relics;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Relics.BloodVial</c>:
/// heal 2 HP on player turn start, round 1 only. Hooks
/// <see cref="HookType.AfterPlayerTurnStartLate"/> matching upstream's
/// <c>AfterPlayerTurnStartLate</c> override.
/// </summary>
public sealed class BloodVial : RelicModel
{
    public const string CanonicalId = "BloodVial";

    /// <summary>Upstream <c>HealVar(2m)</c>.</summary>
    public const int HealAmount = 2;

    public BloodVial()
        : base(CanonicalId, "Blood Vial", RelicRarity.Common) { }

    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(
            hooks,
            HookType.AfterPlayerTurnStartLate,
            ctx =>
            {
                ctx.Execution.Queue.Enqueue(new HealAction(HealAmount));
            }
        );
    }
}

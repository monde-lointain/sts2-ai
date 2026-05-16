using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Content.Cards.Effects;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Content.Powers;

namespace Sts2Headless.Domain.Content.Relics;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Relics.Vajra</c>: at
/// combat start, gain 1 Strength. Upstream subscribes
/// <c>AfterRoomEntered(CombatRoom)</c>; Q1 collapses that to
/// <see cref="HookType.BeforeCombatStart"/> for Phase-1 smoke content (CombatRoom is
/// the only room shape that fires combat hooks at S5 scope; the differentiator
/// matters only when S11 ships RestSite / Merchant rooms).
/// </summary>
public sealed class Vajra : RelicModel
{
    public const string CanonicalId = "Vajra";

    /// <summary>Strength granted at combat start — upstream <c>PowerVar&lt;StrengthPower&gt;(1m)</c>.</summary>
    public const int StrengthAtStart = 1;

    public Vajra()
        : base(CanonicalId, "Vajra", RelicRarity.Common) { }

    protected override void SubscribeHooks(HookRegistry hooks)
    {
        Subscribe(
            hooks,
            HookType.BeforeCombatStart,
            ctx =>
            {
                ctx.Execution.Queue.Enqueue(
                    new ApplyPowerAction(
                        PowerIds.Strength,
                        StrengthAtStart,
                        Target: null /* self */
                    )
                );
            }
        );
    }
}

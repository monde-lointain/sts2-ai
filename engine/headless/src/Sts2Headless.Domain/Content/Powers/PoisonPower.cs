using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Content.Powers;

/// <summary>
/// Verbatim port of upstream <c>MegaCrit.Sts2.Core.Models.Powers.PoisonPower</c>
/// (~/development/projects/godot/sts2/src/Core/Models/Powers/PoisonPower.cs):
/// counter-type debuff. Catalog metadata only; per-instance Poison stacks live on
/// the combat-side <c>PowerInstance</c>. The combat engine reads those stacks at
/// the owner's turn start to deal unblockable damage equal to the stack count, then
/// decrements the instance.
/// </summary>
public sealed class PoisonPower : PowerModel
{
    public PoisonPower()
        : base(PowerIds.Poison, PowerType.Debuff, PowerStackType.Counter) { }
}

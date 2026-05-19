using Sts2Headless.Domain.Actions;
using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Optional extension interface for <see cref="PowerModel"/> subclasses that need
/// access to the live <see cref="ICombatContext"/> at subscription time — specifically
/// for powers like <c>SurprisePower</c> whose AfterDeath hooks must call
/// <see cref="ICombatContext.AddEnemies"/> from within the handler.
///
/// <para>
/// <b>Why a separate interface (not a PowerModel overload):</b>
/// <c>PowerModel.cs</c> is shared infrastructure (wave-26/Q1.A); changing its
/// <see cref="PowerModel.OnApplied(uint, HookRegistry)"/> signature would be a
/// cross-stream breaking change. This interface provides an opt-in side-channel:
/// <see cref="Combat.CombatContext.ApplyPower"/> checks <c>model is
/// ICombatAwarePowerModel cam</c> after the standard <c>OnApplied</c> call and
/// dispatches to <see cref="OnAppliedWithContext"/> when the interface is present.
/// Existing powers that don't implement this interface are unaffected.
/// </para>
///
/// <para>
/// <b>Call order (wave-26/Q1.D bridge):</b>
/// <list type="number">
///   <item><c>model.OnApplied(creatureId, registry)</c> — base PowerModel lifecycle
///   (creates the handle slot, calls <c>SubscribeHooks</c> — a no-op for
///   ICombatAwarePowerModel implementors that move all subscription logic here).</item>
///   <item><c>cam.OnAppliedWithContext(creatureId, registry, combatCtx)</c> — this
///   method, called immediately after.</item>
/// </list>
/// Implementors that move all logic here should make their <c>SubscribeHooks</c>
/// override a no-op to avoid double-registration.
/// </para>
/// </summary>
public interface ICombatAwarePowerModel
{
    /// <summary>
    /// Called immediately after <see cref="PowerModel.OnApplied(uint, HookRegistry)"/>
    /// by <see cref="Combat.CombatContext.ApplyPower"/> when this interface is
    /// implemented. Provides the live <paramref name="combatCtx"/> so the implementor
    /// can capture it in handler closures.
    /// </summary>
    /// <param name="ownerCreatureId">Id of the creature this power was applied to.</param>
    /// <param name="registry">Active hook registry for this combat.</param>
    /// <param name="combatCtx">Live combat context (AddEnemies, ApplyPower, etc.).</param>
    void OnAppliedWithContext(
        uint ownerCreatureId,
        HookRegistry registry,
        ICombatContext combatCtx
    );
}

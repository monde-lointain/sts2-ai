// IAction — the queueable effect interface for M6d's action queue.
//
// Upstream analogue: MegaCrit.Sts2.Core.GameActions.GameAction (abstract base,
// async Task ExecuteAction()). We deliberately deviate:
//   1. Synchronous void Execute. Q1-ADR-008 forbids Task/async in the domain
//      decision path. Upstream's async is for Godot's frame yield + player-
//      choice pause; both of those move to M2 (control plane RPCs) in Q1.
//   2. No inheritance hierarchy. Concrete actions are sealed classes (or
//      structs) implementing IAction directly. Keeps cheap-clone trivial.
//   3. Execute is allowed to mutate ctx.Queue. This is how cascading effects
//      work in upstream (e.g., damage-action enqueues an on-take-damage hook
//      firing follow-up). See ActionQueue.Drain for the loop that picks up
//      those enqueued sub-actions.

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// A queueable effect on combat/run state. Implementations are typically small
/// sealed classes or structs so they cheap-clone for S17 counterfactual rollout.
/// </summary>
public interface IAction
{
    /// <summary>
    /// Apply this action. The implementation may:
    ///   - read/mutate state via <see cref="ExecutionContext"/>'s services,
    ///   - enqueue follow-up <see cref="IAction"/>s on <see cref="ExecutionContext.Queue"/>
    ///     (cascading effects),
    ///   - fire hooks on <see cref="ExecutionContext.Hooks"/>.
    /// MUST NOT throw on legal inputs; illegal inputs are filtered upstream by
    /// the legal-action enumerator (per action-queue.md).
    /// </summary>
    void Execute(ExecutionContext ctx);
}

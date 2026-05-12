using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Host;

/// <summary>
/// Source of <see cref="PlayerAction"/>s consumed by the host's main loop.
/// File-driven in Phase 1 (via <see cref="FileScriptedActionProvider"/>); S11
/// will introduce a control-plane provider that fans out RPC calls over the
/// M4 socket. The two implementations share this single-method port so the
/// main loop is provider-agnostic.
///
/// <para>
/// <b>Contract:</b> implementations must return a legal action with respect to
/// <paramref name="legal"/>. Returning <c>null</c> signals "no more input" —
/// the main loop translates that to exit code 2 when combat is still ongoing.
/// Throwing <see cref="InvalidOperationException"/> is reserved for genuine
/// programmer errors (e.g. file disappeared between reads).
/// </para>
/// </summary>
public interface IScriptedActionProvider
{
    /// <summary>
    /// Produce the next <see cref="PlayerAction"/> given the live state and
    /// the legal-actions set computed by <see cref="LegalActions.Enumerate"/>.
    /// Returns <c>null</c> to signal "script exhausted" (early termination).
    /// </summary>
    /// <param name="state">The current combat state.</param>
    /// <param name="legal">All legal actions for <paramref name="state"/>.</param>
    PlayerAction? NextAction(CombatState state, ImmutableArray<PlayerAction> legal);
}

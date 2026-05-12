// HookContext — payload passed into every HookHandler. Phase 1 carries just
// the ambient ExecutionContext; later stages expand it with per-hook payload
// (e.g., the card being played, the damage being applied) once those state
// types exist in S5/S6.
//
// Struct for cheap pass-by-value through the hot Fire loop. Single readonly
// field keeps copying free.

using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Actions;

/// <summary>
/// Per-hook-firing payload. Currently carries the <see cref="Execution"/>
/// context only; will gain per-hook fields as state types come online in S5/S6.
/// </summary>
public readonly struct HookContext
{
    public ExecutionContext Execution { get; }

    public HookContext(ExecutionContext execution)
    {
        Execution = execution;
    }
}

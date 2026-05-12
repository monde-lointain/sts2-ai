using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// Integration surface that <see cref="ReplayRecorder"/> exposes for the Host
/// (S8) to plug into MainLoop in a later orchestrator-driven integration.
/// Per the S10 stage prompt, this stage does not modify Host — the interface
/// is provided so a future stage can wire it without rewriting Host.
///
/// <para>
/// Method shapes match <see cref="ReplayRecorder"/>'s public surface. Any
/// concrete implementation must:
/// </para>
/// <list type="bullet">
///   <item>Return synchronously from <c>AppendStep</c> — no blocking IO on
///   the decision path.</item>
///   <item>Persist all queued records before <c>Close</c> returns.</item>
///   <item>Be safe to call <c>Close</c> multiple times (idempotent).</item>
/// </list>
///
/// <para>
/// A no-op stub implementation (for hosts that disable recording) can simply
/// drop all calls; tests construct <see cref="ReplayRecorder"/> directly.
/// </para>
/// </summary>
public interface IReplaySink : IDisposable
{
    /// <summary>
    /// Append one step (post-state + action). Returns synchronously after
    /// enqueueing onto the async flush channel.
    /// </summary>
    void AppendStep(
        CombatState postState,
        PlayerAction action,
        RunRngSet runRng,
        PlayerRngSet playerRng,
        TokenMap tokens);

    /// <summary>
    /// Flush pending entries, write the trailer, and close the underlying
    /// stream. Idempotent.
    /// </summary>
    void Close();
}

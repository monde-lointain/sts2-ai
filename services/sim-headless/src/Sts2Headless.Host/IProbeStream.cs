using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Host;

/// <summary>
/// Optional per-step canonical-hash sink for the S13 determinism probe.
///
/// <para>
/// When the host runs with <c>--probe-out &lt;path&gt;</c>, the
/// <see cref="MainLoop"/> emits one record at each turn boundary
/// (<c>combat_start</c>, <c>turn_start</c>, <c>card_played</c>,
/// <c>enemy_turn</c>, <c>combat_end</c>). Each record carries the canonical
/// SHA-256 hash of the full state blob at that moment, so the probe can
/// compare per-step against a golden trace.
/// </para>
///
/// <para>
/// <b>Determinism contract:</b> for a fixed (CLI args, scripted-action stream),
/// the emitted sequence of (step, event, hash) tuples must be byte-identical
/// across processes / platforms / runs. The hash function is
/// <see cref="Sts2Headless.Domain.Determinism.CanonicalHash"/> over the
/// <see cref="Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(CombatState, Sts2Headless.Domain.Determinism.RunRngSet, Sts2Headless.Domain.Determinism.PlayerRngSet, Sts2Headless.Domain.Content.TokenMap, Sts2Headless.Adapters.StateCodec.ManifestStamp)"/>
/// output — which is already a Q1-ADR-002 CI gate, so the per-step hash
/// inherits that determinism guarantee.
/// </para>
/// </summary>
public interface IProbeStream
{
    /// <summary>
    /// Emit one per-step record. <paramref name="eventName"/> labels the
    /// moment (e.g., <c>combat_start</c>); <paramref name="state"/> is the
    /// authoritative <see cref="CombatState"/> snapshot at that moment.
    /// Implementations must compute the canonical hash deterministically.
    /// </summary>
    void Emit(string eventName, CombatState state);

    /// <summary>Flush + close the sink. Safe to call multiple times.</summary>
    void Close();
}

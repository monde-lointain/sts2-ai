using System.Collections.Generic;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Content.Encounters;

/// <summary>
/// Wave-24/K.q1 port of upstream NibbitsNormal encounter: 2 Nibbits with
/// per-slot initial-move overrides.
/// Slot 0 (front) starts SLICE_MOVE; slot 1 (back) starts HISS_MOVE.
/// No encounter-RNG ticks — override list is fully deterministic.
/// </summary>
public sealed class NibbitsNormal : EncounterModel
{
    public const string CanonicalId = "NibbitsNormal";

    public NibbitsNormal()
        : base(CanonicalId, new[] { Nibbit.CanonicalId, Nibbit.CanonicalId }) { }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a fixed list; no Rng ticks.
    /// Slot 0 → SLICE_MOVE (front Nibbit); slot 1 → HISS_MOVE (back Nibbit).
    /// </remarks>
    public override IReadOnlyList<(
        string MonsterId,
        string? InitialMoveIdOverride
    )> GenerateMonstersWithMoves(Rng rng) =>
        new (string, string?)[]
        {
            (Nibbit.CanonicalId, Nibbit.SliceMoveId),
            (Nibbit.CanonicalId, Nibbit.HissMoveId),
        };
}

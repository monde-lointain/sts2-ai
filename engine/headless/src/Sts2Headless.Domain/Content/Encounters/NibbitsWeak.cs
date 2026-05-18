using System.Collections.Generic;
using Sts2Headless.Domain.Content.Monsters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Content.Encounters;

/// <summary>
/// Wave-24/K.q1 port of upstream NibbitsWeak encounter: 1 Nibbit, no override.
/// Nibbit starts its default initial move (BUTT_MOVE) per <c>Nibbit.InitialMoveId</c>.
/// No encounter-RNG ticks.
/// </summary>
public sealed class NibbitsWeak : EncounterModel
{
    public const string CanonicalId = "NibbitsWeak";

    public NibbitsWeak()
        : base(CanonicalId, new[] { Nibbit.CanonicalId }) { }
}

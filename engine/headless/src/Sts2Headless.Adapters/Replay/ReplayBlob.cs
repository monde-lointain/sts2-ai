using System.Collections.Immutable;
using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Domain.Combat;

namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// One step recorded in a replay file. Equivalent on-wire shape:
/// <code>
///   u32 turn_no
///   u8  phase           (CombatPhase cast)
///   u8  action_type     (ReplayActionType cast)
///   u32 action_size     (length of action_data in bytes)
///   bytes action_data
///   32 bytes post_hash  (S1 CanonicalHash of post-step CombatState section bytes)
/// </code>
/// </summary>
/// <param name="TurnNo">Turn counter from the post-step CombatState (never the
/// terminator sentinel <see cref="ReplayConstants.EntryTerminator"/>).</param>
/// <param name="Phase">Post-step CombatPhase.</param>
/// <param name="ActionType">Discriminator for action_data.</param>
/// <param name="ActionData">Opaque payload whose format depends on ActionType.</param>
/// <param name="PostHash">32-byte CanonicalHash digest of the post-step state.</param>
public sealed record ReplayEntry(
    uint TurnNo,
    CombatPhase Phase,
    ReplayActionType ActionType,
    byte[] ActionData,
    byte[] PostHash
)
{
    /// <summary>Element-wise equality across the two byte arrays.</summary>
    public bool Equals(ReplayEntry? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (TurnNo != other.TurnNo)
            return false;
        if (Phase != other.Phase)
            return false;
        if (ActionType != other.ActionType)
            return false;
        if (!ActionData.AsSpan().SequenceEqual(other.ActionData))
            return false;
        if (!PostHash.AsSpan().SequenceEqual(other.PostHash))
            return false;
        return true;
    }

    /// <summary>Required override to match <see cref="Equals(ReplayEntry?)"/>.</summary>
    public override int GetHashCode()
    {
        HashCode h = default;
        h.Add(TurnNo);
        h.Add(Phase);
        h.Add(ActionType);
        for (int i = 0; i < ActionData.Length; i++)
            h.Add(ActionData[i]);
        for (int i = 0; i < PostHash.Length; i++)
            h.Add(PostHash[i]);
        return h.ToHashCode();
    }
}

/// <summary>
/// Decoded view of a replay file. Produced by <see cref="ReplayReader.Open"/>;
/// holds the header fields, decoded entries in on-wire order, and a flag
/// indicating whether the trailer SHA-256 validated.
///
/// <para>
/// Reader throws <see cref="ReplayException"/> on a corrupt trailer before
/// producing a blob, so any returned blob has <c>TrailerValidated=true</c>;
/// the field is preserved for future when the reader might tolerate
/// informational-only trailers (e.g., during partial reads).
/// </para>
/// </summary>
/// <param name="SchemaVersion">Version read from header (reader rejects mismatch before producing a blob).</param>
/// <param name="ManifestStamp">Manifest stamp extracted from the header.</param>
/// <param name="InitialSeed">Initial seed recorded in the header.</param>
/// <param name="Entries">Recorded steps in on-wire order.</param>
/// <param name="TrailerValidated">True iff the trailer SHA-256 matched the body.</param>
public sealed record ReplayBlob(
    ushort SchemaVersion,
    ManifestStamp ManifestStamp,
    uint InitialSeed,
    ImmutableList<ReplayEntry> Entries,
    bool TrailerValidated
);

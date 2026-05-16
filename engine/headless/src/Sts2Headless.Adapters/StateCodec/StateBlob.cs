using System.Collections.Immutable;
using Sts2Headless.Domain.Combat;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Adapters.StateCodec;

/// <summary>
/// Wrapper describing a single section's payload + which kind it is. The
/// deserialize path produces one per section it consumed; callers project
/// these to concrete types via <see cref="StateBlob"/>.
/// </summary>
/// <param name="Id">Section identifier.</param>
/// <param name="Bytes">Raw section body (the bytes between <c>section_size</c> and the next entry).</param>
public sealed record StateSection(SectionId Id, byte[] Bytes);

/// <summary>
/// Decoded view of a state blob after deserialize. Holds the manifest stamp,
/// the parsed sections in their on-wire order, and a flag indicating whether
/// the trailer SHA-256 validated.
///
/// <para>
/// Callers can either:
/// <list type="bullet">
/// <item>Use <see cref="StateCodec.ToCombatState"/> for convenient extraction.</item>
/// <item>Reach into <see cref="Sections"/> directly for sections this codec doesn't
/// project (forward-compat for future stages adding RunState etc.).</item>
/// </list>
/// </para>
/// </summary>
/// <param name="SchemaVersion">Version read from header (codec rejects mismatch before producing a blob).</param>
/// <param name="Stamp">Manifest stamp extracted from the header.</param>
/// <param name="Sections">Sections in on-wire order (canonical Rng → Tokens → CombatState for v1).</param>
/// <param name="TrailerValidated">
/// True iff the trailer SHA-256 matched the body. Deserialize throws on mismatch
/// before returning, so any returned blob has TrailerValidated=true; the field
/// is preserved for future when codec might tolerate informational-only trailers.
/// </param>
public sealed record StateBlob(
    ushort SchemaVersion,
    ManifestStamp Stamp,
    ImmutableList<StateSection> Sections,
    bool TrailerValidated
)
{
    /// <summary>Convenience accessor for the Rng section bytes (or null if absent).</summary>
    public byte[]? RngBytes => FindSection(SectionId.Rng)?.Bytes;

    /// <summary>Convenience accessor for the Tokens section bytes (or null if absent).</summary>
    public byte[]? TokensBytes => FindSection(SectionId.Tokens)?.Bytes;

    /// <summary>Convenience accessor for the CombatState section bytes (or null if absent).</summary>
    public byte[]? CombatStateBytes => FindSection(SectionId.CombatState)?.Bytes;

    private StateSection? FindSection(SectionId id)
    {
        for (int i = 0; i < Sections.Count; i++)
        {
            if (Sections[i].Id == id)
                return Sections[i];
        }
        return null;
    }
}

namespace Sts2Headless.Adapters.Replay;

/// <summary>
/// Pinned wire-format constants for the M3 replay file. All values are part of
/// the schema contract; changing one bumps <see cref="SchemaVersion"/>.
///
/// <para>
/// <b>Layout (little-endian, all multi-byte integers):</b>
/// </para>
/// <code>
///   [HEADER]
///     magic           u32  = <see cref="HeaderMagic"/>      ("RPLY" little-endian)
///     schema          u16  = <see cref="SchemaVersion"/>
///     manifest_size   u32  = byte-count of the manifest_stamp body
///     manifest_stamp  bytes = serialized S7 ManifestStamp (u8 git_sha_len + utf8 git_sha
///                             + u16 build_id_len + utf8 build_id + 32 content_hash)
///     initial_seed    u32
///   [ENTRIES]
///     for each step:
///       turn_no       u32   (sentinel <see cref="EntryTerminator"/> marks end)
///       phase         u8    (CombatPhase cast)
///       action_type   u8    (ActionType cast)
///       action_size   u32   (length of action_data in bytes)
///       action_data   bytes
///       post_hash     32 bytes (S1 CanonicalHash of post-step CombatState section bytes)
///     terminator:
///       turn_no       u32 = <see cref="EntryTerminator"/>
///   [TRAILER]
///     trailer_magic   u32 = <see cref="TrailerMagic"/>   ("RPLT" little-endian)
///     sha256          32 bytes (SHA-256 of all preceding bytes)
/// </code>
///
/// <para>
/// <b>Endianness:</b> matches S1/S7 — little-endian throughout. Cross-platform
/// consumers see identical bytes regardless of host endianness.
/// </para>
/// </summary>
internal static class ReplayConstants
{
    /// <summary>"RPLY" little-endian (0x59='Y', 0x4C='L', 0x50='P', 0x52='R').</summary>
    public const uint HeaderMagic = 0x594C5052u;

    /// <summary>"RPLT" little-endian — trailer marker, paired with header magic.</summary>
    public const uint TrailerMagic = 0x544C5052u;

    /// <summary>Current replay schema version. Bumped when wire layout changes.</summary>
    public const ushort SchemaVersion = 1;

    /// <summary>Sentinel turn_no marking the end of the entries stream.</summary>
    public const uint EntryTerminator = 0xFFFFFFFFu;

    /// <summary>SHA-256 digest size in bytes; matches S1/S7.</summary>
    public const int Sha256ByteLength = 32;

    /// <summary>Trailer size on wire: u32 magic + 32-byte SHA-256.</summary>
    public const int TrailerSizeBytes = 4 + Sha256ByteLength;
}

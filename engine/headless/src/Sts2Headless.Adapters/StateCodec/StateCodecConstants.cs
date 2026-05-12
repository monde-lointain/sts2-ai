namespace Sts2Headless.Adapters.StateCodec;

/// <summary>
/// Pinned wire-format constants for the state codec. All values are part of
/// the schema contract; changing one bumps <see cref="SchemaVersion"/>.
///
/// <para>
/// <b>Layout (little-endian, all multi-byte integers):</b>
/// </para>
/// <code>
///   [HEADER]
///     magic        u32  = <see cref="HeaderMagic"/>      ("STCT" in ASCII when read LSB-first)
///     schema       u16  = <see cref="SchemaVersion"/>
///     header_size  u16  = byte-count of the stamp block below
///     stamp        bytes:
///       git_sha_len  u8
///       git_sha      utf8 bytes
///       build_id_len u16 (LE)
///       build_id     utf8 bytes
///       content_hash 32 bytes (SHA-256 of registered-content id set; see ManifestStamp)
///   [SECTIONS]
///     for each section in canonical order:
///       section_id     u16
///       section_size   u32
///       section_bytes  bytes
///     terminator:
///       section_id     u16 = <see cref="SectionTerminator"/>
///   [TRAILER]
///     trailer_magic    u32 = <see cref="TrailerMagic"/>
///     sha256           32 bytes (SHA-256 of all preceding bytes — header + sections + terminator)
/// </code>
/// </summary>
internal static class StateCodecConstants
{
    /// <summary>"STCT" little-endian — Sts2 sTate Codec.</summary>
    public const uint HeaderMagic = 0x53435443u;

    /// <summary>Trailer marker. "TCTS" / S-T-C-T reversed — paired with header magic for symmetry.</summary>
    public const uint TrailerMagic = 0x53544354u;

    /// <summary>Section table terminator. 0xFFFF reserves the full u16 range for terminator-only.</summary>
    public const ushort SectionTerminator = 0xFFFF;

    /// <summary>
    /// Current schema version. Bumped when wire layout changes.
    ///
    /// <para>
    /// <b>v3 (B.1-gamma-T5)</b> — appends two CombatState fields for the
    /// deferred X-cost and Shiv-tracking infrastructure:
    /// <list type="bullet">
    ///   <item><c>CombatState.LastSpentEnergy</c> appended — energy consumed
    ///         by the most recently played X-cost card (Skewer / Malaise).</item>
    ///   <item><c>CombatState.ExhaustedShivCount</c> appended — counter for
    ///         Shiv-tagged cards landing in exhaust (KnifeTrap). Q1's smoke
    ///         set doesn't spawn Shivs, so the counter stays at 0 today.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>v2 (B.1-alpha-T3, 2026-05-12)</b> — reflects Stream B's additive
    /// CombatState / MonsterIntent fields:
    /// <list type="bullet">
    ///   <item><c>MonsterIntent.MoveId</c> appended (Stream-B-T3, per-creature
    ///         move-state-machine cursor).</item>
    ///   <item><c>CombatState.AttacksPlayedThisTurn</c> appended (Stream-B-T4,
    ///         Finisher / Murder calc-damage formula evaluator).</item>
    ///   <item><c>CombatState.CardsDrawnThisCombat</c> appended (Stream-B-T4,
    ///         Murder's draws-this-combat multiplier).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>v1 (S7-original through Stream-A merge)</b> — initial M1 codec; no
    /// Stream-B fields; no per-creature MoveId on MonsterIntent.
    /// </para>
    /// </summary>
    public const ushort SchemaVersion = 3;

    /// <summary>Trailer size on wire: u32 magic + 32-byte SHA-256.</summary>
    public const int TrailerSizeBytes = 4 + 32;

    /// <summary>SHA-256 digest size in bytes.</summary>
    public const int Sha256ByteLength = 32;
}

/// <summary>
/// Section identifiers. Each variant pins a specific section's wire shape.
/// Adding a new section is additive — the deserializer treats unknown ids as
/// "unsupported" (forward-compat additive reads are not in scope for Phase 1
/// per spec). Existing variants must never change value.
/// </summary>
public enum SectionId : ushort
{
    /// <summary>RngBundle section — opaque M5 bytes inside a self-describing envelope.</summary>
    Rng = 0,

    /// <summary>TokenMap section — ordered (string, id) pairs from <c>TokenMap.Enumerate()</c>.</summary>
    Tokens = 1,

    /// <summary>CombatState body — flattened representation of CombatState's fields.</summary>
    CombatState = 2,

    // Future stages will add CombatAux (3), RunState (4), Hooks (5), etc.
    // Each new id is append-only.
}

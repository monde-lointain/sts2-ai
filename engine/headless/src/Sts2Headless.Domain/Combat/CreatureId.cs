using System.Globalization;
using System.Text.Json.Serialization;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Typed wrapper around the underlying <see cref="uint"/> creature-id value.
/// Replaces bare <see cref="uint"/> in the Domain combat surface so a card-instance
/// id, source id, target id, and creature id can no longer be confused at the
/// type system level.
///
/// <para>
/// <b>Type choice — record struct, no implicit ops:</b> auto-generated value
/// equality (CA1815 / CA2231 satisfied); same 4-byte size as <see cref="uint"/>;
/// no heap allocation. Explicit <see cref="Value"/> accessor when an actual
/// uint is needed (codec wire emission, deck-side comparisons). Implicit
/// conversions to/from <see cref="uint"/> are deliberately omitted — defeating
/// them is the entire point of this type.
/// </para>
///
/// <para>
/// <b>JSON contract:</b> serializes as a bare <see cref="uint"/> via
/// <see cref="CreatureIdJsonConverter"/>, not as <c>{"value": N}</c>. The
/// converter preserves the control-plane RPC wire shape and JSON-line log
/// shape that consumers expect.
/// </para>
///
/// <para>
/// <b>Wire contract (binary codec):</b> emitted as <see cref="uint"/> u32
/// little-endian by <see cref="Sts2Headless.Adapters.StateCodec.StateCodec"/>
/// and the replay codec. The in-memory type change does not affect bytes on
/// the wire; the codec calls <c>.Value</c> on write and wraps via
/// <c>new CreatureId(...)</c> on read.
/// </para>
///
/// <para>
/// <b>Invariant:</b> the player creature id is always <see cref="Player"/>
/// (<c>Value == 0</c>); the first enemy id is always <see cref="FirstEnemy"/>
/// (<c>Value == 1</c>); enemy ids increase monotonically per spawn.
/// </para>
/// </summary>
[JsonConverter(typeof(CreatureIdJsonConverter))]
public readonly record struct CreatureId(uint Value)
{
    /// <summary>Player creature id sentinel (always <c>Value == 0</c>).</summary>
    public static readonly CreatureId Player = new(0u);

    /// <summary>First enemy creature id (always <c>Value == 1</c>). Subsequent enemies are minted by <see cref="CreatureIdAllocator"/>.</summary>
    public static readonly CreatureId FirstEnemy = new(1u);

    /// <summary>Bare-uint string form (invariant culture). Used for log lines and any string-form interchange.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Parse a bare-uint string into a <see cref="CreatureId"/>. Returns false
    /// on any non-numeric or out-of-range input — never throws.
    /// </summary>
    public static bool TryParse(string? s, out CreatureId id)
    {
        if (uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v))
        {
            id = new CreatureId(v);
            return true;
        }
        id = default;
        return false;
    }
}

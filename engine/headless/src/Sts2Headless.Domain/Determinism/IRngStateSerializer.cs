namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Per Q1-ADR-003, M5 owns the RNG state schema and exposes a generic codec;
/// M1 (state codec) treats the produced byte blob as opaque, just attaching
/// it as one section of the larger state blob with the M5 schema version
/// stamped alongside the overall state schema version.
///
/// Contract:
///   * <c>Serialize(Deserialize(Serialize(x))) == Serialize(x)</c> byte-for-byte.
///   * The deserialized object, when resumed, produces a byte stream
///     identical to what the original would have produced from the same
///     point.
///   * The byte format is endian-stable and order-stable (no dependency on
///     Dictionary / HashSet enumeration order). Format choices are pinned in
///     <see cref="RngStateSerializerV1"/>'s class doc.
///
/// Implementations bump the M5 schema version (carried inside the byte blob)
/// when the format changes; M1 only sees the blob as <c>byte[]</c>.
/// </summary>
public interface IRngStateSerializer
{
    byte[] SerializeRng(Rng rng);
    Rng DeserializeRng(ReadOnlySpan<byte> bytes);

    byte[] SerializePlayerRngSet(PlayerRngSet set);
    PlayerRngSet DeserializePlayerRngSet(ReadOnlySpan<byte> bytes);

    byte[] SerializeRunRngSet(RunRngSet set);
    RunRngSet DeserializeRunRngSet(ReadOnlySpan<byte> bytes);
}

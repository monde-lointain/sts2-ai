using Sts2Headless.Adapters.StateCodec;
using Sts2Headless.Tests.Adapters.Fixtures.StateCodec;

namespace Sts2Headless.Tests.Adapters.StateCodec;

/// <summary>
/// THE HARD GATE — Q1-ADR-002's bit-identical-roundtrip contract.
///
/// <para>
/// For every fixture in <see cref="StateCodecFixtures.GenerateAll"/>:
/// assert <c>Serialize(Deserialize(Serialize(t))) == Serialize(t)</c>
/// byte-for-byte (via <see cref="ReadOnlySpan{T}.SequenceEqual"/>).
/// </para>
///
/// <para>
/// <b>Never disable.</b> These tests are the CI gate per pipeline
/// <c>scaling-strategy.md</c> §4.1 #4 — failure here blocks merge. Any
/// fixture must pass on every commit; new fixtures land alongside the
/// code that introduces a new shape, never as future work.
/// </para>
/// </summary>
public class BitIdenticalRoundtripTests
{
    public static IEnumerable<object[]> AllFixtures =>
        StateCodecFixtures.GenerateAll().Select(f => new object[] { f.Name, f });

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Serialize_Deserialize_Serialize_is_byte_identical(string name, StateCodecFixture f)
    {
        byte[] first = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            f.State, f.RunRng, f.PlayerRng, f.Tokens, f.Stamp);

        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(first);
        Assert.True(decoded.TrailerValidated, $"[{name}] trailer must validate");
        Assert.Equal(StateCodecConstants.SchemaVersion, decoded.SchemaVersion);

        // Reconstruct each section back to its source type.
        var rebuiltState = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(decoded);
        var (rebuiltRunRng, rebuiltPlayerRng) =
            global::Sts2Headless.Adapters.StateCodec.StateCodec.ToRngBundle(decoded);
        var rebuiltTokens = global::Sts2Headless.Adapters.StateCodec.StateCodec.ToTokenMap(decoded);

        // Re-serialize: must produce identical bytes.
        byte[] second = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            rebuiltState, rebuiltRunRng, rebuiltPlayerRng, rebuiltTokens, decoded.Stamp);

        Assert.True(
            first.AsSpan().SequenceEqual(second),
            $"[{name}] bit-identical roundtrip failed: first={first.Length}B second={second.Length}B");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void CombatState_record_equals_after_roundtrip(string name, StateCodecFixture f)
    {
        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            f.State, f.RunRng, f.PlayerRng, f.Tokens, f.Stamp);
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        global::Sts2Headless.Domain.Combat.CombatState recovered =
            global::Sts2Headless.Adapters.StateCodec.StateCodec.ToCombatState(decoded);
        Assert.True(f.State.Equals(recovered),
            $"[{name}] CombatState.Equals broke after roundtrip");
    }

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void Manifest_stamp_survives_roundtrip(string name, StateCodecFixture f)
    {
        byte[] blob = global::Sts2Headless.Adapters.StateCodec.StateCodec.Serialize(
            f.State, f.RunRng, f.PlayerRng, f.Tokens, f.Stamp);
        StateBlob decoded = global::Sts2Headless.Adapters.StateCodec.StateCodec.Deserialize(blob);
        Assert.True(
            f.Stamp.Equals(decoded.Stamp),
            $"[{name}] manifest stamp roundtrip broke");
    }

    [Fact]
    public void Corpus_has_at_least_twenty_fixtures()
    {
        // Pin the corpus floor — the spec requires ~20.
        List<StateCodecFixture> all = StateCodecFixtures.GenerateAll();
        Assert.True(all.Count >= 20,
            $"Fixture corpus has {all.Count} fixtures; spec requires >=20.");
    }

    [Fact]
    public void Fixture_names_are_unique()
    {
        List<StateCodecFixture> all = StateCodecFixtures.GenerateAll();
        Assert.Equal(all.Count, all.Select(f => f.Name).Distinct().Count());
    }
}

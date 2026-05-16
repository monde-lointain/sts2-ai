// CanonicalHash is the localization tool for the S13 determinism probe and
// the state-codec equivalence check. It MUST be reproducible:
//   - across multiple CLR invocations on the same machine
//   - across .NET 9 platform/arch combinations (the SHA-256 reference impl
//     is deterministic; we don't depend on hardware acceleration semantics)
//   - across multiple invocations within the same process
//
// We pin known SHA-256 outputs for fixed inputs so the algorithm choice is
// observed in the test suite, not just behaviorally implied.

using System.Text;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Tests.Domain.Determinism;

public class CanonicalHashTests
{
    [Fact]
    public void EmptyInputProducesKnownSha256()
    {
        // SHA-256("") = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        string hex = CanonicalHash.Sha256Hex(ReadOnlySpan<byte>.Empty);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hex);
    }

    [Fact]
    public void AbcInputProducesKnownSha256()
    {
        // SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
        string hex = CanonicalHash.Sha256Hex(Encoding.UTF8.GetBytes("abc"));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hex);
    }

    [Fact]
    public void OutputIsLowercaseHexAlways()
    {
        string hex = CanonicalHash.Sha256Hex(new byte[] { 0xFF, 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.Equal(64, hex.Length);
        foreach (char c in hex)
        {
            Assert.True(
                c is (>= '0' and <= '9') or (>= 'a' and <= 'f'),
                $"non-lowercase-hex char in output: '{c}'"
            );
        }
    }

    [Fact]
    public void SameInputProducesSameOutputAcrossInvocations()
    {
        byte[] input = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        string a = CanonicalHash.Sha256Hex(input);
        string b = CanonicalHash.Sha256Hex(input);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentInputsProduceDifferentOutputs()
    {
        string a = CanonicalHash.Sha256Hex(new byte[] { 0 });
        string b = CanonicalHash.Sha256Hex(new byte[] { 1 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void OneBitChangeChangesOutputCompletely()
    {
        byte[] input = new byte[64];
        string a = CanonicalHash.Sha256Hex(input);
        input[0] = 1;
        string b = CanonicalHash.Sha256Hex(input);
        // Avalanche check (loose): on average ~50% of hex chars should differ
        // after a one-bit input flip. We don't pin a count; just assert the
        // outputs differ.
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AcceptsLargeInput()
    {
        byte[] input = new byte[1 << 16];
        for (int i = 0; i < input.Length; i++)
            input[i] = (byte)i;
        string hex = CanonicalHash.Sha256Hex(input);
        Assert.Equal(64, hex.Length);
    }
}

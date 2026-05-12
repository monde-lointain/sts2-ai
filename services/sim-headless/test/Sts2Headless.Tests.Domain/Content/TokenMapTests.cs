using Sts2Headless.Domain.Content;

namespace Sts2Headless.Tests.Domain.Content;

/// <summary>
/// TokenMap is the bidirectional string ↔ int table M1's State Codec (S7) will use to
/// shrink id strings into compact ints in serialized state. These tests pin the
/// determinism contract: same insertion sequence → same id assignment, byte-identical.
/// </summary>
public class TokenMapTests
{
    [Fact]
    public void GetOrAddId_assigns_zero_to_first_token()
    {
        TokenMap map = new();
        Assert.Equal(0, map.GetOrAddId("strike"));
    }

    [Fact]
    public void GetOrAddId_assigns_sequential_ids_in_insertion_order()
    {
        TokenMap map = new();
        Assert.Equal(0, map.GetOrAddId("strike"));
        Assert.Equal(1, map.GetOrAddId("defend"));
        Assert.Equal(2, map.GetOrAddId("bash"));
    }

    [Fact]
    public void GetOrAddId_returns_existing_id_for_known_token()
    {
        TokenMap map = new();
        int first = map.GetOrAddId("strike");
        int second = map.GetOrAddId("strike");
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetString_returns_token_for_known_id()
    {
        TokenMap map = new();
        int id = map.GetOrAddId("strike");
        Assert.Equal("strike", map.GetString(id));
    }

    [Fact]
    public void GetString_throws_for_unknown_id()
    {
        TokenMap map = new();
        Assert.Throws<KeyNotFoundException>(() => map.GetString(99));
    }

    [Fact]
    public void TryGetId_returns_true_and_id_for_known_token()
    {
        TokenMap map = new();
        map.GetOrAddId("strike");

        bool found = map.TryGetId("strike", out int id);
        Assert.True(found);
        Assert.Equal(0, id);
    }

    [Fact]
    public void TryGetId_returns_false_for_unknown_token()
    {
        TokenMap map = new();
        bool found = map.TryGetId("unknown", out int id);
        Assert.False(found);
        Assert.Equal(0, id); // out-default; only the bool is meaningful
    }

    [Fact]
    public void Count_reflects_unique_token_count()
    {
        TokenMap map = new();
        Assert.Equal(0, map.Count);
        map.GetOrAddId("strike");
        Assert.Equal(1, map.Count);
        map.GetOrAddId("defend");
        Assert.Equal(2, map.Count);
        map.GetOrAddId("strike"); // already present — no growth
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void Contains_reflects_token_presence()
    {
        TokenMap map = new();
        Assert.False(map.Contains("strike"));
        map.GetOrAddId("strike");
        Assert.True(map.Contains("strike"));
    }

    [Fact]
    public void Roundtrip_string_to_int_to_string_is_stable()
    {
        TokenMap map = new();
        string[] tokens = { "strike", "defend", "bash", "ring_of_the_snake" };
        foreach (string t in tokens)
        {
            map.GetOrAddId(t);
        }

        foreach (string t in tokens)
        {
            int id = map.GetOrAddId(t);
            Assert.Equal(t, map.GetString(id));
        }
    }

    [Fact]
    public void Two_token_maps_with_same_insertion_sequence_produce_same_ids()
    {
        // This is *the* M1 (S7) determinism property: rebuilding a TokenMap from the same
        // sequence of GetOrAddId calls must produce byte-identical id assignments. If this
        // ever fails the M1 state codec would emit different bytes on different runs.
        TokenMap a = new();
        TokenMap b = new();
        string[] sequence = { "alpha", "beta", "gamma", "delta", "epsilon", "alpha", "beta" };

        int[] aIds = sequence.Select(a.GetOrAddId).ToArray();
        int[] bIds = sequence.Select(b.GetOrAddId).ToArray();

        Assert.Equal(aIds, bIds);
    }

    [Fact]
    public void Enumerate_yields_tokens_in_insertion_order_with_matching_ids()
    {
        TokenMap map = new();
        map.GetOrAddId("zeta");
        map.GetOrAddId("alpha");
        map.GetOrAddId("mu");

        (string Token, int Id)[] entries = map.Enumerate().ToArray();
        Assert.Equal(
            new[] { ("zeta", 0), ("alpha", 1), ("mu", 2) },
            entries);
    }

    [Fact]
    public void Enumerate_does_not_include_re_added_tokens_twice()
    {
        TokenMap map = new();
        map.GetOrAddId("strike");
        map.GetOrAddId("defend");
        map.GetOrAddId("strike"); // re-add — must not create a duplicate enumeration entry

        (string Token, int Id)[] entries = map.Enumerate().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(("strike", 0), entries[0]);
        Assert.Equal(("defend", 1), entries[1]);
    }

    [Fact]
    public void GetOrAddId_rejects_null_token()
    {
        TokenMap map = new();
        Assert.Throws<ArgumentNullException>(() => map.GetOrAddId(null!));
    }
}

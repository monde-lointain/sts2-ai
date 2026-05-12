using Sts2Headless.Domain.Content;

namespace Sts2Headless.Tests.Domain.Content;

/// <summary>
/// Spec: <c>docs/specs/modules/content-catalog.md</c>. ContentTable is M7's generic
/// registry — <c>Dictionary&lt;TId, T&gt;</c> for lookup, with iteration order pinned to
/// insertion (declaration) order. This file pins those guarantees because S7 (M1 State
/// Codec) will roundtrip catalog contents and a non-deterministic enumeration order would
/// break bit-identical roundtrip.
/// </summary>
public class ContentTableTests
{
    private sealed record Model(string Id, string Name);

    private static ContentTable<string, Model> NewTable() => new();

    [Fact]
    public void Register_then_Get_returns_the_registered_model()
    {
        ContentTable<string, Model> table = NewTable();
        Model strike = new("strike", "Strike");

        table.Register(strike.Id, strike);

        Assert.Same(strike, table.Get("strike"));
    }

    [Fact]
    public void TryGet_returns_true_and_model_for_known_id()
    {
        ContentTable<string, Model> table = NewTable();
        Model defend = new("defend", "Defend");
        table.Register(defend.Id, defend);

        bool found = table.TryGet("defend", out Model? result);

        Assert.True(found);
        Assert.Same(defend, result);
    }

    [Fact]
    public void TryGet_returns_false_and_null_for_unknown_id()
    {
        ContentTable<string, Model> table = NewTable();

        bool found = table.TryGet("missing", out Model? result);

        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void Get_throws_for_unknown_id_with_explicit_id_in_message()
    {
        ContentTable<string, Model> table = NewTable();

        KeyNotFoundException ex = Assert.Throws<KeyNotFoundException>(() => table.Get("nope"));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void Contains_reflects_registration()
    {
        ContentTable<string, Model> table = NewTable();
        Assert.False(table.Contains("x"));
        table.Register("x", new Model("x", "X"));
        Assert.True(table.Contains("x"));
    }

    [Fact]
    public void Register_duplicate_id_throws()
    {
        ContentTable<string, Model> table = NewTable();
        table.Register("strike", new Model("strike", "Strike"));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => table.Register("strike", new Model("strike", "Strike (duplicate)")));
        Assert.Contains("strike", ex.Message);
    }

    [Fact]
    public void Register_null_id_throws()
    {
        ContentTable<string, Model> table = NewTable();
        Assert.Throws<ArgumentNullException>(
            () => table.Register(null!, new Model("x", "X")));
    }

    [Fact]
    public void Register_null_model_throws()
    {
        ContentTable<string, Model> table = NewTable();
        Assert.Throws<ArgumentNullException>(
            () => table.Register("x", null!));
    }

    [Fact]
    public void Count_reflects_registrations()
    {
        ContentTable<string, Model> table = NewTable();
        Assert.Equal(0, table.Count);

        table.Register("a", new Model("a", "A"));
        Assert.Equal(1, table.Count);

        table.Register("b", new Model("b", "B"));
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void Enumerate_yields_insertion_order_independent_of_id_sort_order()
    {
        // R2 mitigation: if enumeration were Dictionary-derived, output could be hash-order.
        // Register ids in a sequence that is *not* sorted, then assert insertion order is
        // preserved. This deliberately picks ids whose natural string-sort order differs
        // from the registration order so the test catches accidental SortedDictionary use.
        ContentTable<string, Model> table = NewTable();
        Model[] inOrder = new[]
        {
            new Model("zeta", "Z"),
            new Model("alpha", "A"),
            new Model("mu", "M"),
            new Model("beta", "B"),
        };
        foreach (Model m in inOrder)
        {
            table.Register(m.Id, m);
        }

        string[] enumerated = table.Enumerate().Select(m => m.Id).ToArray();
        Assert.Equal(new[] { "zeta", "alpha", "mu", "beta" }, enumerated);
    }

    [Fact]
    public void EnumerateIds_yields_insertion_order_matching_Enumerate()
    {
        ContentTable<string, Model> table = NewTable();
        table.Register("c", new Model("c", "C"));
        table.Register("a", new Model("a", "A"));
        table.Register("b", new Model("b", "B"));

        Assert.Equal(new[] { "c", "a", "b" }, table.EnumerateIds().ToArray());
        Assert.Equal(
            table.Enumerate().Select(m => m.Id).ToArray(),
            table.EnumerateIds().ToArray());
    }

    [Fact]
    public void Enumerate_stable_across_repeated_iterations()
    {
        ContentTable<string, Model> table = NewTable();
        for (int i = 0; i < 16; i++)
        {
            table.Register($"id_{i}", new Model($"id_{i}", $"M{i}"));
        }

        string[] pass1 = table.Enumerate().Select(m => m.Id).ToArray();
        string[] pass2 = table.Enumerate().Select(m => m.Id).ToArray();
        string[] pass3 = table.Enumerate().Select(m => m.Id).ToArray();
        Assert.Equal(pass1, pass2);
        Assert.Equal(pass1, pass3);
    }

    [Fact]
    public void Two_tables_with_same_insertion_sequence_enumerate_identically()
    {
        // Preview of M1 (S7) determinism: two fresh process states fed the same sequence
        // produce byte-identical enumeration. This is *the* property M1 will rely on.
        ContentTable<string, Model> a = NewTable();
        ContentTable<string, Model> b = NewTable();
        string[] ids = new[] { "kappa", "delta", "lambda", "alpha", "omega" };
        foreach (string id in ids)
        {
            a.Register(id, new Model(id, id.ToUpperInvariant()));
            b.Register(id, new Model(id, id.ToUpperInvariant()));
        }

        Assert.Equal(
            a.Enumerate().Select(m => m.Id).ToArray(),
            b.Enumerate().Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Works_with_non_string_id_types()
    {
        ContentTable<int, Model> table = new();
        table.Register(42, new Model("forty-two", "42"));
        table.Register(7, new Model("seven", "7"));

        Assert.True(table.Contains(42));
        Assert.Equal(new[] { 42, 7 }, table.EnumerateIds().ToArray());
    }
}

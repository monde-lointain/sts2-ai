namespace Sts2Headless.Domain.Content;

/// <summary>
/// Generic in-process content registry — the M7 base type. Stores <typeparamref name="TModel"/>
/// instances keyed by <typeparamref name="TId"/>, with O(1) lookup and a separately
/// maintained insertion-order index for enumeration.
///
/// <para>
/// <b>Determinism contract (R2 mitigation, see Q1 plan risk register):</b>
/// enumeration order is <b>insertion order</b>, never hash order. The internal lookup
/// uses <see cref="Dictionary{TKey,TValue}"/> for speed, but the source of enumeration
/// truth is a parallel <see cref="List{T}"/> of ids. S7's M1 State Codec will rely on
/// this — replacing the list with a dictionary-derived iteration would silently break
/// bit-identical state roundtrip.
/// </para>
///
/// <para>
/// <b>Lookup-miss contract:</b> <see cref="Get(TId)"/> throws
/// <see cref="KeyNotFoundException"/>; <see cref="TryGet(TId, out TModel?)"/> returns
/// <see langword="false"/>. Both forms are kept: combat code that *knows* an id should
/// exist (e.g., reward selection from a populated pool) wants the loud failure;
/// validation and coverage gates want the quiet probe.
/// </para>
///
/// <para>
/// <b>Duplicate-Register contract:</b> registering the same id twice throws
/// <see cref="InvalidOperationException"/>. Catalogs are built once at process init and
/// frozen-by-convention afterwards (the module spec calls for hard freeze; that's a
/// later refinement once we have an init phase to hook).
/// </para>
/// </summary>
/// <typeparam name="TId">Id type. Must be non-nullable and value-equality comparable.</typeparam>
/// <typeparam name="TModel">Model type. Reference type so we can sanity-check null.</typeparam>
public class ContentTable<TId, TModel>
    where TId : notnull
    where TModel : class
{
    private readonly Dictionary<TId, TModel> _byId = new();
    private readonly List<TId> _insertionOrder = new();

    /// <summary>Number of registered entries.</summary>
    public int Count => _insertionOrder.Count;

    /// <summary>
    /// Register <paramref name="model"/> under <paramref name="id"/>.
    /// Throws <see cref="InvalidOperationException"/> if <paramref name="id"/> is
    /// already registered; throws <see cref="ArgumentNullException"/> if either
    /// argument is null.
    /// </summary>
    public void Register(TId id, TModel model)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(model);

        if (_byId.ContainsKey(id))
        {
            throw new InvalidOperationException(
                $"Duplicate content registration: id '{id}' is already registered.");
        }

        _byId.Add(id, model);
        _insertionOrder.Add(id);
    }

    /// <summary>
    /// Look up by id. Throws <see cref="KeyNotFoundException"/> on miss. Use this when
    /// the caller knows the id must exist (combat lookups, reward draws).
    /// </summary>
    public TModel Get(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (!_byId.TryGetValue(id, out TModel? model))
        {
            throw new KeyNotFoundException($"No content registered for id '{id}'.");
        }
        return model;
    }

    /// <summary>
    /// Try-look up by id. Returns <see langword="false"/> on miss without throwing.
    /// Use this for validation and coverage probes.
    /// </summary>
    public bool TryGet(TId id, out TModel? model)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.TryGetValue(id, out model);
    }

    /// <summary>True if <paramref name="id"/> is registered.</summary>
    public bool Contains(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.ContainsKey(id);
    }

    /// <summary>
    /// Enumerate all registered models in insertion order. Stable across repeated calls
    /// and across processes given the same registration sequence.
    /// </summary>
    public IEnumerable<TModel> Enumerate()
    {
        foreach (TId id in _insertionOrder)
        {
            yield return _byId[id];
        }
    }

    /// <summary>
    /// Enumerate all registered ids in insertion order. Equivalent to
    /// <c>Enumerate().Select(getId)</c> but doesn't require a model→id projection.
    /// </summary>
    public IEnumerable<TId> EnumerateIds()
    {
        foreach (TId id in _insertionOrder)
        {
            yield return id;
        }
    }
}

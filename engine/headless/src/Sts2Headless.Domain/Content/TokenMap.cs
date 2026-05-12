namespace Sts2Headless.Domain.Content;

/// <summary>
/// Bidirectional string ↔ int table used by M1's State Codec (S7) to compactly serialize
/// id-shaped strings (card ids, relic ids, etc.) as ints. Module spec
/// (<c>docs/specs/modules/content-catalog.md</c>) calls this <c>TokenMap</c>.
///
/// <para>
/// <b>Id-assignment contract:</b> ids are assigned monotonically starting at 0 in the
/// order tokens are first seen via <see cref="GetOrAddId"/>. A token presented twice
/// returns its previously assigned id — no duplicate growth.
/// </para>
///
/// <para>
/// <b>Determinism contract (R2 mitigation):</b> two <see cref="TokenMap"/> instances fed
/// the same sequence of <see cref="GetOrAddId"/> calls produce byte-identical id
/// assignments. The token-by-id lookup is backed by a parallel
/// <see cref="List{T}"/> of tokens (indexed by id) rather than a
/// <see cref="Dictionary{TKey,TValue}"/>, so enumeration order is always insertion order.
/// </para>
///
/// <para>
/// <b>Unknown-token behavior:</b> <see cref="GetOrAddId"/> auto-assigns the next int id
/// for a previously-unseen token. This is intentional — the M1 state codec emits writes
/// before reads, and forcing pre-registration would require a separate seed pass. For
/// the read direction, <see cref="GetString(int)"/> throws on unknown ids — a state blob
/// that references an id not in the codec's token map is a real error, not a recoverable
/// miss.
/// </para>
/// </summary>
public class TokenMap
{
    private readonly Dictionary<string, int> _idByToken = new(StringComparer.Ordinal);
    private readonly List<string> _tokenById = new();

    /// <summary>Number of unique tokens currently mapped.</summary>
    public int Count => _tokenById.Count;

    /// <summary>
    /// Return the int id assigned to <paramref name="token"/>, assigning the next
    /// sequential id (starting at 0) if this is the first time the token is seen.
    /// </summary>
    public int GetOrAddId(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (_idByToken.TryGetValue(token, out int existing))
        {
            return existing;
        }
        int newId = _tokenById.Count;
        _idByToken.Add(token, newId);
        _tokenById.Add(token);
        return newId;
    }

    /// <summary>
    /// Look up the token string for an id. Throws <see cref="KeyNotFoundException"/>
    /// if the id was never assigned. Use for the deserialize direction of M1.
    /// </summary>
    public string GetString(int id)
    {
        if ((uint)id >= (uint)_tokenById.Count)
        {
            throw new KeyNotFoundException(
                $"No token registered for id {id} (token map has {_tokenById.Count} entries).");
        }
        return _tokenById[id];
    }

    /// <summary>
    /// Try-lookup of the int id for a token. Does not auto-assign on miss — for the
    /// "is this token present?" probe without growing the map. Returns
    /// <see langword="false"/> and <c>id = 0</c> if the token is unknown.
    /// </summary>
    public bool TryGetId(string token, out int id)
    {
        ArgumentNullException.ThrowIfNull(token);
        return _idByToken.TryGetValue(token, out id);
    }

    /// <summary>True if <paramref name="token"/> has been assigned an id.</summary>
    public bool Contains(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return _idByToken.ContainsKey(token);
    }

    /// <summary>
    /// Enumerate all <c>(token, id)</c> pairs in insertion order. Stable across processes
    /// for the same insertion sequence — see class-level remarks on determinism.
    /// </summary>
    public IEnumerable<(string Token, int Id)> Enumerate()
    {
        for (int i = 0; i < _tokenById.Count; i++)
        {
            yield return (_tokenById[i], i);
        }
    }
}

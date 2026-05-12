namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Fan-out of player-scope RNGs — direct port of upstream
/// <c>MegaCrit.Sts2.Core.Random.PlayerRngSet</c>. One independent
/// <see cref="Rng"/> instance per <see cref="PlayerRngType"/>, each seeded
/// from the master <see cref="Seed"/> + the deterministic hash of the
/// snake_case subsystem name (the upstream contract; see
/// <see cref="StringHelpers.GetDeterministicHashCode(string)"/>).
///
/// Notes vs upstream:
///   * upstream's <c>ToSerializable</c> / <c>FromSerializable</c> /
///     <c>LoadFromSerializable</c> entry points use the multiplayer
///     <c>PacketWriter</c>/<c>PacketReader</c> types that aren't ported here;
///     persistence is handled by <see cref="IRngStateSerializer"/> instead
///     (M5 -> M1 boundary per Q1-ADR-003).
///   * upstream returns <c>SerializablePlayerRngSet</c> with a counter
///     dictionary; we expose the equivalent via
///     <see cref="GetCounter(PlayerRngType)"/> for the serializer's use.
/// </summary>
public sealed class PlayerRngSet
{
    private readonly Dictionary<PlayerRngType, Rng> _rngs = new();

    public uint Seed { get; }

    public Rng Rewards => _rngs[PlayerRngType.Rewards];
    public Rng Shops => _rngs[PlayerRngType.Shops];
    public Rng Transformations => _rngs[PlayerRngType.Transformations];

    public PlayerRngSet(uint seed)
    {
        Seed = seed;
        foreach (PlayerRngType t in Enum.GetValues<PlayerRngType>())
        {
            _rngs[t] = CreateRng(t);
        }
    }

    /// <summary>
    /// Reconstructs a set from a master seed plus a per-subsystem target
    /// counter (the upstream save format's payload). Each subsystem RNG is
    /// freshly constructed and then fast-forwarded to its target counter, so
    /// resumed streams are byte-equal to the pre-save stream. Used by
    /// <see cref="IRngStateSerializer"/>.
    /// </summary>
    public static PlayerRngSet Restore(uint seed, IReadOnlyDictionary<PlayerRngType, int> counters)
    {
        var set = new PlayerRngSet(seed);
        foreach (PlayerRngType t in Enum.GetValues<PlayerRngType>())
        {
            if (counters.TryGetValue(t, out int target))
            {
                set._rngs[t].FastForwardCounter(target);
            }
        }
        return set;
    }

    /// <summary>Indexer access — equivalent to upstream's private GetRng.</summary>
    public Rng this[PlayerRngType type] => _rngs[type];

    /// <summary>Current counter for the given subsystem; used by serializer.</summary>
    public int GetCounter(PlayerRngType type) => _rngs[type].Counter;

    private Rng CreateRng(PlayerRngType type)
    {
        string name = StringHelpers.SnakeCase(type.ToString());
        return new Rng(Seed, name);
    }
}

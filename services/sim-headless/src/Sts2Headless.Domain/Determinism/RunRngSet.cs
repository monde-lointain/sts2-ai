namespace Sts2Headless.Domain.Determinism;

/// <summary>
/// Fan-out of run-scope RNGs — direct port of upstream
/// <c>MegaCrit.Sts2.Core.Runs.RunRngSet</c>. Master seed is a free-form
/// <see cref="StringSeed"/> (e.g. the run-id daily-seed string) that is hashed
/// once via <see cref="StringHelpers.GetDeterministicHashCode(string)"/> into
/// the uint <see cref="Seed"/> that each subsystem RNG then derives from.
///
/// Notes vs upstream:
///   * upstream's <c>ToSerializable</c> / <c>FromSave</c> /
///     <c>LoadFromSerializable</c> use the multiplayer packet codec types
///     that aren't ported; persistence is handled by
///     <see cref="IRngStateSerializer"/>.
///   * upstream's static <c>_mockInstance</c> + <c>GetMockInstance</c> +
///     <c>TestMode</c> hooks are intentionally not ported — Q1 tests stub
///     RNG via the <see cref="IRngSource"/> port, not a global singleton.
///   * the upstream enum value <c>CombatOrbs</c> is surfaced as
///     <see cref="CombatOrbGeneration"/> on this class to mirror the
///     upstream property name; the enum value itself keeps its identifier so
///     the hashed snake_case derivation is unchanged.
/// </summary>
public sealed class RunRngSet
{
    private readonly Dictionary<RunRngType, Rng> _rngs = new();

    public string StringSeed { get; }
    public uint Seed { get; }

    public Rng UpFront => _rngs[RunRngType.UpFront];
    public Rng Shuffle => _rngs[RunRngType.Shuffle];
    public Rng UnknownMapPoint => _rngs[RunRngType.UnknownMapPoint];
    public Rng CombatCardGeneration => _rngs[RunRngType.CombatCardGeneration];
    public Rng CombatPotionGeneration => _rngs[RunRngType.CombatPotionGeneration];
    public Rng CombatCardSelection => _rngs[RunRngType.CombatCardSelection];
    public Rng CombatEnergyCosts => _rngs[RunRngType.CombatEnergyCosts];
    public Rng CombatTargets => _rngs[RunRngType.CombatTargets];
    public Rng MonsterAi => _rngs[RunRngType.MonsterAi];
    public Rng Niche => _rngs[RunRngType.Niche];
    public Rng CombatOrbGeneration => _rngs[RunRngType.CombatOrbs];
    public Rng TreasureRoomRelics => _rngs[RunRngType.TreasureRoomRelics];

    public RunRngSet(string stringSeed)
    {
        StringSeed = stringSeed;
        Seed = (uint)StringHelpers.GetDeterministicHashCode(stringSeed);
        foreach (RunRngType t in Enum.GetValues<RunRngType>())
        {
            _rngs[t] = CreateRng(t);
        }
    }

    /// <summary>
    /// Reconstructs a set from a string seed + per-subsystem target counters.
    /// Each subsystem RNG is freshly constructed and fast-forwarded so the
    /// resumed stream is byte-equal to the pre-save stream.
    /// </summary>
    public static RunRngSet Restore(string stringSeed, IReadOnlyDictionary<RunRngType, int> counters)
    {
        var set = new RunRngSet(stringSeed);
        foreach (RunRngType t in Enum.GetValues<RunRngType>())
        {
            if (counters.TryGetValue(t, out int target))
            {
                set._rngs[t].FastForwardCounter(target);
            }
        }
        return set;
    }

    public Rng this[RunRngType type] => _rngs[type];

    public int GetCounter(RunRngType type) => _rngs[type].Counter;

    private Rng CreateRng(RunRngType type)
    {
        string name = StringHelpers.SnakeCase(type.ToString());
        return new Rng(Seed, name);
    }
}

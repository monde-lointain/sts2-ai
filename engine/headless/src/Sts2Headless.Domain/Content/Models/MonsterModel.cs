using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Content.Models;

/// <summary>Power stamped on a monster at spawn time. Matches upstream's
/// <c>AfterAddedToRoom</c> <c>PowerCmd.Apply&lt;TPower&gt;</c> calls.</summary>
public sealed record MonsterSpawnPower(string PowerId, int Stacks);

/// <summary>
/// Abstract base for all monster content. Q1-headless analogue of upstream
/// <c>MegaCrit.Sts2.Core.Models.MonsterModel</c>
/// (~/development/projects/godot/sts2/src/Core/Models/MonsterModel.cs:28).
///
/// <para>
/// <b>Intent rotation:</b> a finite move-state machine equivalent to upstream's
/// <c>MonsterMoveStateMachine</c>. Each move has a <see cref="MonsterMove.FollowUpMoveId"/>
/// (and optionally an <see cref="MonsterMove.BranchResolver"/> for HP / RNG / power
/// branches). The model is the read-only catalog of those transitions;
/// per-creature rotation state lives on
/// <see cref="Sts2Headless.Domain.Combat.MonsterIntent.MoveId"/>. The engine
/// advances it via <see cref="AdvanceMoveId"/>, the immutable resolver.
/// </para>
///
/// <para>
/// <b>HP:</b> upstream gives min/max init HP and rolls a value via the run RNG. The
/// base exposes that contract; concrete subclasses provide the upstream values
/// verbatim. <see cref="RollInitialHp"/> performs the [min,max] inclusive roll using
/// the supplied <see cref="IRngSource"/> so the same seed always yields the same
/// initial HP — matching upstream's behaviour.
/// </para>
///
/// <para>
/// <b>Powers:</b> monsters may carry power stacks at spawn time via
/// <see cref="SpawnPowers"/>; upstream's combat code stamps these onto the
/// monster's Creature during play. The runtime stack list lives on the
/// per-creature <c>Creature.Powers</c>, never on the catalog model.
/// </para>
///
/// <para>
/// <b>Immutability:</b> the model is fully immutable after construction. It is
/// a catalog singleton — <c>MonsterCatalog</c> registers one instance per id
/// and every encounter of that monster type shares it. Per-creature state
/// (current move-id cursor, block, debuffs) lives on the <c>Creature</c>
/// snapshot in <c>CombatState</c>.
/// </para>
/// </summary>
public abstract class MonsterModel : IMonsterModel
{
    private readonly IReadOnlyDictionary<string, MonsterMove> _moves;

    /// <summary>Stable string id matching upstream <c>ModelId.Entry</c>.</summary>
    public string Id { get; }

    /// <summary>Minimum initial HP (inclusive). Matches upstream <c>MinInitialHp</c>.</summary>
    public int MinInitialHp { get; }

    /// <summary>Maximum initial HP (inclusive). Matches upstream <c>MaxInitialHp</c>.</summary>
    public int MaxInitialHp { get; }

    /// <summary>Initial moves available to this monster (immutable after construction).</summary>
    public IReadOnlyList<MonsterMove> Moves { get; }

    /// <summary>
    /// Powers stamped on the monster at spawn time. Mirrors upstream's
    /// <c>AfterAddedToRoom</c> <c>PowerCmd.Apply&lt;TPower&gt;</c> calls
    /// (Louse CurlUpPower, Exoskeleton HardToKillPower, Lagavulin Plating +
    /// AsleepPower, FossilStalker SuckPower). Default empty; concrete
    /// monsters override via the constructor parameter (B.1-gamma-T3).
    /// </summary>
    public ImmutableArray<MonsterSpawnPower> SpawnPowers { get; }

    /// <summary>
    /// The move the monster performs on its first turn. The catalog model is
    /// shared by every creature of this type; per-creature rotation state
    /// (the live move-id cursor) lives on
    /// <see cref="Sts2Headless.Domain.Combat.MonsterIntent.MoveId"/>.
    /// </summary>
    public string InitialMoveId { get; }

    /// <summary>
    /// Construct with the upstream HP envelope and a move-state-machine description.
    /// </summary>
    /// <param name="id">Stable id (e.g., "calcified_cultist").</param>
    /// <param name="minInitialHp">Min HP (inclusive) per upstream.</param>
    /// <param name="maxInitialHp">Max HP (inclusive) per upstream.</param>
    /// <param name="moves">All states in the machine (order does not matter; lookup is by id).</param>
    /// <param name="initialMoveId">The first move performed (round 1's intent).</param>
    protected MonsterModel(
        string id,
        int minInitialHp,
        int maxInitialHp,
        IEnumerable<MonsterMove> moves,
        string initialMoveId,
        ImmutableArray<MonsterSpawnPower> spawnPowers = default)
    {
        SpawnPowers = spawnPowers.IsDefault ? ImmutableArray<MonsterSpawnPower>.Empty : spawnPowers;
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new System.ArgumentException("MonsterModel id must be non-empty.", nameof(id));
        }
        if (minInitialHp <= 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(minInitialHp),
                $"MonsterModel '{id}': MinInitialHp must be positive (got {minInitialHp}).");
        }
        if (maxInitialHp < minInitialHp)
        {
            throw new System.ArgumentOutOfRangeException(nameof(maxInitialHp),
                $"MonsterModel '{id}': MaxInitialHp ({maxInitialHp}) must be >= MinInitialHp ({minInitialHp}).");
        }
        System.ArgumentNullException.ThrowIfNull(moves);
        if (string.IsNullOrWhiteSpace(initialMoveId))
        {
            throw new System.ArgumentException("initialMoveId must be non-empty.", nameof(initialMoveId));
        }

        Id = id;
        MinInitialHp = minInitialHp;
        MaxInitialHp = maxInitialHp;
        List<MonsterMove> movesList = moves.ToList();
        Moves = movesList;
        // Build the lookup once. Duplicate ids would silently shadow; reject loudly.
        Dictionary<string, MonsterMove> byId = new();
        foreach (MonsterMove move in movesList)
        {
            System.ArgumentNullException.ThrowIfNull(move);
            if (byId.ContainsKey(move.Id))
            {
                throw new System.InvalidOperationException(
                    $"MonsterModel '{id}': duplicate move id '{move.Id}'.");
            }
            byId.Add(move.Id, move);
        }
        if (!byId.ContainsKey(initialMoveId))
        {
            throw new System.InvalidOperationException(
                $"MonsterModel '{id}': initialMoveId '{initialMoveId}' not in moves list.");
        }
        // Validate every follow-up references a real move id to catch typos at init.
        foreach (MonsterMove move in movesList)
        {
            if (!byId.ContainsKey(move.FollowUpMoveId))
            {
                throw new System.InvalidOperationException(
                    $"MonsterModel '{id}': move '{move.Id}' references unknown FollowUpMoveId '{move.FollowUpMoveId}'.");
            }
        }
        _moves = byId;
        InitialMoveId = initialMoveId;
    }

    /// <summary>
    /// Roll an initial HP in the inclusive [<see cref="MinInitialHp"/>,
    /// <see cref="MaxInitialHp"/>] range using <paramref name="rng"/>.
    /// Determinism: same seed yields the same value across processes / platforms.
    /// </summary>
    public int RollInitialHp(IRngSource rng)
    {
        System.ArgumentNullException.ThrowIfNull(rng);
        // Inclusive both ends. IRngSource.NextInt(min, maxExclusive) gives [min, max),
        // so we pass max+1 as the exclusive upper bound.
        return rng.NextInt(MinInitialHp, MaxInitialHp + 1);
    }

    /// <summary>
    /// RC-4 (B.1-beta-T3) byte-exact port of upstream
    /// <c>~/development/projects/godot/sts2/src/Core/Entities/Creatures/Creature.cs</c>
    /// <c>SetUniqueMonsterHpValue(IEnumerable&lt;Creature&gt; creaturesOnSide, Rng rng)</c>
    /// (lines 319-331). Rolls an initial HP that's UNIQUE among same-type
    /// monsters already on this side of the field; falls back to a regular
    /// <see cref="RollInitialHp(IRngSource)"/> when every value in the
    /// envelope is already taken.
    ///
    /// <para>
    /// Algorithm (verbatim from upstream):
    /// <list type="number">
    ///   <item>Build a <see cref="HashSet{T}"/> over [<c>MinInitialHp</c>..<c>MaxInitialHp</c>].</item>
    ///   <item>Remove all HP values already assigned to same-type creatures
    ///         via <see cref="HashSet{T}.ExceptWith"/>.</item>
    ///   <item>If the remaining set is empty, fall back to
    ///         <see cref="IRngSource.NextInt(int, int)"/>(min, max+1).</item>
    ///   <item>Otherwise call <see cref="IRngSource.NextItem{T}(IEnumerable{T})"/>
    ///         on the residual set. <c>NextItem</c> materializes the IEnumerable
    ///         into an array; <see cref="HashSet{T}"/>'s enumeration order for
    ///         small <c>int</c> sets is INSERTION ORDER in .NET 9 (verified by
    ///         <c>HashSet_int_insertion_order_is_deterministic_for_small_sets</c>
    ///         in CombatEngineTests).</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Caller must thread the <c>.Niche</c> bucket of the RunRngSet per
    /// upstream <c>CombatState.CreateCreature:133</c>.
    /// </para>
    /// </summary>
    public int RollUniqueInitialHp(IRngSource rng, IReadOnlyCollection<int> takenHps)
    {
        System.ArgumentNullException.ThrowIfNull(rng);
        System.ArgumentNullException.ThrowIfNull(takenHps);

        // Build the candidate set [Min..Max] (inclusive both ends).
        // System.Linq.Enumerable.Range(start, count) — count = Max-Min+1.
        var hpRange = Enumerable.Range(MinInitialHp, MaxInitialHp - MinInitialHp + 1).ToHashSet();
        hpRange.ExceptWith(takenHps);

        if (hpRange.Count == 0)
        {
            // Fallback path: every value already taken. Upstream falls back
            // to rng.NextInt(Min, Max+1) — exactly Q1's RollInitialHp.
            return rng.NextInt(MinInitialHp, MaxInitialHp + 1);
        }

        // NextItem<int>(HashSet<int>) — counter-monotonic single advance.
        // Note: the return type is T? where T=int → int? — but with Count>=1
        // NextItem returns a non-default int from the set. We assert non-null
        // to surface any future quirk in NextItem semantics.
        int? picked = rng.NextItem<int>(hpRange);
        if (picked is null)
        {
            throw new System.InvalidOperationException(
                $"MonsterModel '{Id}': NextItem returned null on a non-empty HashSet (Count={hpRange.Count}).");
        }
        return picked.Value;
    }

    /// <summary>
    /// The intent the monster shows on its first turn (i.e., the intent of
    /// the move whose id is <see cref="InitialMoveId"/>). For subsequent
    /// turns, callers consult the per-creature
    /// <see cref="Sts2Headless.Domain.Combat.MonsterIntent"/> directly.
    /// </summary>
    public Intent InitialIntent => _moves[InitialMoveId].Intent;

    /// <summary>
    /// Look up a move by id; throws when unknown.
    /// </summary>
    public MonsterMove GetMove(string moveId)
    {
        if (!_moves.TryGetValue(moveId, out MonsterMove? move))
        {
            throw new System.Collections.Generic.KeyNotFoundException(
                $"MonsterModel '{Id}': no move with id '{moveId}'.");
        }
        return move;
    }

    /// <summary>
    /// Compute the next move id given a per-creature cursor and the live
    /// creature snapshot — does NOT mutate the model. Per-creature rotation
    /// state lives on <see cref="Sts2Headless.Domain.Combat.MonsterIntent.MoveId"/>;
    /// this method is the read-only resolver consulted by the engine each
    /// turn to advance that cursor (B.1-gamma-T2).
    ///
    /// <para>
    /// When the current move's <see cref="MonsterMove.BranchResolver"/> is
    /// non-null, the resolver decides the next id (RNG-branch, HP-threshold,
    /// power-gate). Otherwise the static <see cref="MonsterMove.FollowUpMoveId"/>
    /// is returned — preserving the deterministic-rotation contract for moves
    /// without branches.
    /// </para>
    /// </summary>
    /// <param name="currentMoveId">The move just executed.</param>
    /// <param name="context">Creature snapshot for branch predicates.</param>
    /// <param name="runRng">Run-scope RNG fan-out (resolver picks the bucket).</param>
    public virtual string AdvanceMoveId(string currentMoveId, MoveBranchContext context, RunRngSet runRng)
    {
        System.ArgumentException.ThrowIfNullOrWhiteSpace(currentMoveId);
        System.ArgumentNullException.ThrowIfNull(runRng);
        if (!_moves.TryGetValue(currentMoveId, out MonsterMove? move))
        {
            throw new System.Collections.Generic.KeyNotFoundException(
                $"MonsterModel '{Id}': no move with id '{currentMoveId}'.");
        }
        return move.BranchResolver?.Resolve(context, runRng) ?? move.FollowUpMoveId;
    }
}

using System.Collections.Immutable;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// One node in a monster's move-rotation state machine. Matches the data shape of
/// upstream's <c>MoveState</c>
/// (~/development/projects/godot/sts2/src/Core/MonsterMoves/MonsterMoveStateMachine/MoveState.cs):
/// each move has a stable id, the <see cref="Intent"/> shown to the player, and a
/// pointer (by id) to the move that follows.
///
/// <para>
/// <b>Deterministic-rotation contract:</b> when <see cref="BranchResolver"/> is null,
/// <see cref="FollowUpMoveId"/> wholly determines the next state (Cultist /
/// Chomper patterns). When non-null, the resolver decides the next move id based
/// on the live creature snapshot (HP / powers via <see cref="MoveBranchContext"/>)
/// and the <see cref="RunRngSet"/> — supporting upstream's <c>RandomBranchState</c> /
/// <c>ConditionalBranchState</c> shapes (B.1-gamma-T2).
/// </para>
///
/// <para>
/// <b>Cheap-clone:</b> the resolver is a stateless singleton on the catalog
/// model; creatures of the same monster type share the same resolver instance.
/// Per-creature rotation state is carried via the
/// <see cref="Sts2Headless.Domain.Combat.MonsterIntent.MoveId"/> cursor, not via
/// mutable resolver state.
/// </para>
/// </summary>
public sealed record MonsterMove(
    string Id,
    Intent Intent,
    string FollowUpMoveId,
    IMoveBranchResolver? BranchResolver = null
);

/// <summary>
/// Read-only snapshot the engine hands to an <see cref="IMoveBranchResolver"/>
/// at branch time. Avoids an upward dependency on the
/// <see cref="Sts2Headless.Domain.Combat"/> namespace by passing primitive
/// fields instead of a Creature reference.
/// </summary>
/// <param name="CurrentHp">Creature's current HP.</param>
/// <param name="MaxHp">Creature's max HP.</param>
/// <param name="HasPower">Predicate: does this creature carry the named power
/// with positive stacks? Closures over the live Powers list for the duration
/// of the branch call.</param>
/// <param name="GetPowerStacks">Get the stack count for a power (zero if
/// absent). Useful when a branch depends on a stack threshold.</param>
public readonly record struct MoveBranchContext(
    int CurrentHp,
    int MaxHp,
    System.Func<string, bool> HasPower,
    System.Func<string, int> GetPowerStacks
);

/// <summary>
/// Decides the next move id when the current move has branching logic
/// (RNG-driven, HP-threshold, prior-move-dependent). Stateless singleton
/// shared across all creatures of the same monster type — per-creature
/// rotation state lives on <see cref="Sts2Headless.Domain.Combat.MonsterIntent.MoveId"/>.
/// </summary>
public interface IMoveBranchResolver
{
    /// <summary>
    /// Resolve the next move id given the creature snapshot and the run-scope
    /// RNG. May consume from <paramref name="runRng"/>; consumption must be
    /// deterministic for a fixed RNG state.
    /// </summary>
    string Resolve(MoveBranchContext context, RunRngSet runRng);
}

/// <summary>One weighted choice in an <see cref="RngBranchResolver"/>.</summary>
public sealed record RngBranchChoice(string MoveId, float Weight);

/// <summary>
/// Weighted-random pick across a fixed set of follow-up moves. Mirrors
/// upstream <c>RandomBranchState.AddBranch(moveState, repeat, weight)</c>.
/// Consumes one float from the configured RNG bucket per resolution.
/// </summary>
public sealed class RngBranchResolver : IMoveBranchResolver
{
    private readonly ImmutableArray<RngBranchChoice> _choices;
    private readonly float _totalWeight;
    private readonly RunRngType _bucket;

    /// <summary>
    /// Construct an RNG branch resolver.
    /// </summary>
    /// <param name="choices">Weighted move-id choices (must be non-empty).</param>
    /// <param name="bucket">RNG bucket to consume from. Defaults to
    /// <see cref="RunRngType.MonsterAi"/> — upstream's
    /// <c>RandomBranchState.Pick</c> consumes from
    /// <c>RunState.Rng.MonsterAi</c>.</param>
    public RngBranchResolver(
        ImmutableArray<RngBranchChoice> choices,
        RunRngType bucket = RunRngType.MonsterAi
    )
    {
        if (choices.IsDefault || choices.Length == 0)
        {
            throw new System.ArgumentException(
                "RngBranchResolver requires at least one choice.",
                nameof(choices)
            );
        }
        float total = 0f;
        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i].Weight <= 0f)
            {
                throw new System.ArgumentException(
                    $"RngBranchChoice weights must be positive (got {choices[i].Weight} for {choices[i].MoveId}).",
                    nameof(choices)
                );
            }
            total += choices[i].Weight;
        }
        _choices = choices;
        _totalWeight = total;
        _bucket = bucket;
    }

    /// <inheritdoc />
    public string Resolve(MoveBranchContext context, RunRngSet runRng)
    {
        System.ArgumentNullException.ThrowIfNull(runRng);
        // Sample uniformly in [0, totalWeight) and find the first cumulative
        // bucket containing the sample. Deterministic for fixed RNG state.
        // CA1859: IRngSource is the determinism-substitution boundary; concrete
        // type would defeat test-fake injection.
#pragma warning disable CA1859
        IRngSource rng = runRng[_bucket];
#pragma warning restore CA1859
        float sample = rng.NextFloat() * _totalWeight;
        float running = 0f;
        for (int i = 0; i < _choices.Length; i++)
        {
            running += _choices[i].Weight;
            if (sample < running)
                return _choices[i].MoveId;
        }
        // Fallback: last choice (covers floating-point boundary).
        return _choices[_choices.Length - 1].MoveId;
    }
}

/// <summary>
/// Branches based on whether the creature's current HP fraction is below
/// <see cref="Fraction"/>. Mirrors upstream's HP-threshold conditional
/// branches (e.g., <c>Creature.CurrentHp &lt;= Creature.MaxHp / 2</c>).
///
/// <para>
/// <b>Convention:</b> the boundary value (<c>currentHp / maxHp == Fraction</c>)
/// uses the "above" branch. Strictly-below crosses to "below". This matches
/// upstream's <c>&lt;= half</c> pattern when read as "NOT-below = above".
/// </para>
/// </summary>
public sealed class HpThresholdResolver : IMoveBranchResolver
{
    /// <summary>Threshold fraction (e.g., 0.5 for half HP).</summary>
    public float Fraction { get; }

    /// <summary>Move id to take when HP fraction is strictly below
    /// <see cref="Fraction"/>.</summary>
    public string BelowMoveId { get; }

    /// <summary>Move id to take when HP fraction is at or above
    /// <see cref="Fraction"/>.</summary>
    public string AboveMoveId { get; }

    public HpThresholdResolver(float fraction, string belowMoveId, string aboveMoveId)
    {
        if (fraction <= 0f || fraction > 1f)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(fraction),
                $"HpThresholdResolver fraction must be in (0, 1] (got {fraction})."
            );
        }
        System.ArgumentException.ThrowIfNullOrWhiteSpace(belowMoveId);
        System.ArgumentException.ThrowIfNullOrWhiteSpace(aboveMoveId);
        Fraction = fraction;
        BelowMoveId = belowMoveId;
        AboveMoveId = aboveMoveId;
    }

    /// <inheritdoc />
    public string Resolve(MoveBranchContext context, RunRngSet runRng)
    {
        if (context.MaxHp <= 0)
            return AboveMoveId;
        float current = (float)context.CurrentHp / context.MaxHp;
        return current < Fraction ? BelowMoveId : AboveMoveId;
    }
}

/// <summary>
/// Branches based on whether the creature carries a named power.
/// Mirrors upstream conditional branches like Lagavulin's
/// <c>HasPower&lt;AsleepPower&gt;</c> gate.
/// </summary>
public sealed class HasPowerResolver : IMoveBranchResolver
{
    public string PowerId { get; }
    public string HasPowerMoveId { get; }
    public string LacksPowerMoveId { get; }

    public HasPowerResolver(string powerId, string hasPowerMoveId, string lacksPowerMoveId)
    {
        System.ArgumentException.ThrowIfNullOrWhiteSpace(powerId);
        System.ArgumentException.ThrowIfNullOrWhiteSpace(hasPowerMoveId);
        System.ArgumentException.ThrowIfNullOrWhiteSpace(lacksPowerMoveId);
        PowerId = powerId;
        HasPowerMoveId = hasPowerMoveId;
        LacksPowerMoveId = lacksPowerMoveId;
    }

    /// <inheritdoc />
    public string Resolve(MoveBranchContext context, RunRngSet runRng) =>
        context.HasPower(PowerId) ? HasPowerMoveId : LacksPowerMoveId;
}

/// <summary>
/// Delegates branching to a caller-supplied predicate. Used for branches
/// that depend on combinations of creature state beyond what the built-in
/// resolvers express.
/// </summary>
public sealed class PredicateBranchResolver : IMoveBranchResolver
{
    private readonly System.Func<MoveBranchContext, RunRngSet, string> _resolver;

    public PredicateBranchResolver(System.Func<MoveBranchContext, RunRngSet, string> resolver)
    {
        System.ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <inheritdoc />
    public string Resolve(MoveBranchContext context, RunRngSet runRng) =>
        _resolver(context, runRng);
}

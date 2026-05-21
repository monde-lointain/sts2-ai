using System.Collections.Immutable;
using Sts2Headless.Domain.Content.Models;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Coarse intent kind shown for a monster's next move. Verbatim subset of the
/// upstream <c>IntentType</c> enum (only the kinds the smoke encounter — Cultists —
/// exercise). Phase 1 expands as new monsters need them.
/// </summary>
public enum MonsterIntentKind
{
    /// <summary>Default zero. Used when no intent has been resolved yet.</summary>
    Unknown = 0,
    Attack,
    AttackDefend,
    Defend,
    Buff,
    Debuff,
    Sleep,

    /// <summary>
    /// Status-card-pollution intent (e.g., SCREECH, STICKY_SHOT). Maps to upstream's
    /// <c>IntentKind.Status(N)</c> where N is the number of status cards added.
    /// Engine payload (adding status cards to player discard) is deferred; rotation
    /// advances normally.
    /// </summary>
    Status,
}

/// <summary>One (powerId, stacks, target) triple that a monster intent will apply on resolution.</summary>
/// <param name="PowerId">String id matching <c>PowerCatalog</c>.</param>
/// <param name="Stacks">Stack count to apply (Counter) or assign (Single).</param>
/// <param name="Target">
/// Which side receives the power application. Defaults to <see cref="PowerTarget.Self"/>
/// so existing two-argument construction sites compile unchanged and pre-Wave-B
/// serialized blobs decode correctly after the schema version bump.
/// </param>
public sealed record MonsterIntentPower(string PowerId, int Stacks, PowerTarget Target = PowerTarget.Self);

/// <summary>
/// What a monster intends to do on its next turn — already-resolved (the monster's
/// model state machine ran ahead of time per upstream's <c>PrepareForNextTurn</c>)
/// but not yet executed. Sits on the monster's <see cref="Creature.Intent"/> slot
/// until <c>EnemyTurn</c> resolves it.
///
/// <para>
/// <b>Why two layers (this + S5 <see cref="Sts2Headless.Domain.Content.Models.Intent"/>):</b>
/// the content-layer <c>Intent</c> is the catalog declaration (one per move in the
/// monster's state machine). This <c>MonsterIntent</c> is the runtime instance —
/// it expands the catalog intent into the full executable payload (powers list,
/// hit count, etc.) for the resolver. Mapping is via
/// <see cref="FromContentIntent"/>.
/// </para>
///
/// <para>
/// <b>Cheap-clone friendly:</b> <see cref="ImmutableList{T}"/> for the powers list
/// keeps the record cheaply <c>with</c>-able.
/// </para>
/// </summary>
/// <param name="Kind">Coarse intent kind.</param>
/// <param name="DamagePerHit">Per-hit damage for Attack-shape intents (zero otherwise).</param>
/// <param name="HitCount">Number of hits for Attack-shape intents (one for single-attack).</param>
/// <param name="AppliesPowers">
/// Powers this intent applies on resolution (e.g., INCANTATION applies Ritual to self).
/// Order-preserving; empty list for moves with no power application.
/// </param>
/// <param name="MoveId">
/// Per-creature move-state-machine cursor. Stream-B-T3 addition: when multiple
/// monsters share a single <c>MonsterModel</c> instance from the catalog, the
/// move-id must live on the creature (not on the shared model) so each
/// individual enemy's rotation advances independently. Empty string for
/// monsters that don't advance their rotation between turns (most Phase-1
/// single-state monsters).
/// </param>
/// <param name="SelfBlockGain">
/// Block the monster gains when executing this intent. Used by
/// <see cref="MonsterIntentKind.Defend"/> (from <c>Intent.Defend(N)</c>) and
/// <see cref="MonsterIntentKind.AttackDefend"/> (attack + self-block hybrid).
/// Zero for pure attack/buff/debuff moves.
/// </param>
public sealed record MonsterIntent(
    MonsterIntentKind Kind,
    int DamagePerHit,
    int HitCount,
    ImmutableList<MonsterIntentPower> AppliesPowers,
    string MoveId = "",
    int SelfBlockGain = 0
)
{
    /// <summary>"No intent resolved yet" sentinel — Kind=Unknown, no damage, no powers.</summary>
    public static MonsterIntent None { get; } =
        new(MonsterIntentKind.Unknown, 0, 0, ImmutableList<MonsterIntentPower>.Empty, string.Empty, 0);

    /// <summary>
    /// Override record-default equality so the AppliesPowers list is compared
    /// element-wise. See <see cref="Creature.Equals(Creature?)"/> for the rationale.
    /// </summary>
    public bool Equals(MonsterIntent? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (Kind != other.Kind)
            return false;
        if (DamagePerHit != other.DamagePerHit || HitCount != other.HitCount)
            return false;
        if (SelfBlockGain != other.SelfBlockGain)
            return false;
        if (!string.Equals(MoveId, other.MoveId, System.StringComparison.Ordinal))
            return false;
        if (AppliesPowers.Count != other.AppliesPowers.Count)
            return false;
        for (int i = 0; i < AppliesPowers.Count; i++)
        {
            if (!AppliesPowers[i].Equals(other.AppliesPowers[i]))
                return false;
        }
        return true;
    }

    /// <summary>Override required to match overridden <see cref="Equals(MonsterIntent?)"/>.</summary>
    public override int GetHashCode()
    {
        HashCode h = default;
        h.Add(Kind);
        h.Add(DamagePerHit);
        h.Add(HitCount);
        h.Add(SelfBlockGain);
        h.Add(MoveId);
        for (int i = 0; i < AppliesPowers.Count; i++)
            h.Add(AppliesPowers[i]);
        return h.ToHashCode();
    }

    /// <summary>
    /// Build a runtime <see cref="MonsterIntent"/> from a catalog-layer
    /// <see cref="MonsterMove"/>. Reads <c>move.Intent</c>, <c>move.AppliesPowers</c>,
    /// and <c>move.SelfBlockGain</c> to produce the full executable payload.
    /// </summary>
    public static MonsterIntent FromContentIntent(MonsterMove move) =>
        FromContentIntent(move, string.Empty);

    /// <summary>
    /// Build a runtime <see cref="MonsterIntent"/> from a catalog-layer
    /// <see cref="MonsterMove"/> with a recorded <paramref name="moveId"/> for
    /// per-creature state-machine cursor tracking (Stream-B-T3). See the
    /// constructor's <c>MoveId</c> remarks for rationale.
    /// </summary>
    public static MonsterIntent FromContentIntent(MonsterMove move, string moveId)
    {
        ImmutableList<MonsterIntentPower> powers =
            move.AppliesPowers ?? ImmutableList<MonsterIntentPower>.Empty;
        Intent source = move.Intent;

        return source.Kind switch
        {
            // Attack + self-block → AttackDefend (hybrid)
            IntentKind.Attack when move.SelfBlockGain > 0 => new(
                MonsterIntentKind.AttackDefend,
                source.Value,
                source.HitCount > 0 ? source.HitCount : 1,
                powers,
                moveId,
                move.SelfBlockGain
            ),
            // Pure attack
            IntentKind.Attack => new(
                MonsterIntentKind.Attack,
                source.Value,
                source.HitCount > 0 ? source.HitCount : 1,
                powers,
                moveId,
                0
            ),
            // Defend: Intent.Value carries the declared block amount; SelfBlockGain can supplement.
            IntentKind.Defend => new(
                MonsterIntentKind.Defend,
                0,
                0,
                powers,
                moveId,
                source.Value + move.SelfBlockGain
            ),
            IntentKind.Buff => new(
                MonsterIntentKind.Buff,
                0,
                0,
                powers,
                moveId,
                0
            ),
            IntentKind.Debuff => new(
                MonsterIntentKind.Debuff,
                0,
                0,
                powers,
                moveId,
                0
            ),
            IntentKind.Sleep => new(
                MonsterIntentKind.Sleep,
                0,
                0,
                ImmutableList<MonsterIntentPower>.Empty,
                moveId,
                0
            ),
            // Status (card pollution) — first-class Kind=Status; HitCount carries the
            // card count (overloaded, no engine consumer yet). Engine payload deferred.
            IntentKind.Status => new(
                MonsterIntentKind.Status,
                0,
                source.Value,
                powers,
                moveId,
                0
            ),
            _ => None,
        };
    }

    // ---- Back-compat overloads (Intent source) ----------------------------
    // Kept so existing test sites (StatePrimitiveTests.cs:131,141) and the
    // determinism-probe comparer (UpstreamInitialStateComparer.cs:241) compile
    // unchanged. They build a synthetic MonsterMove with null AppliesPowers and
    // zero SelfBlockGain, then delegate to the MonsterMove overload.

    /// <summary>
    /// Back-compat: builds a synthetic <see cref="MonsterMove"/> with no
    /// <c>AppliesPowers</c> / zero <c>SelfBlockGain</c> and delegates to
    /// <see cref="FromContentIntent(MonsterMove)"/>.
    /// </summary>
    public static MonsterIntent FromContentIntent(Intent source) =>
        FromContentIntent(new MonsterMove(string.Empty, source, string.Empty));

    /// <summary>
    /// Back-compat: builds a synthetic <see cref="MonsterMove"/> with no
    /// <c>AppliesPowers</c> / zero <c>SelfBlockGain</c> and delegates to
    /// <see cref="FromContentIntent(MonsterMove, string)"/>.
    /// </summary>
    public static MonsterIntent FromContentIntent(Intent source, string moveId) =>
        FromContentIntent(new MonsterMove(string.Empty, source, string.Empty), moveId);
}

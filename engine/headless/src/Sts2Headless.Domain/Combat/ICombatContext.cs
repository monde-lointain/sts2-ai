using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// The state-mutation surface presented to S5 content code (card OnPlay, relic
/// OnHook handlers) and S4 actions. Implementations hold a mutable handle to the
/// "current" <see cref="CombatState"/> and rewrite it through record
/// <c>with</c>-expressions on every call. Action code therefore never touches a
/// CombatState directly — they go through this port.
///
/// <para>
/// <b>Why a single mutable wrapper around immutable state:</b> the alternative
/// is threading <c>ref CombatState</c> through every action's <c>Execute</c>.
/// That works but breaks the existing <see cref="Sts2Headless.Domain.Actions.IAction"/>
/// interface (<c>void Execute(ExecutionContext)</c>) and the registered
/// hook-handler delegate shape. The wrapper is a single allocation per combat
/// and keeps the action pipeline byte-compatible with S4.
/// </para>
///
/// <para>
/// <b>Cheap-clone semantics:</b> <see cref="State"/> is the immutable record at
/// any given instant; <c>state with { ... }</c> on a snapshot doesn't disturb the
/// engine's current pointer. S17 will clone the state field directly (each
/// CombatState is structurally immutable).
/// </para>
///
/// <para>
/// <b>State-codec semantics:</b> S7 (M1 State Codec) serializes/deserializes
/// CombatState directly; it never touches an ICombatContext.
/// </para>
/// </summary>
public interface ICombatContext
{
    // === Read-only ports ==================================================

    /// <summary>
    /// Convenience handle to a single bucket of the M5 determinism kernel.
    /// Backed by <see cref="RunRng"/>'s <c>Shuffle</c> bucket — the default
    /// in-combat consumer (deck reshuffle on empty draw pile, matching
    /// upstream <c>CardPileCmd.Shuffle</c> line 795:
    /// <c>list.StableShuffle(player.RunState.Rng.Shuffle)</c>). Content
    /// code that needs a different bucket (HP rolls on <c>.Niche</c>,
    /// monster AI on <c>.MonsterAi</c>, etc.) goes through
    /// <see cref="RunRng"/> instead.
    /// </summary>
    IRngSource Rng { get; }

    /// <summary>
    /// The full <see cref="Determinism.RunRngSet"/> driving this combat.
    /// Exposes every upstream <see cref="Determinism.RunRngType"/> bucket so
    /// content code can pick the correct stream per-callsite (HP rolls on
    /// <c>.Niche</c>, deck shuffles on <c>.Shuffle</c>, monster AI on
    /// <c>.MonsterAi</c>, etc.) — direct port of upstream's
    /// <c>IRunState.Rng</c> surface.
    ///
    /// <para>
    /// <b>B.1-alpha-T2 (RC-3):</b> added because the pre-refactor
    /// single-shared-<c>IRngSource</c> wiring diverged from upstream's
    /// per-bucket consumption topology — see Stream-C-T5 report.
    /// </para>
    /// </summary>
    Determinism.RunRngSet RunRng { get; }

    /// <summary>Determinism kernel's clock for this combat.</summary>
    IClock Clock { get; }

    /// <summary>The current immutable combat state.</summary>
    CombatState State { get; }

    /// <summary>Card catalog (resolves <c>CardInstance.ModelId</c> to CardModel).</summary>
    CardCatalog Cards { get; }

    /// <summary>Relic catalog.</summary>
    RelicCatalog Relics { get; }

    /// <summary>Power catalog.</summary>
    PowerCatalog Powers { get; }

    /// <summary>Monster catalog.</summary>
    MonsterCatalog Monsters { get; }

    /// <summary>Encounter catalog.</summary>
    EncounterCatalog Encounters { get; }

    // === Mutation ports (each updates State in place) =====================

    /// <summary>
    /// Deal <paramref name="amount"/> raw damage to the creature identified by
    /// <paramref name="targetId"/>. Applies block first, then HP. Source ID is
    /// recorded for hook payloads but doesn't affect the math at the smoke
    /// level. <see cref="Sts2Headless.Domain.Content.Powers.VulnerablePower"/>
    /// and <see cref="Sts2Headless.Domain.Content.Powers.StrengthPower"/>
    /// modifiers are applied by the engine before this call (so callers pass
    /// post-modifier damage).
    /// </summary>
    void DealDamage(CreatureId targetId, int amount, CreatureId sourceId);

    /// <summary>
    /// Grant <paramref name="amount"/> block to <paramref name="targetId"/>.
    /// </summary>
    void GainBlock(CreatureId targetId, int amount);

    /// <summary>
    /// Apply <paramref name="stacks"/> of <paramref name="powerId"/> to
    /// <paramref name="targetId"/>. If the target already has the power, the
    /// stacks combine per the power's <c>StackType</c>. Source ID is recorded
    /// on the new <see cref="PowerInstance"/>.
    /// </summary>
    void ApplyPower(CreatureId targetId, string powerId, int stacks, CreatureId sourceId);

    /// <summary>
    /// Heal <paramref name="amount"/> HP on <paramref name="targetId"/>, clamped
    /// at their <c>MaxHp</c>.
    /// </summary>
    void Heal(CreatureId targetId, int amount);

    /// <summary>Draw <paramref name="count"/> cards into hand.</summary>
    void DrawCards(int count);

    /// <summary>
    /// Discard the entire hand. Cards move to the discard pile. Used by
    /// end-of-turn. Does not honor Retain (smoke set has no Retain cards).
    /// </summary>
    void DiscardHand();

    /// <summary>
    /// Modify the per-turn hand-draw size by <paramref name="delta"/> (typically
    /// +2 for RingOfTheSnake / BagOfPreparation at combat start).
    /// </summary>
    void ModifyHandDrawSize(int delta);

    /// <summary>Add <paramref name="amount"/> energy to the current pool.</summary>
    void IncreaseEnergy(int amount);

    /// <summary>
    /// Replace the current state wholesale. Escape hatch for engine glue that
    /// needs to do multi-field updates atomically (e.g., turn phase transitions).
    /// Content code should prefer the higher-level helpers above.
    /// </summary>
    void SetState(CombatState state);

    /// <summary>
    /// Spawn additional enemies into the current combat. Each element in
    /// <paramref name="enemies"/> is appended to <see cref="State"/>'s enemy
    /// list; unique creature ids must be pre-allocated via
    /// <see cref="CreatureIdAllocator"/>. Use this from OnAfterDeath hooks (e.g.,
    /// SurprisePower) to introduce reinforcements mid-combat.
    ///
    /// <para>
    /// <b>StateCodec compatibility:</b> no schema-version bump needed — the
    /// codec encodes enemy count as a plain i32 and already round-trips arbitrary
    /// counts. Forward-compatible by design per Q1-ADR-002.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    /// If any element has <c>IsPlayer=true</c> or its id collides with an
    /// existing creature.
    /// </exception>
    void AddEnemies(IEnumerable<Creature> enemies);

    /// <summary>
    /// B.1-gamma-T5: amount of energy consumed by the most recently played
    /// X-cost card. Engine snapshots <see cref="CombatState.Energy"/> before
    /// consumption and writes it here so the card's <c>OnPlay</c> body can
    /// read the value via <see cref="TrailCounters.LastSpentEnergy"/>. Mirrors
    /// upstream <c>CardModel.ResolveEnergyXValue()</c>. Returns 0 if no
    /// X-cost card has been played in this combat.
    /// </summary>
    int AllRemainingEnergy();
}

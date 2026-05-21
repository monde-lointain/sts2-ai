using System.Collections.Immutable;
using Sts2Headless.Domain.Content;
using Sts2Headless.Domain.Content.Encounters;
using Sts2Headless.Domain.Content.Models;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// Default in-process implementation of <see cref="ICombatContext"/>. Holds a
/// mutable handle to the current <see cref="CombatState"/> and applies record
/// <c>with</c>-expressions on each mutation. Construction wires the kernel
/// ports (Rng / Clock) and the five-plus-one S3/S5 content catalogs.
///
/// <para>
/// <b>Per-combat lifetime:</b> a single instance lives for one combat. The
/// engine creates it in <c>StartCombat</c> and discards it at
/// <c>CombatEnd</c>. RNG/Clock are shared with the surrounding run.
/// </para>
///
/// <para>
/// <b>Mutation discipline:</b> every mutation method computes a new
/// <see cref="CombatState"/> via <c>State with { ... }</c> and assigns it back
/// to the internal <c>_state</c> field. We never construct mutable lists or
/// nested mutable graphs — the state remains snapshot-able for S17 at any
/// point.
/// </para>
/// </summary>
public sealed class CombatContext : ICombatContext
{
    private CombatState _state;

    /// <inheritdoc />
    public IRngSource Rng { get; }

    /// <inheritdoc />
    public RunRngSet RunRng { get; }
    public IClock Clock { get; }
    public CardCatalog Cards { get; }
    public RelicCatalog Relics { get; }
    public PowerCatalog Powers { get; }
    public MonsterCatalog Monsters { get; }
    public EncounterCatalog Encounters { get; }

    /// <inheritdoc />
    public CombatState State => _state;

    /// <summary>
    /// Construct a live combat context.
    ///
    /// <para>
    /// <b>B.1-alpha-T2 (RC-3):</b> takes the full <see cref="RunRngSet"/>
    /// rather than a single shared <see cref="IRngSource"/>. The convenience
    /// <see cref="Rng"/> property routes to the <c>.Shuffle</c> bucket — the
    /// upstream default for in-combat reshuffles (see <see cref="DrawCards"/>
    /// where an empty draw pile reshuffles the discard via
    /// <c>discard.Shuffle(Rng)</c>, matching upstream
    /// <c>CardPileCmd.Shuffle</c> line 795). Bucket-aware consumers route
    /// through <see cref="RunRng"/> directly.
    /// </para>
    ///
    /// <para>
    /// <b>Wave A:</b> <paramref name="plumbing"/> is required at construction
    /// time so the context is fully wired. Pass <see cref="HookPlumbing.Empty"/>
    /// for snapshot / inspection contexts (ControlPlaneSession state restore,
    /// hand-constructed unit tests) — see <see cref="HookPlumbing.Empty"/> for
    /// constraints on using the resulting context.
    /// </para>
    /// </summary>
    public CombatContext(
        CombatState initialState,
        RunRngSet runRng,
        IClock clock,
        CardCatalog cards,
        RelicCatalog relics,
        PowerCatalog powers,
        MonsterCatalog monsters,
        EncounterCatalog encounters,
        HookPlumbing plumbing
    )
    {
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentNullException.ThrowIfNull(runRng);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(relics);
        ArgumentNullException.ThrowIfNull(powers);
        ArgumentNullException.ThrowIfNull(monsters);
        ArgumentNullException.ThrowIfNull(encounters);
        ArgumentNullException.ThrowIfNull(plumbing);

        _state = initialState;
        RunRng = runRng;
        // The convenience port routes to the Shuffle bucket — the upstream
        // default for in-combat reshuffles (deck-empty -> shuffle discard
        // path, CardPileCmd.Shuffle:795).
        Rng = runRng.Shuffle;
        Clock = clock;
        Cards = cards;
        Relics = relics;
        Powers = powers;
        Monsters = monsters;
        Encounters = encounters;
        Plumbing = plumbing;
    }

    public void SetState(CombatState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    // Wave A: plumbing is required at ctor; fully wired from construction.
    // All engine consumers access hook plumbing via ctx.Plumbing.{Hooks,Queue,Context}.
    internal HookPlumbing Plumbing { get; }

    public void DealDamage(uint targetId, int amount, uint sourceId)
    {
        if (amount <= 0)
            return;
        Creature target = ResolveTarget(targetId);

        // Block absorbs first; overflow hits HP.
        int blockAbsorbed = Math.Min(target.Block, amount);
        int hpDamage = amount - blockAbsorbed;
        int newBlock = target.Block - blockAbsorbed;
        int newHp = Math.Max(0, target.CurrentHp - hpDamage);

        var updated = target with { Block = newBlock, CurrentHp = newHp };
        _state = WriteCreature(updated);
    }

    public void GainBlock(uint targetId, int amount)
    {
        if (amount <= 0)
            return;
        Creature target = ResolveTarget(targetId);
        var updated = target with { Block = target.Block + amount };
        _state = WriteCreature(updated);
    }

    public void ApplyPower(uint targetId, string powerId, int stacks, uint sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(powerId);
        Creature target = ResolveTarget(targetId);
        // Catalog stores marker interface; concrete base provides StackType. The cast
        // is safe by construction: every entry in PowerCatalog is a PowerModel via
        // SmokeContent / S5 registration.
        PowerModel model = (PowerModel)Powers.Get(powerId);

        // Find existing instance, if any.
        int existingIndex = -1;
        for (int i = 0; i < target.Powers.Count; i++)
        {
            if (target.Powers[i].ModelId == powerId)
            {
                existingIndex = i;
                break;
            }
        }

        ImmutableList<PowerInstance> newPowers;
        if (existingIndex >= 0)
        {
            PowerInstance existing = target.Powers[existingIndex];
            int newStacks =
                model.StackType == PowerStackType.Counter ? existing.Stacks + stacks : stacks;
            var replaced = existing with
            {
                Stacks = newStacks,
                SourceCreatureId = sourceId,
                JustApplied = true,
            };
            newPowers = target.Powers.SetItem(existingIndex, replaced);
        }
        else
        {
            var fresh = new PowerInstance(
                ModelId: powerId,
                Stacks: stacks,
                SourceCreatureId: sourceId,
                JustApplied: true
            );
            newPowers = target.Powers.Add(fresh);
        }
        var updated = target with { Powers = newPowers };
        _state = WriteCreature(updated);

        // Wave-26/Q1.D OnApplied bridge: notify the power model that a new
        // PowerInstance was attached to a creature so it can subscribe hooks.
        // Wave A: Plumbing is now always present; empty plumbing (snapshot
        // contexts) has a real-but-inert HookRegistry with zero subscribers —
        // OnApplied/SubscribeHooks are no-ops on pure-metadata powers regardless.
        if (existingIndex < 0)
        {
            // Fresh attachment: call OnApplied which creates the handle slot
            // and calls SubscribeHooks (no-op for pure-metadata powers).
            model.OnApplied(targetId, Plumbing.Hooks);

            // ICombatAwarePowerModel opt-in: powers that need ICombatContext
            // (e.g., SurprisePower) subscribe their hooks here with the live ctx.
            if (model is ICombatAwarePowerModel cam)
                cam.OnAppliedWithContext(targetId, Plumbing.Hooks, this);
        }
        // Note: re-application (existingIndex >= 0) does NOT call OnApplied again.
        // Per PowerModel doc: "double-apply without a prior remove is a caller bug".
        // For Counter-stack re-applications, the subscription from the first
        // OnApplied call is still active — no re-subscribe needed.
    }

    public void Heal(uint targetId, int amount)
    {
        if (amount <= 0)
            return;
        Creature target = ResolveTarget(targetId);
        int newHp = Math.Min(target.MaxHp, target.CurrentHp + amount);
        var updated = target with { CurrentHp = newHp };
        _state = WriteCreature(updated);
    }

    public void DrawCards(int count)
    {
        if (count <= 0)
            return;
        CombatState s = _state;
        int drawnThisCall = 0;

        for (int i = 0; i < count; i++)
        {
            // Reshuffle if draw pile is empty.
            if (s.DrawPile.IsEmpty)
            {
                if (s.DiscardPile.IsEmpty)
                {
                    // Both empty — can't draw any more.
                    break;
                }
                CardPile reshuffled = s.DiscardPile.Shuffle(Rng);
                s = s with { DrawPile = reshuffled, DiscardPile = CardPile.Empty };
            }

            var (remaining, drawn) = s.DrawPile.DrawTop();
            s = s with { DrawPile = remaining, HandPile = s.HandPile.Add(drawn) };
            drawnThisCall++;
        }
        // Stream-B-T4: maintain the cumulative cards-drawn-this-combat counter
        // used by calc-damage cards like Murder (damage = base × draws).
        _state = s with
        {
            PlayerRngCounter = Rng.Counter,
            CardsDrawnThisCombat = s.CardsDrawnThisCombat + drawnThisCall,
        };
    }

    public void DiscardHand()
    {
        if (_state.HandPile.IsEmpty)
            return;
        // Move every hand card to the discard in order.
        CombatState s = _state;
        var hand = s.HandPile;
        var discard = s.DiscardPile;
        for (int i = 0; i < hand.Cards.Count; i++)
        {
            discard = discard.Add(hand.Cards[i]);
        }
        _state = s with { HandPile = CardPile.Empty, DiscardPile = discard };
    }

    public void ModifyHandDrawSize(int delta)
    {
        if (delta == 0)
            return;
        _state = _state with { HandDrawSize = Math.Max(0, _state.HandDrawSize + delta) };
    }

    public void IncreaseEnergy(int amount)
    {
        _state = _state with { Energy = _state.Energy + amount };
    }

    /// <inheritdoc />
    public int AllRemainingEnergy() => _state.LastSpentEnergy;

    /// <inheritdoc />
    public void AddEnemies(IEnumerable<Creature> enemies)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        _state = _state.WithSpawnedEnemies(enemies);
    }

    // --- helpers ----------------------------------------------------------

    /// <summary>
    /// Resolve a creature by id — returns the player when <paramref name="targetId"/>
    /// matches the player's id, else searches enemies. Throws when not found.
    /// </summary>
    private Creature ResolveTarget(uint targetId)
    {
        if (_state.Player.Id == targetId)
            return _state.Player;
        Creature? enemy = _state.FindEnemy(targetId);
        if (enemy is null)
        {
            throw new InvalidOperationException(
                $"CombatContext: no creature with id={targetId} (player={_state.Player.Id}, "
                    + $"enemies={string.Join(",", _state.Enemies.Select(e => e.Id))})."
            );
        }
        return enemy;
    }

    /// <summary>
    /// Write <paramref name="updated"/> back into the state, dispatching by
    /// <c>IsPlayer</c>. Used by every mutator that targets a creature.
    /// </summary>
    private CombatState WriteCreature(Creature updated)
    {
        return updated.IsPlayer ? _state.WithPlayer(updated) : _state.WithEnemy(updated);
    }
}

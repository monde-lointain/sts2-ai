using System.Collections.Generic;
using ExecutionContext = Sts2Headless.Domain.Actions.ExecutionContext;

namespace Sts2Headless.Domain.Content.Models;

/// <summary>
/// Abstract base for all card content. This is Q1's headless analogue of upstream
/// <c>MegaCrit.Sts2.Core.Models.CardModel</c>
/// (~/development/projects/godot/sts2/src/Core/Models/CardModel.cs:32) — same shape
/// concrete subclasses need (constructor takes energy/type/rarity/target, override
/// <see cref="OnPlay"/>), without the Godot scene / asset / animation surface
/// upstream pulls in.
///
/// <para>
/// <b>Why a fresh base instead of inheriting upstream:</b> upstream <c>CardModel</c> is
/// ~1700 lines of asset paths, localization, hover tips, audio cues, and Godot
/// scene wiring. Q1's domain only needs the play-result delta + ordering metadata.
/// Concrete cards (S5/S12) take only the values from their upstream twin
/// (damage, block, draw, etc.) and embed them in <see cref="OnPlay"/>.
/// </para>
///
/// <para>
/// <b>Immutable singleton (R7):</b> the model carries no mutable instance state.
/// <see cref="CardCatalog"/> registers one instance per id and the engine treats
/// each model as a flyweight. Per-instance upgrade level lives on
/// <c>CardInstance.UpgradeLevel</c>, not here.
/// </para>
/// </summary>
public abstract class CardModel : ICardModel
{
    /// <summary>
    /// Stable string id used by <see cref="CardCatalog"/> and the Q4 token map. Must be
    /// unique across all cards. Subclasses pass this to the base constructor.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Energy cost to play the card. Matches upstream <c>CanonicalEnergyCost</c>.
    /// </summary>
    public int Cost { get; }

    /// <summary>
    /// B.1-gamma-T5: true for X-cost cards (Skewer / Malaise). Upstream's
    /// <c>HasEnergyCostX</c> flag. When true, the engine consumes ALL the
    /// player's available energy on play and snapshots the spent amount to
    /// <see cref="Sts2Headless.Domain.Combat.TrailCounters.LastSpentEnergy"/>
    /// before invoking the card's <see cref="OnPlay"/>. Default false.
    /// </summary>
    public virtual bool IsXCost => false;

    /// <summary>Card type (attack / skill / power / status / curse / quest).</summary>
    public CardType Type { get; }

    /// <summary>Rarity (basic / common / uncommon / rare / etc.).</summary>
    public CardRarity Rarity { get; }

    /// <summary>Required target shape (self / single-enemy / all-enemies / etc.).</summary>
    public TargetType Target { get; }

    /// <summary>
    /// Tags (functional family markers — Strike, Defend, Shiv, etc.). Empty by default;
    /// subclasses override <see cref="DeclareTags"/>.
    /// </summary>
    public IReadOnlySet<CardTag> Tags { get; }

    /// <summary>
    /// Construct with a canonical configuration. Upstream-shape signature: id +
    /// (energy, type, rarity, target). Subclasses provide their id as a string literal
    /// matching upstream's <c>ModelId.Entry</c>.
    /// </summary>
    /// <param name="id">Stable id (e.g., "strike_silent").</param>
    /// <param name="cost">Energy cost (e.g., 1 for basic strike).</param>
    /// <param name="type">Card type.</param>
    /// <param name="rarity">Card rarity.</param>
    /// <param name="target">Target requirement.</param>
    protected CardModel(string id, int cost, CardType type, CardRarity rarity, TargetType target)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new System.ArgumentException("CardModel id must be non-empty.", nameof(id));
        }
        Id = id;
        Cost = cost;
        Type = type;
        Rarity = rarity;
        Target = target;
        // Materialise the subclass's declared tag set once. Concrete subclasses build a
        // fresh HashSet each call to DeclareTags; we wrap it read-only so the canonical
        // instance can't be mutated by callers.
        HashSet<CardTag> tags = new();
        DeclareTags(tags);
        Tags = tags;
    }

    /// <summary>
    /// Hook for subclasses to declare card tags. Default is none. Subclasses override and
    /// <c>Add</c> tags into the provided set. Called once during construction.
    /// </summary>
    protected virtual void DeclareTags(HashSet<CardTag> tags) { }

    /// <summary>
    /// Apply the card's effect. Concrete subclasses enqueue damage / block / draw /
    /// power-application actions on <paramref name="ctx"/>'s queue, or fire hooks.
    /// <paramref name="target"/> identifies the chosen target (null for self-target /
    /// no-target cards, otherwise an opaque creature handle interpreted by the action
    /// implementations).
    ///
    /// <para>
    /// Implementations MUST NOT mutate state directly — they enqueue actions. The
    /// <see cref="ExecutionContext.Queue"/> drain loop in S4 processes them in order.
    /// </para>
    /// </summary>
    /// <param name="ctx">Active execution context.</param>
    /// <param name="target">
    /// Optional target id (null for self-target / random-target / no-target cards).
    /// String for now; S6 may replace with a richer creature reference.
    /// </param>
    public abstract void OnPlay(ExecutionContext ctx, string? target);
}

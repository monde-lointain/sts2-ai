using System.Collections.Immutable;
using Sts2Headless.Domain.Determinism;

namespace Sts2Headless.Domain.Combat;

/// <summary>
/// An ordered, immutable collection of <see cref="CardInstance"/> records.
/// Backs the draw / hand / discard / exhaust piles inside <see cref="CombatState"/>.
/// All operations return a new <see cref="CardPile"/> — never mutate in place.
///
/// <para>
/// <b>Ordering contract:</b> the order of <see cref="Cards"/> is the source of
/// truth for the pile's enumeration. For draw pile, index 0 = top of deck; for
/// discard / hand / exhaust, index 0 = first-added. Operations like
/// <see cref="DrawTop"/> reflect this — drawing removes index 0.
/// </para>
///
/// <para>
/// <b>Cheap-clone friendly:</b> <see cref="ImmutableList{T}"/> uses structural
/// sharing; <c>Add</c>/<c>Remove</c> are O(log N) but most-common-case copying is
/// shared with parent, so a record <c>with</c>-clone of a CombatState containing
/// many piles allocates minimal new memory.
/// </para>
///
/// <para>
/// <b>State-codec friendly:</b> S7 serializes <c>Cards</c> as length-prefixed
/// sequence of <c>CardInstance</c> records in their stored order.
/// </para>
/// </summary>
public sealed record CardPile(ImmutableList<CardInstance> Cards)
{
    /// <summary>The canonical empty pile. Use as a starting point for builders.</summary>
    public static CardPile Empty { get; } = new(ImmutableList<CardInstance>.Empty);

    /// <summary>Number of cards in this pile.</summary>
    public int Count => Cards.Count;

    /// <summary>True iff <see cref="Count"/> is zero.</summary>
    public bool IsEmpty => Cards.IsEmpty;

    /// <summary>
    /// Construct a new pile from a sequence of cards, preserving order. The first
    /// element becomes the top of the pile (index 0).
    /// </summary>
    public static CardPile OfRange(IEnumerable<CardInstance> cards)
    {
        System.ArgumentNullException.ThrowIfNull(cards);
        return new(ImmutableList.CreateRange(cards));
    }

    /// <summary>
    /// Return a new pile with <paramref name="card"/> appended at the end (bottom
    /// for draw piles; tail for discard / hand / exhaust). Use
    /// <see cref="AddTop"/> for top-of-deck inserts.
    /// </summary>
    public CardPile Add(CardInstance card)
    {
        System.ArgumentNullException.ThrowIfNull(card);
        return new(Cards.Add(card));
    }

    /// <summary>
    /// Return a new pile with <paramref name="card"/> prepended at index 0 (top
    /// of the draw pile). Used for innate / forced-top-of-deck effects.
    /// </summary>
    public CardPile AddTop(CardInstance card)
    {
        System.ArgumentNullException.ThrowIfNull(card);
        return new(Cards.Insert(0, card));
    }

    /// <summary>
    /// Draw the top card (index 0). Returns the remaining pile and the drawn card.
    /// Throws <see cref="InvalidOperationException"/> if the pile is empty —
    /// callers must reshuffle from discard before drawing if the draw pile is
    /// empty (combat layer responsibility, not the pile's).
    /// </summary>
    public (CardPile RemainingPile, CardInstance Drawn) DrawTop()
    {
        if (Cards.IsEmpty)
        {
            throw new InvalidOperationException("Cannot draw from an empty pile.");
        }
        CardInstance drawn = Cards[0];
        return (new CardPile(Cards.RemoveAt(0)), drawn);
    }

    /// <summary>
    /// Return a new pile with the card matching <paramref name="instanceId"/>
    /// removed. Throws <see cref="InvalidOperationException"/> if not found.
    /// </summary>
    public CardPile Remove(uint instanceId)
    {
        int index = -1;
        for (int i = 0; i < Cards.Count; i++)
        {
            if (Cards[i].InstanceId == instanceId)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"CardPile.Remove: no card with InstanceId={instanceId}."
            );
        }
        return new(Cards.RemoveAt(index));
    }

    /// <summary>True iff this pile contains a card with the given instance id.</summary>
    public bool Contains(uint instanceId)
    {
        for (int i = 0; i < Cards.Count; i++)
        {
            if (Cards[i].InstanceId == instanceId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Return a new pile with cards in deterministic shuffled order driven by
    /// <paramref name="rng"/>. Uses the same Fisher-Yates pass as
    /// <see cref="IRngSource.Shuffle{T}(IList{T})"/> so the same seed produces
    /// the same permutation as upstream.
    ///
    /// <para>
    /// We build a fresh mutable list to feed <see cref="IRngSource.Shuffle{T}"/>
    /// (which mutates), then wrap the result in an ImmutableList. The mutation
    /// is scoped to this local — no shared state is touched.
    /// </para>
    /// </summary>
    public CardPile Shuffle(IRngSource rng)
    {
        System.ArgumentNullException.ThrowIfNull(rng);
        if (Cards.IsEmpty)
            return this;
        List<CardInstance> scratch = new(Cards);
        rng.Shuffle(scratch);
        return new(ImmutableList.CreateRange(scratch));
    }

    /// <summary>
    /// Equality comparison via record-default semantics, but ImmutableList
    /// equality is reference-based by default. We override here so two piles with
    /// the same sequence of cards compare equal — required for CombatState
    /// equality and the canonical-hash byte serialization.
    /// </summary>
    public bool Equals(CardPile? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (Cards.Count != other.Cards.Count)
            return false;
        for (int i = 0; i < Cards.Count; i++)
        {
            if (!Cards[i].Equals(other.Cards[i]))
                return false;
        }
        return true;
    }

    /// <summary>Override required when <see cref="Equals(CardPile?)"/> is overridden.</summary>
    public override int GetHashCode()
    {
        HashCode h = default;
        for (int i = 0; i < Cards.Count; i++)
        {
            h.Add(Cards[i]);
        }
        return h.ToHashCode();
    }
}

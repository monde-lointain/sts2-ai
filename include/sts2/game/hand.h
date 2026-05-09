#pragma once

#include <cassert>
#include <cstddef>
#include <span>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/index_types.h"

namespace sts2::game {

class Deck;
class Rng;

class Hand {
 public:
  static constexpr int kMaxSize = 10;

  // Appends c; no-op if already at kMaxSize.
  void add(Card c);

  // Removes card at idx and returns it (play semantics). Asserts valid(idx).
  [[nodiscard]] Card play(HandIndex idx);

  // Same operation as play; exists for semantic clarity at call sites.
  [[nodiscard]] Card discard_at(HandIndex idx);

  // Moves all cards into deck.discard(), back-to-front (LIFO order).
  void dump_into(Deck& deck);

  // Draws up to n cards from deck, stopping at kMaxSize or deck exhaustion.
  void draw_from(Deck& deck, Rng& rng, int n);

  // Returns HandIndex of the first card with the given id, or HandIndex::none().
  [[nodiscard]] HandIndex find(CardId id) const noexcept;

  [[nodiscard]] bool valid(HandIndex idx) const noexcept;
  [[nodiscard]] bool empty() const noexcept { return cards_.empty(); }
  [[nodiscard]] std::size_t size() const noexcept { return cards_.size(); }

  // Bounds-checked read-only access. Asserts valid(idx).
  [[nodiscard]] const Card& at(HandIndex idx) const noexcept;

  // Read-only view for test inspection (mirrors Deck::draw_pile()).
  [[nodiscard]] std::span<const Card> cards() const noexcept { return cards_; }

 private:
  // Shared pop logic for play() and discard_at().
  [[nodiscard]] Card pop_at(HandIndex idx);

  std::vector<Card> cards_;
};

}  // namespace sts2::game

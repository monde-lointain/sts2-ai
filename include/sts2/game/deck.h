#pragma once

#include <cstddef>
#include <optional>
#include <span>
#include <vector>

#include "sts2/game/card.h"

namespace sts2::game {

class Rng;

class Deck {
 public:
  // Clears state, loads draw_pile from deck, then shuffles.
  void load_starter(std::vector<Card> deck, Rng& rng);

  // Pops from draw_pile back. Reshuffles discard->draw if draw empty.
  // Returns nullopt if both piles are empty after potential reshuffle.
  [[nodiscard]] std::optional<Card> draw_one(Rng& rng);

  void discard(Card c);
  void reshuffle(Rng& rng);  // discard -> draw, then shuffle draw

  [[nodiscard]] std::size_t draw_size() const noexcept;
  [[nodiscard]] std::size_t discard_size() const noexcept;
  [[nodiscard]] std::size_t total_size() const noexcept;  // draw + discard

  // Read-only views for test inspection.
  [[nodiscard]] std::span<const Card> draw_pile() const noexcept;
  [[nodiscard]] std::span<const Card> discard_pile() const noexcept;

 private:
  std::vector<Card> draw_pile_;
  std::vector<Card> discard_pile_;
};

}  // namespace sts2::game

#include "sts2/game/deck.h"

#include <optional>
#include <span>
#include <utility>

#include "sts2/game/rng.h"

namespace sts2::game {

void Deck::load_starter(std::vector<Card> deck, Rng& rng) {
  draw_pile_ = std::move(deck);
  discard_pile_.clear();
  rng.shuffle(draw_pile_);
}

std::optional<Card> Deck::draw_one(Rng& rng) {
  if (draw_pile_.empty()) {
    reshuffle(rng);
  }
  if (draw_pile_.empty()) {
    return std::nullopt;
  }
  Card c = std::move(draw_pile_.back());
  draw_pile_.pop_back();
  return c;
}

void Deck::discard(Card c) { discard_pile_.push_back(std::move(c)); }

void Deck::reshuffle(Rng& rng) {
  while (!discard_pile_.empty()) {
    draw_pile_.push_back(std::move(discard_pile_.back()));
    discard_pile_.pop_back();
  }
  rng.shuffle(draw_pile_);
}

std::size_t Deck::draw_size() const noexcept { return draw_pile_.size(); }
std::size_t Deck::discard_size() const noexcept { return discard_pile_.size(); }
std::size_t Deck::total_size() const noexcept {
  return draw_pile_.size() + discard_pile_.size();
}

std::span<const Card> Deck::draw_pile() const noexcept { return draw_pile_; }
std::span<const Card> Deck::discard_pile() const noexcept {
  return discard_pile_;
}

}  // namespace sts2::game

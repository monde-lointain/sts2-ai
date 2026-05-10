#include "sts2/game/hand.h"

#include <cassert>
#include <utility>

#include "sts2/game/deck.h"
#include "sts2/game/rng.h"

namespace sts2::game {

void Hand::add(Card c) {
  if (static_cast<int>(cards_.size()) >= kMaxSize) {
    return;
  }
  cards_.push_back(std::move(c));
}

Card Hand::pop_at(HandIndex idx) {
  assert(valid(idx));
  const auto i = static_cast<std::size_t>(idx.raw());
  Card out = std::move(cards_[i]);
  cards_.erase(cards_.begin() + static_cast<std::ptrdiff_t>(i));
  return out;
}

Card Hand::play(HandIndex idx) { return pop_at(idx); }

Card Hand::discard_at(HandIndex idx) { return pop_at(idx); }

void Hand::dump_into(Deck& deck) {
  while (!cards_.empty()) {
    deck.discard(std::move(cards_.back()));
    cards_.pop_back();
  }
}

void Hand::draw_from(Deck& deck, Rng& rng, int n) {
  for (int i = 0; i < n; ++i) {
    if (static_cast<int>(cards_.size()) >= kMaxSize) {
      return;
    }
    auto card = deck.draw_one(rng);
    if (!card) {
      return;
    }
    cards_.push_back(std::move(*card));
  }
}

HandIndex Hand::find(CardId id) const noexcept {
  for (std::size_t i = 0; i < cards_.size(); ++i) {
    if (cards_[i].id == id) {
      return HandIndex{static_cast<int>(i)};
    }
  }
  return HandIndex::none();
}

bool Hand::valid(HandIndex idx) const noexcept { return idx.in_range(cards_); }

const Card& Hand::at(HandIndex idx) const noexcept {
  assert(valid(idx));
  return idx.at(cards_);
}

}  // namespace sts2::game

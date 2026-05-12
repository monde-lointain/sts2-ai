#pragma once

#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <iterator>

#include "sts2/game/card_effects.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"

namespace sts2::game {
class Combat;
}

namespace sts2::ai {

namespace transition::detail {
class StateMutator;
}

namespace detail {
constexpr bool counted_card_ids_are_ordered() noexcept {
  for (std::size_t i = 0; i < sts2::game::card_effects::kCountedCardIds.size();
       ++i) {
    if (static_cast<int>(sts2::game::card_effects::kCountedCardIds[i]) !=
        static_cast<int>(i) + 1) {
      return false;
    }
  }
  return true;
}
}  // namespace detail

struct CardCounts {
  std::array<uint8_t, sts2::game::card_effects::kCountedCardIds.size()>
      counts{};
  static_assert(std::size(sts2::game::card_effects::kCountedCardIds) <= 8,
                "pack_counts uint64 packing limit (8 bits per slot)");

  // to_index() relies on kCountedCardIds being ordered to match CardId enum
  // layout (kNone=0 implicit; entry i is CardId{i+1}). Without this, indexing
  // silently misaligns when a new CardId is added at the wrong enum position.
  static_assert(detail::counted_card_ids_are_ordered(),
                "kCountedCardIds order must match CardId enum "
                "(kNone=0, kStrike=1, ...)");

  // CardId is 1-indexed; kNone=0 is an assert-only sentinel, so we offset by 1
  // to map kStrike..kSurvivor onto array indices 0..3.
  static constexpr std::size_t to_index(sts2::game::CardId id) noexcept {
    assert(id != sts2::game::CardId::kNone);
    return static_cast<std::size_t>(id) - 1;
  }

  uint8_t& operator[](sts2::game::CardId id) noexcept {
    return counts[to_index(id)];
  }
  uint8_t operator[](sts2::game::CardId id) const noexcept {
    return counts[to_index(id)];
  }

  CardCounts& operator+=(const CardCounts& o) noexcept;
  CardCounts& operator-=(const CardCounts& o) noexcept;
  friend CardCounts operator+(CardCounts a, const CardCounts& b) noexcept {
    return a += b;
  }

  [[nodiscard]] bool covers(const CardCounts& subset) const noexcept;
  [[nodiscard]] int total() const noexcept;
  bool operator==(const CardCounts&) const = default;
};

class EnemyState {
 public:
  [[nodiscard]] sts2::game::Stat get_hp() const noexcept { return hp_; }
  [[nodiscard]] sts2::game::Stat get_block() const noexcept { return block_; }
  [[nodiscard]] sts2::game::Stat get_strength() const noexcept {
    return strength_;
  }
  [[nodiscard]] sts2::game::Stat get_weak() const noexcept { return weak_; }
  [[nodiscard]] sts2::game::Stat get_dark_strike_base() const noexcept {
    return dark_strike_base_;
  }
  [[nodiscard]] sts2::game::Stat get_ritual_amount() const noexcept {
    return ritual_amount_;
  }
  [[nodiscard]] bool get_just_applied_ritual() const noexcept {
    return just_applied_ritual_;
  }
  [[nodiscard]] bool get_performed_first_move() const noexcept {
    return performed_first_move_;
  }
  [[nodiscard]] sts2::game::MoveId get_current_move() const noexcept {
    return current_move_;
  }
  [[nodiscard]] bool get_alive() const noexcept { return alive_; }

  bool operator==(const EnemyState&) const = default;

 private:
  friend class EnemyStateBuilder;
  friend class transition::detail::StateMutator;

  sts2::game::Stat hp_;
  sts2::game::Stat block_;
  sts2::game::Stat strength_;
  sts2::game::Stat weak_;
  sts2::game::Stat dark_strike_base_;
  sts2::game::Stat ritual_amount_;
  bool just_applied_ritual_ = false;
  bool performed_first_move_ = false;
  sts2::game::MoveId current_move_ = sts2::game::MoveId::kIncantation;
  bool alive_ = false;
};

class EnemyStateBuilder {
 public:
  explicit EnemyStateBuilder(EnemyState state = {}) noexcept : state_(state) {}

  EnemyStateBuilder& hp(sts2::game::Stat value) noexcept {
    state_.hp_ = value;
    return *this;
  }
  EnemyStateBuilder& block(sts2::game::Stat value) noexcept {
    state_.block_ = value;
    return *this;
  }
  EnemyStateBuilder& strength(sts2::game::Stat value) noexcept {
    state_.strength_ = value;
    return *this;
  }
  EnemyStateBuilder& weak(sts2::game::Stat value) noexcept {
    state_.weak_ = value;
    return *this;
  }
  EnemyStateBuilder& dark_strike_base(sts2::game::Stat value) noexcept {
    state_.dark_strike_base_ = value;
    return *this;
  }
  EnemyStateBuilder& ritual_amount(sts2::game::Stat value) noexcept {
    state_.ritual_amount_ = value;
    return *this;
  }
  EnemyStateBuilder& just_applied_ritual(bool value) noexcept {
    state_.just_applied_ritual_ = value;
    return *this;
  }
  EnemyStateBuilder& performed_first_move(bool value) noexcept {
    state_.performed_first_move_ = value;
    return *this;
  }
  EnemyStateBuilder& current_move(sts2::game::MoveId value) noexcept {
    state_.current_move_ = value;
    return *this;
  }
  EnemyStateBuilder& alive(bool value) noexcept {
    state_.alive_ = value;
    return *this;
  }

  [[nodiscard]] EnemyState build() const noexcept { return state_; }

 private:
  EnemyState state_{};
};

[[nodiscard]] inline bool is_alive(const EnemyState& e) noexcept {
  return e.get_alive();
}

enum class Phase : uint8_t { kPlayerActing, kAtChanceDraw };

class CompactState {
 public:
  [[nodiscard]] sts2::game::Stat get_player_hp() const noexcept {
    return player_hp_;
  }
  [[nodiscard]] sts2::game::Stat get_player_block() const noexcept {
    return player_block_;
  }
  [[nodiscard]] sts2::game::Stat get_player_strength() const noexcept {
    return player_strength_;
  }
  [[nodiscard]] sts2::game::Stat get_player_weak() const noexcept {
    return player_weak_;
  }
  [[nodiscard]] sts2::game::Stat get_energy() const noexcept { return energy_; }
  [[nodiscard]] uint16_t get_round() const noexcept { return round_; }
  [[nodiscard]] Phase get_phase() const noexcept { return phase_; }
  [[nodiscard]] const std::array<EnemyState, 2>& get_enemies() const noexcept {
    return enemies_;
  }
  [[nodiscard]] const EnemyState& get_enemy(std::size_t index) const noexcept {
    assert(index < enemies_.size());
    return enemies_[index];
  }
  [[nodiscard]] const CardCounts& get_hand() const noexcept { return hand_; }
  [[nodiscard]] const CardCounts& get_draw() const noexcept { return draw_; }
  [[nodiscard]] const CardCounts& get_discard() const noexcept {
    return discard_;
  }

  bool operator==(const CompactState&) const = default;

 private:
  friend class CompactStateBuilder;
  friend class transition::detail::StateMutator;

  sts2::game::Stat player_hp_;
  sts2::game::Stat player_block_;
  sts2::game::Stat player_strength_;
  sts2::game::Stat player_weak_;
  sts2::game::Stat energy_;
  uint16_t round_ = 1;
  Phase phase_ = Phase::kPlayerActing;
  std::array<EnemyState, 2> enemies_{};
  CardCounts hand_{};
  CardCounts draw_{};
  CardCounts discard_{};
};

class CompactStateBuilder {
 public:
  explicit CompactStateBuilder(CompactState state = {}) noexcept
      : state_(state) {}

  CompactStateBuilder& player_hp(sts2::game::Stat value) noexcept {
    state_.player_hp_ = value;
    return *this;
  }
  CompactStateBuilder& player_block(sts2::game::Stat value) noexcept {
    state_.player_block_ = value;
    return *this;
  }
  CompactStateBuilder& player_strength(sts2::game::Stat value) noexcept {
    state_.player_strength_ = value;
    return *this;
  }
  CompactStateBuilder& player_weak(sts2::game::Stat value) noexcept {
    state_.player_weak_ = value;
    return *this;
  }
  CompactStateBuilder& energy(sts2::game::Stat value) noexcept {
    state_.energy_ = value;
    return *this;
  }
  CompactStateBuilder& round(uint16_t value) noexcept {
    state_.round_ = value;
    return *this;
  }
  CompactStateBuilder& phase(Phase value) noexcept {
    state_.phase_ = value;
    return *this;
  }
  CompactStateBuilder& enemy(std::size_t index, EnemyState value) noexcept {
    assert(index < state_.enemies_.size());
    state_.enemies_[index] = value;
    return *this;
  }
  CompactStateBuilder& hand(CardCounts value) noexcept {
    state_.hand_ = value;
    return *this;
  }
  CompactStateBuilder& draw(CardCounts value) noexcept {
    state_.draw_ = value;
    return *this;
  }
  CompactStateBuilder& discard(CardCounts value) noexcept {
    state_.discard_ = value;
    return *this;
  }

  [[nodiscard]] CompactState build() const noexcept { return state_; }

 private:
  CompactState state_{};
};

CompactState from_combat(const sts2::game::Combat& combat);

}  // namespace sts2::ai

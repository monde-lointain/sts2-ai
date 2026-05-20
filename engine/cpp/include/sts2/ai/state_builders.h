#pragma once

#include "sts2/ai/state.h"  // EnemyState, CompactState, Phase, CardCounts, kMaxEnemies, PowerKind

namespace sts2::ai {

// ---------------------------------------------------------------------------
// EnemyStateBuilder
// ---------------------------------------------------------------------------
class EnemyStateBuilder {
 public:
  // cppcheck-suppress passedByValueCallback
  explicit EnemyStateBuilder(EnemyState state = {}) noexcept : state_(state) {}

  EnemyStateBuilder& hp(sts2::game::Stat value) noexcept {
    state_.hp_ = value;
    return *this;
  }
  EnemyStateBuilder& block(sts2::game::Stat value) noexcept {
    state_.block_ = value;
    return *this;
  }
  // strength/weak: route through PowerArray (nonzero → set, zero → remove)
  EnemyStateBuilder& strength(sts2::game::Stat value) noexcept {
    if (value.value() != 0) {
      state_.powers_.set(sts2::game::PowerKind::kStrength, value.value());
    } else {
      state_.powers_.remove(sts2::game::PowerKind::kStrength);
    }
    return *this;
  }
  EnemyStateBuilder& weak(sts2::game::Stat value) noexcept {
    if (value.value() != 0) {
      state_.powers_.set(sts2::game::PowerKind::kWeak, value.value());
    } else {
      state_.powers_.remove(sts2::game::PowerKind::kWeak);
    }
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
  // New builders (wave-17+):
  EnemyStateBuilder& kind(sts2::game::MonsterKind value) noexcept {
    state_.kind_ = value;
    return *this;
  }
  EnemyStateBuilder& move_index(uint8_t value) noexcept {
    state_.move_index_ = value;
    return *this;
  }
  // Wave-18: generic power setter for powers not covered by typed builders.
  EnemyStateBuilder& add_power(sts2::game::PowerKind k,
                               int32_t stacks) noexcept {
    state_.powers_.add(k, stacks);
    return *this;
  }

  // cppcheck-suppress returnByReference -- builder is often a temporary;
  // returning const& would dangle when called on a temporary builder.
  [[nodiscard]] EnemyState build() const noexcept { return state_; }

 private:
  EnemyState state_{};
};

// ---------------------------------------------------------------------------
// CompactStateBuilder
// ---------------------------------------------------------------------------
class CompactStateBuilder {
 public:
  // cppcheck-suppress passedByValueCallback
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
  // player_strength / player_weak: route through PowerArray
  CompactStateBuilder& player_strength(sts2::game::Stat value) noexcept {
    if (value.value() != 0) {
      state_.player_powers_.set(sts2::game::PowerKind::kStrength,
                                value.value());
    } else {
      state_.player_powers_.remove(sts2::game::PowerKind::kStrength);
    }
    return *this;
  }
  CompactStateBuilder& player_weak(sts2::game::Stat value) noexcept {
    if (value.value() != 0) {
      state_.player_powers_.set(sts2::game::PowerKind::kWeak, value.value());
    } else {
      state_.player_powers_.remove(sts2::game::PowerKind::kWeak);
    }
    return *this;
  }
  CompactStateBuilder& energy(sts2::game::Stat value) noexcept {
    state_.energy_ = value;
    return *this;
  }
  // Wave-23/J.beta: round widened uint16_t → int32_t (Q2-ADR-014).
  CompactStateBuilder& round(int32_t value) noexcept {
    state_.round_ = value;
    return *this;
  }
  CompactStateBuilder& phase(Phase value) noexcept {
    state_.phase_ = value;
    return *this;
  }
  CompactStateBuilder& enemy(std::size_t index,
                             const EnemyState& value) noexcept {
    assert(index < state_.enemies_.size());
    state_.enemies_[index] = value;
    // Update enemy_count_ to cover this slot
    if (index >= state_.enemy_count_) {
      state_.enemy_count_ = static_cast<uint8_t>(index + 1);
    }
    return *this;
  }
  CompactStateBuilder& enemy_count(uint8_t value) noexcept {
    state_.enemy_count_ = value;
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

  // cppcheck-suppress returnByReference -- builder is often a temporary;
  // returning const& would dangle when called on a temporary builder.
  [[nodiscard]] CompactState build() const noexcept { return state_; }

 private:
  CompactState state_{};
};

}  // namespace sts2::ai

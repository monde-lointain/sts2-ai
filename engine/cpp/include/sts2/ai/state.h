#pragma once

#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <iterator>
#include <optional>

#include "sts2/game/card_effects.h"
#include "sts2/game/monster_moves.h"
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

// ---------------------------------------------------------------------------
// PowerInstance — generic per-creature power entry (wave-17+).
// POD; 4 bytes; stable layout (static_assert below).
// ---------------------------------------------------------------------------
struct PowerInstance {
  sts2::game::PowerKind kind = sts2::game::PowerKind::kWeak;
  int16_t stacks = 0;
  // bit 0: just_applied (used by Ritual to skip strength grant on spawn turn)
  uint8_t flags = 0;
  uint8_t _pad = 0;

  bool operator==(const PowerInstance&) const = default;
};
// NOTE: PowerKind is int-backed (4 bytes), so sizeof(PowerInstance) = 8.
// Wave-17 note in monster_moves.h suggests migrating PowerKind to uint8_t in a
// future wave to shrink this to 4 bytes; deferred.
static_assert(sizeof(PowerInstance) == 8);

// Max powers stored per creature (EnemyState or player slot in CompactState).
// Sized for Phase-1 monsters; cultist uses 1 (Ritual), louse uses 1 (CurlUp).
constexpr uint8_t kMaxPowersPerCreature =
    sts2::game::monster_moves::kMaxSpawnPowers * 2;  // = 6

// Max enemies in a CompactState.
constexpr uint8_t kMaxEnemies = 4;

// ---------------------------------------------------------------------------
// Power helpers (find/add/set flags)
// ---------------------------------------------------------------------------
namespace powers {

[[nodiscard]] inline PowerInstance* find_power(
    std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t count,
    sts2::game::PowerKind kind) noexcept {
  for (uint8_t i = 0; i < count; ++i) {
    if (arr[i].kind == kind) {
      return &arr[i];
    }
  }
  return nullptr;
}

[[nodiscard]] inline const PowerInstance* find_power(
    const std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t count,
    sts2::game::PowerKind kind) noexcept {
  for (uint8_t i = 0; i < count; ++i) {
    if (arr[i].kind == kind) {
      return &arr[i];
    }
  }
  return nullptr;
}

// Add stacks to an existing power, or insert a new entry. Returns ref.
inline PowerInstance& add_power(
    std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t& count,
    sts2::game::PowerKind kind, int16_t stacks) noexcept {
  PowerInstance* existing = find_power(arr, count, kind);
  if (existing != nullptr) {
    existing->stacks = static_cast<int16_t>(existing->stacks + stacks);
    return *existing;
  }
  assert(count < kMaxPowersPerCreature && "powers array full");
  arr[count] = PowerInstance{kind, stacks, 0, 0};
  return arr[count++];
}

// Set a power to an exact stack value (replaces existing or inserts).
inline PowerInstance& set_power(
    std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t& count,
    sts2::game::PowerKind kind, int16_t stacks) noexcept {
  PowerInstance* existing = find_power(arr, count, kind);
  if (existing != nullptr) {
    existing->stacks = stacks;
    return *existing;
  }
  assert(count < kMaxPowersPerCreature && "powers array full");
  arr[count] = PowerInstance{kind, stacks, 0, 0};
  return arr[count++];
}

// Return stack count for kind, or 0 if absent.
[[nodiscard]] inline int16_t stacks_of(
    const std::array<PowerInstance, kMaxPowersPerCreature>& arr, uint8_t count,
    sts2::game::PowerKind kind) noexcept {
  const PowerInstance* p = find_power(arr, count, kind);
  return (p != nullptr) ? p->stacks : 0;
}

}  // namespace powers

// ---------------------------------------------------------------------------
// CardCounts
// ---------------------------------------------------------------------------
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

// ---------------------------------------------------------------------------
// EnemyState — polymorphic enemy state (wave-17 shape)
//
// Shape change from wave-16:
//   - strength_, weak_, just_applied_ritual_ → powers_ array (PowerInstance)
//   - kind_, move_index_ added
//   - dark_strike_base_, ritual_amount_, current_move_ kept as scalars to
//     avoid excessive test call-site churn (may be removed in a future wave)
//
// Existing accessor functions preserved as wrappers so callers compile
// unchanged.
// ---------------------------------------------------------------------------
class EnemyState {
 public:
  // ---- Existing accessors (preserved; some now route through powers_) ----
  [[nodiscard]] sts2::game::Stat get_hp() const noexcept { return hp_; }
  [[nodiscard]] sts2::game::Stat get_block() const noexcept { return block_; }

  [[nodiscard]] sts2::game::Stat get_strength() const noexcept {
    return sts2::game::Stat{powers::stacks_of(
        powers_, power_count_, sts2::game::PowerKind::kStrength)};
  }
  [[nodiscard]] sts2::game::Stat get_weak() const noexcept {
    return sts2::game::Stat{
        powers::stacks_of(powers_, power_count_, sts2::game::PowerKind::kWeak)};
  }

  // dark_strike_base and ritual_amount remain scalar fields for call-site
  // compatibility with tests that construct synthetic enemies.
  [[nodiscard]] sts2::game::Stat get_dark_strike_base() const noexcept {
    return dark_strike_base_;
  }
  [[nodiscard]] sts2::game::Stat get_ritual_amount() const noexcept {
    return ritual_amount_;
  }

  [[nodiscard]] bool get_just_applied_ritual() const noexcept {
    const PowerInstance* p = powers::find_power(powers_, power_count_,
                                                sts2::game::PowerKind::kRitual);
    return (p != nullptr) && ((p->flags & 0x01U) != 0);
  }
  [[nodiscard]] bool get_performed_first_move() const noexcept {
    return performed_first_move_;
  }
  [[nodiscard]] sts2::game::MoveId get_current_move() const noexcept {
    return current_move_;
  }
  [[nodiscard]] bool get_alive() const noexcept { return alive_; }

  // ---- New accessors (wave-17+) ----
  [[nodiscard]] sts2::game::MonsterKind get_kind() const noexcept {
    return kind_;
  }
  [[nodiscard]] uint8_t get_move_index() const noexcept { return move_index_; }
  [[nodiscard]] uint8_t get_power_count() const noexcept {
    return power_count_;
  }
  [[nodiscard]] const std::array<PowerInstance, kMaxPowersPerCreature>&
  get_powers() const noexcept {
    return powers_;
  }

  bool operator==(const EnemyState&) const = default;

 private:
  friend class EnemyStateBuilder;
  friend class transition::detail::StateMutator;

  sts2::game::Stat hp_;
  sts2::game::Stat block_;
  // scalar fields kept for test call-site compatibility:
  sts2::game::Stat dark_strike_base_;
  sts2::game::Stat ritual_amount_;
  bool performed_first_move_ = false;
  sts2::game::MoveId current_move_ = sts2::game::MoveId::kIncantation;
  bool alive_ = false;

  // New polymorphic shape:
  sts2::game::MonsterKind kind_ = sts2::game::MonsterKind::kCultistCalcified;
  uint8_t move_index_ = 0;
  uint8_t power_count_ = 0;
  uint8_t _pad = 0;
  std::array<PowerInstance, kMaxPowersPerCreature> powers_{};
};

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
  // strength/weak: now set into powers_ array
  EnemyStateBuilder& strength(sts2::game::Stat value) noexcept {
    if (value.value() != 0) {
      powers::set_power(state_.powers_, state_.power_count_,
                        sts2::game::PowerKind::kStrength,
                        static_cast<int16_t>(value.value()));
    } else {
      // Zero strength: remove from array if present
      for (uint8_t i = 0; i < state_.power_count_; ++i) {
        if (state_.powers_[i].kind == sts2::game::PowerKind::kStrength) {
          // Shift remaining entries left
          for (uint8_t j = i; j + 1 < state_.power_count_; ++j) {
            state_.powers_[j] = state_.powers_[j + 1];
          }
          --state_.power_count_;
          state_.powers_[state_.power_count_] = {};
          break;
        }
      }
    }
    return *this;
  }
  EnemyStateBuilder& weak(sts2::game::Stat value) noexcept {
    if (value.value() != 0) {
      powers::set_power(state_.powers_, state_.power_count_,
                        sts2::game::PowerKind::kWeak,
                        static_cast<int16_t>(value.value()));
    } else {
      for (uint8_t i = 0; i < state_.power_count_; ++i) {
        if (state_.powers_[i].kind == sts2::game::PowerKind::kWeak) {
          for (uint8_t j = i; j + 1 < state_.power_count_; ++j) {
            state_.powers_[j] = state_.powers_[j + 1];
          }
          --state_.power_count_;
          state_.powers_[state_.power_count_] = {};
          break;
        }
      }
    }
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
  // just_applied_ritual: sets flag on kRitual PowerInstance (insert if absent)
  EnemyStateBuilder& just_applied_ritual(bool value) noexcept {
    PowerInstance* p = powers::find_power(state_.powers_, state_.power_count_,
                                          sts2::game::PowerKind::kRitual);
    if (value) {
      if (p == nullptr) {
        // Insert a Ritual entry with 0 stacks but just_applied flag set.
        // The actual ritual_amount stays in ritual_amount_ scalar.
        p = &powers::add_power(state_.powers_, state_.power_count_,
                               sts2::game::PowerKind::kRitual, 0);
      }
      p->flags |= 0x01U;
    } else {
      if (p != nullptr) {
        p->flags &= static_cast<uint8_t>(~0x01U);
      }
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

  // cppcheck-suppress returnByReference -- builder is often a temporary;
  // returning const& would dangle when called on a temporary builder.
  [[nodiscard]] EnemyState build() const noexcept { return state_; }

 private:
  EnemyState state_{};
};

[[nodiscard]] inline bool is_alive(const EnemyState& e) noexcept {
  return e.get_alive();
}

enum class Phase : uint8_t { kPlayerActing, kAtChanceDraw };

// ---------------------------------------------------------------------------
// CompactState — wave-17 shape
//
// Shape change from pre-wave-17:
//   - player_strength_, player_weak_ → player_powers_ array
//   - enemies_ widened from 2 to kMaxEnemies=4; enemy_count_ added
//   - get_enemies() still returns const array ref (kMaxEnemies); only
//     entries [0..enemy_count_-1] are valid. Callers using .size() will get
//     kMaxEnemies (4); transition.cc uses enemy_count() op which reads the
//     field.
// ---------------------------------------------------------------------------
class CompactState {
 public:
  [[nodiscard]] sts2::game::Stat get_player_hp() const noexcept {
    return player_hp_;
  }
  [[nodiscard]] sts2::game::Stat get_player_block() const noexcept {
    return player_block_;
  }
  // player_strength / player_weak: route through player_powers_
  [[nodiscard]] sts2::game::Stat get_player_strength() const noexcept {
    return sts2::game::Stat{powers::stacks_of(
        player_powers_, player_power_count_, sts2::game::PowerKind::kStrength)};
  }
  [[nodiscard]] sts2::game::Stat get_player_weak() const noexcept {
    return sts2::game::Stat{powers::stacks_of(
        player_powers_, player_power_count_, sts2::game::PowerKind::kWeak)};
  }
  [[nodiscard]] sts2::game::Stat get_energy() const noexcept { return energy_; }
  [[nodiscard]] uint16_t get_round() const noexcept { return round_; }
  [[nodiscard]] Phase get_phase() const noexcept { return phase_; }
  // Returns the full kMaxEnemies array; valid entries are [0..enemy_count_-1].
  [[nodiscard]] const std::array<EnemyState, kMaxEnemies>& get_enemies()
      const noexcept {
    return enemies_;
  }
  [[nodiscard]] const EnemyState& get_enemy(std::size_t index) const noexcept {
    assert(index < enemies_.size());
    return enemies_[index];
  }
  [[nodiscard]] uint8_t get_enemy_count() const noexcept {
    return enemy_count_;
  }
  [[nodiscard]] const CardCounts& get_hand() const noexcept { return hand_; }
  [[nodiscard]] const CardCounts& get_draw() const noexcept { return draw_; }
  [[nodiscard]] const CardCounts& get_discard() const noexcept {
    return discard_;
  }
  // Player powers (wave-17+)
  [[nodiscard]] uint8_t get_player_power_count() const noexcept {
    return player_power_count_;
  }
  [[nodiscard]] const std::array<PowerInstance, kMaxPowersPerCreature>&
  get_player_powers() const noexcept {
    return player_powers_;
  }

  bool operator==(const CompactState&) const = default;

 private:
  friend class CompactStateBuilder;
  friend class transition::detail::StateMutator;

  sts2::game::Stat player_hp_;
  sts2::game::Stat player_block_;
  // player_strength_ and player_weak_ replaced by player_powers_:
  std::array<PowerInstance, kMaxPowersPerCreature> player_powers_{};
  uint8_t player_power_count_ = 0;
  sts2::game::Stat energy_;
  uint16_t round_ = 1;
  Phase phase_ = Phase::kPlayerActing;
  uint8_t enemy_count_ = 0;
  std::array<EnemyState, kMaxEnemies> enemies_{};
  CardCounts hand_{};
  CardCounts draw_{};
  CardCounts discard_{};
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
  // player_strength / player_weak: route through player_powers_
  CompactStateBuilder& player_strength(sts2::game::Stat value) noexcept {
    if (value.value() != 0) {
      powers::set_power(state_.player_powers_, state_.player_power_count_,
                        sts2::game::PowerKind::kStrength,
                        static_cast<int16_t>(value.value()));
    } else {
      for (uint8_t i = 0; i < state_.player_power_count_; ++i) {
        if (state_.player_powers_[i].kind == sts2::game::PowerKind::kStrength) {
          for (uint8_t j = i; j + 1 < state_.player_power_count_; ++j) {
            state_.player_powers_[j] = state_.player_powers_[j + 1];
          }
          --state_.player_power_count_;
          state_.player_powers_[state_.player_power_count_] = {};
          break;
        }
      }
    }
    return *this;
  }
  CompactStateBuilder& player_weak(sts2::game::Stat value) noexcept {
    if (value.value() != 0) {
      powers::set_power(state_.player_powers_, state_.player_power_count_,
                        sts2::game::PowerKind::kWeak,
                        static_cast<int16_t>(value.value()));
    } else {
      for (uint8_t i = 0; i < state_.player_power_count_; ++i) {
        if (state_.player_powers_[i].kind == sts2::game::PowerKind::kWeak) {
          for (uint8_t j = i; j + 1 < state_.player_power_count_; ++j) {
            state_.player_powers_[j] = state_.player_powers_[j + 1];
          }
          --state_.player_power_count_;
          state_.player_powers_[state_.player_power_count_] = {};
          break;
        }
      }
    }
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

CompactState from_combat(const sts2::game::Combat& combat);

}  // namespace sts2::ai

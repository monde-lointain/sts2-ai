#pragma once

#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <iterator>
#include <optional>

#include "sts2/ai/power_array.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/damage_calc.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"

namespace sts2::game {
class Combat;
}

namespace sts2::ai {

// Forward declarations so friend class lines compile before builder
// definitions.
class EnemyStateBuilder;
class CompactStateBuilder;

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
// CardCounts
//
// Wave-23/J.beta: per-slot count widened uint8_t → int32_t to match upstream
// STS2's uniform int storage (Q2-ADR-014). pack_counts static_assert removed
// (no consumers found; the prior uint64 packing helper never materialized as
// a function or callsite in src/ or tests/). The Zobrist card_counts hash
// table widens its per-slot count range accordingly (kMaxCountPerCardZone
// 16 → 64).
// ---------------------------------------------------------------------------
struct CardCounts {
  std::array<int32_t, sts2::game::card_effects::kCountedCardIds.size()>
      counts{};

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

  int32_t& operator[](sts2::game::CardId id) noexcept {
    return counts[to_index(id)];
  }
  int32_t operator[](sts2::game::CardId id) const noexcept {
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
//   - current_move_ kept as scalar for intent tracking
//   - dark_strike_base_, ritual_amount_ removed (wave-35/B.2-β; ADR-031):
//     now sourced via cultist_dark_strike_base/cultist_ritual_amount helpers
//     in transition.cc, indexed from kMonsterMoveTables[kind].
// ---------------------------------------------------------------------------
class EnemyState {
 public:
  // ---- Existing accessors (preserved; some now route through powers_) ----
  [[nodiscard]] sts2::game::Stat get_hp() const noexcept { return hp_; }
  [[nodiscard]] sts2::game::Stat get_block() const noexcept { return block_; }

  [[nodiscard]] sts2::game::Stat get_strength() const noexcept {
    return sts2::game::Stat{
        powers_.stacks_of(sts2::game::PowerKind::kStrength)};
  }
  [[nodiscard]] sts2::game::Stat get_weak() const noexcept {
    return sts2::game::Stat{powers_.stacks_of(sts2::game::PowerKind::kWeak)};
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
    return powers_.count();
  }
  [[nodiscard]] const std::array<PowerInstance, kMaxPowersPerCreature>&
  get_powers() const noexcept {
    return powers_.data();
  }
  [[nodiscard]] const PowerArray& powers() const noexcept { return powers_; }

  // ---- Mutation helpers for OracleTarget (wave-28/C.1) ----
  // Takes a POST-formula block value (caller computes via
  // damage::compute_outgoing_block).
  void add_block_amount(int32_t v) noexcept { block_ += v; }
  void add_power(sts2::game::PowerKind kind, int32_t stacks) noexcept {
    powers_.add(kind, stacks);
  }

  // ---- Power-management — delegate to PowerArray (wave-30/A) ----
  void decrement_power(sts2::game::PowerKind kind) noexcept {
    powers_.decrement(kind);
  }
  void remove_power(sts2::game::PowerKind kind) noexcept {
    powers_.remove(kind);
  }
  // ---- Typed public mutators (wave-31/B: StateMutator deletion) ----
  void set_block(sts2::game::Stat value) noexcept { block_ = value; }
  void set_performed_first_move(bool value) noexcept {
    performed_first_move_ = value;
  }
  void set_alive(bool value) noexcept { alive_ = value; }
  void advance_intent(
      const sts2::game::monster_moves::MonsterMoveTable& table) noexcept {
    sts2::game::move_calc::advance_intent_table(
        performed_first_move_, current_move_, move_index_, table);
  }
  // Mutable PowerArray access (used for curl_up_card stamp in
  // apply_player_action).
  PowerArray& powers_mut() noexcept { return powers_; }
  // Mutable hp/block refs for apply_to_defender (damage path).
  sts2::game::Stat& hp_mut() noexcept { return hp_; }
  sts2::game::Stat& block_mut() noexcept { return block_; }

  bool operator==(const EnemyState&) const = default;

 private:
  friend class EnemyStateBuilder;

  sts2::game::Stat hp_;
  sts2::game::Stat block_;
  bool performed_first_move_ = false;
  sts2::game::MoveId current_move_ = sts2::game::MoveId::kIncantation;
  bool alive_ = false;

  // kind_ default = kCultistCalcified is LOAD-BEARING post-wave-35/B.2-β:
  // cultist transition.cc helpers (cultist_dark_strike_base /
  // cultist_ritual_amount) index kMonsterMoveTables[kind]. Tests that
  // construct EnemyState without an explicit .kind() call silently inherit
  // the Calcified default; the DefaultKindIsCalcifiedCultist regression test
  // guards this. See ADR-031.
  sts2::game::MonsterKind kind_ = sts2::game::MonsterKind::kCultistCalcified;
  uint8_t move_index_ = 0;
  uint8_t _pad = 0;
  uint8_t _pad2 = 0;
  PowerArray powers_{};
};

[[nodiscard]] inline bool is_alive(const EnemyState& e) noexcept {
  return e.get_alive();
}

// Phase: kPlayerActing=0, kAtChanceDraw=1 preserved from original definition
// (zobrist.cc depends on these positions for cultist byte-identity pin).
// Wave-22.α APPENDED kAtEnemyMoveRng=2 — chance node between
// resolve_end_turn_pre_draw and the draw step, materialized when at least
// one alive enemy's current move has a RandomBranch follow-up (slime POKEY
// / GOOP cycles). Cultist + LouseProgenitor moves use kStrict follow-ups and
// SKIP this phase entirely → cultist Zobrist hash unchanged.
enum class Phase : uint8_t {
  kPlayerActing = 0,
  kAtChanceDraw = 1,
  kAtEnemyMoveRng = 2,  // wave-22.α
};

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
    return sts2::game::Stat{
        player_powers_.stacks_of(sts2::game::PowerKind::kStrength)};
  }
  [[nodiscard]] sts2::game::Stat get_player_weak() const noexcept {
    return sts2::game::Stat{
        player_powers_.stacks_of(sts2::game::PowerKind::kWeak)};
  }
  [[nodiscard]] sts2::game::Stat get_energy() const noexcept { return energy_; }
  // Wave-23/J.beta: round_ widened uint16_t → int32_t (Q2-ADR-014).
  [[nodiscard]] int32_t get_round() const noexcept { return round_; }
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
    return player_powers_.count();
  }
  [[nodiscard]] const std::array<PowerInstance, kMaxPowersPerCreature>&
  get_player_powers() const noexcept {
    return player_powers_.data();
  }
  [[nodiscard]] const PowerArray& player_powers() const noexcept {
    return player_powers_;
  }

  // ---- Mutation helpers for OracleTarget (wave-28/C.1) ----
  void apply_to_player(int32_t dmg) noexcept {
    (void)sts2::damage::apply_to_defender(player_hp_, player_block_, dmg);
  }
  void add_player_frail(int32_t v) noexcept {
    if (v != 0) {
      player_powers_.add(sts2::game::PowerKind::kFrail, v);
    }
  }
  void add_player_weak(int32_t v) noexcept {
    if (v != 0) {
      player_powers_.add(sts2::game::PowerKind::kWeak, v);
    }
  }
  // No Phase-1 consumer; exists for snapshot discipline.
  void add_player_vulnerable(int32_t v) noexcept {
    if (v != 0) {
      player_powers_.add(sts2::game::PowerKind::kVulnerable, v);
    }
  }
  void add_player_discard_slimed(int32_t count) noexcept {
    for (int32_t i = 0; i < count; ++i) {
      if (discard_[sts2::game::CardId::kSlimed] <
          sts2::game::card_effects::kMaxSlimedAccumulation) {
        ++discard_[sts2::game::CardId::kSlimed];
      }
    }
  }

  // ---- Power-management — delegate to PowerArray (wave-30/A) ----
  void decrement_player_power(sts2::game::PowerKind kind) noexcept {
    player_powers_.decrement(kind);
  }
  [[nodiscard]] int32_t get_player_frail() const noexcept {
    return player_powers_.stacks_of(sts2::game::PowerKind::kFrail);
  }

  // ---- Typed public mutators (wave-31/B: StateMutator deletion) ----
  [[nodiscard]] EnemyState& get_enemy_mut(std::size_t i) noexcept {
    assert(i < enemies_.size());
    return enemies_[i];
  }
  void set_phase(Phase value) noexcept { phase_ = value; }
  void set_energy(sts2::game::Stat value) noexcept { energy_ = value; }
  void sub_energy(sts2::game::Stat value) noexcept { energy_ -= value.value(); }
  void set_round(int32_t value) noexcept { round_ = value; }
  void set_player_block(sts2::game::Stat value) noexcept {
    player_block_ = value;
  }
  void add_player_block(sts2::game::Stat value) noexcept {
    player_block_ += value.value();
  }
  void remove_one_from_hand(sts2::game::CardId id) noexcept { --hand_[id]; }
  void add_one_to_discard(sts2::game::CardId id) noexcept { ++discard_[id]; }
  void clear_hand() noexcept { hand_ = CardCounts{}; }
  void move_hand_to_discard() noexcept {
    discard_ += hand_;
    hand_ = CardCounts{};
  }
  void reshuffle_discard_into_draw() noexcept {
    draw_ += discard_;
    discard_ = CardCounts{};
  }
  void apply_draw_from_pile(const CardCounts& drawn) noexcept {
    hand_ += drawn;
    draw_ -= drawn;
  }
  // Mutable player hp/block refs for apply_to_defender (damage path).
  sts2::game::Stat& player_hp_mut() noexcept { return player_hp_; }
  sts2::game::Stat& player_block_mut() noexcept { return player_block_; }

  bool operator==(const CompactState&) const = default;

 private:
  friend class CompactStateBuilder;

  sts2::game::Stat player_hp_;
  sts2::game::Stat player_block_;
  // player_strength_ and player_weak_ replaced by player_powers_:
  PowerArray player_powers_{};
  sts2::game::Stat energy_;
  int32_t round_ = 1;
  Phase phase_ = Phase::kPlayerActing;
  uint8_t enemy_count_ = 0;
  std::array<EnemyState, kMaxEnemies> enemies_{};
  CardCounts hand_{};
  CardCounts draw_{};
  CardCounts discard_{};
};

CompactState from_combat(const sts2::game::Combat& combat);

}  // namespace sts2::ai

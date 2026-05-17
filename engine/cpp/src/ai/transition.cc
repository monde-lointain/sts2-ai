#include "sts2/ai/transition.h"

#include <algorithm>
#include <cassert>

#include "sts2/game/card_effects.h"
#include "sts2/game/damage_calc.h"
#include "sts2/game/index_types.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/stat.h"
#include "sts2/game/turn_calc.h"
#include "sts2/game/turn_flow.h"

namespace sts2::ai::transition {

namespace detail {

class StateMutator {
 public:
  [[nodiscard]] static sts2::game::Stat& hp(EnemyState& e) noexcept {
    return e.hp_;
  }
  [[nodiscard]] static sts2::game::Stat& block(EnemyState& e) noexcept {
    return e.block_;
  }
  // strength/weak now route through the powers_ array
  static void add_strength(EnemyState& e, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(e.powers_, e.power_count_,
                      sts2::game::PowerKind::kStrength,
                      static_cast<int16_t>(delta));
  }
  static void add_weak(EnemyState& e, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(e.powers_, e.power_count_, sts2::game::PowerKind::kWeak,
                      static_cast<int16_t>(delta));
  }
  static void decrement_weak(EnemyState& e) noexcept {
    PowerInstance* p = powers::find_power(e.powers_, e.power_count_,
                                          sts2::game::PowerKind::kWeak);
    if (p == nullptr) {
      return;
    }
    --p->stacks;
    if (p->stacks <= 0) {
      // Remove from array
      for (uint8_t i = 0; i < e.power_count_; ++i) {
        if (e.powers_[i].kind == sts2::game::PowerKind::kWeak) {
          for (uint8_t j = i; j + 1 < e.power_count_; ++j) {
            e.powers_[j] = e.powers_[j + 1];
          }
          --e.power_count_;
          e.powers_[e.power_count_] = {};
          break;
        }
      }
    }
  }
  // Ritual flag manipulation
  [[nodiscard]] static bool get_just_applied_ritual(
      const EnemyState& e) noexcept {
    const PowerInstance* p = powers::find_power(e.powers_, e.power_count_,
                                                sts2::game::PowerKind::kRitual);
    return (p != nullptr) && ((p->flags & 0x01U) != 0);
  }
  static void set_just_applied_ritual(EnemyState& e, bool value) noexcept {
    PowerInstance* p = powers::find_power(e.powers_, e.power_count_,
                                          sts2::game::PowerKind::kRitual);
    if (value) {
      if (p == nullptr) {
        p = &powers::add_power(e.powers_, e.power_count_,
                               sts2::game::PowerKind::kRitual, 0);
      }
      p->flags |= 0x01U;
    } else {
      if (p != nullptr) {
        p->flags &= static_cast<uint8_t>(~0x01U);
      }
    }
  }
  static void clear_just_applied_ritual(EnemyState& e) noexcept {
    // Clear the just_applied flag. If the resulting PowerInstance has stacks=0
    // and no flags set, remove it so from_combat comparison stays consistent
    // (from_combat only inserts kRitual when just_applied=true).
    PowerInstance* p = powers::find_power(e.powers_, e.power_count_,
                                          sts2::game::PowerKind::kRitual);
    if (p == nullptr) {
      return;
    }
    p->flags &= static_cast<uint8_t>(~0x01U);
    if (p->stacks == 0 && p->flags == 0) {
      // Remove the now-empty Ritual entry
      for (uint8_t i = 0; i < e.power_count_; ++i) {
        if (e.powers_[i].kind == sts2::game::PowerKind::kRitual) {
          for (uint8_t j = i; j + 1 < e.power_count_; ++j) {
            e.powers_[j] = e.powers_[j + 1];
          }
          --e.power_count_;
          e.powers_[e.power_count_] = {};
          break;
        }
      }
    }
  }

  [[nodiscard]] static sts2::game::Stat& dark_strike_base(
      EnemyState& e) noexcept {
    return e.dark_strike_base_;
  }
  [[nodiscard]] static sts2::game::Stat& ritual_amount(EnemyState& e) noexcept {
    return e.ritual_amount_;
  }
  [[nodiscard]] static bool& performed_first_move(EnemyState& e) noexcept {
    return e.performed_first_move_;
  }
  [[nodiscard]] static sts2::game::MoveId& current_move(
      EnemyState& e) noexcept {
    return e.current_move_;
  }
  [[nodiscard]] static bool& alive(EnemyState& e) noexcept { return e.alive_; }

  [[nodiscard]] static sts2::game::Stat& player_hp(CompactState& s) noexcept {
    return s.player_hp_;
  }
  [[nodiscard]] static sts2::game::Stat& player_block(
      CompactState& s) noexcept {
    return s.player_block_;
  }
  // player_strength / player_weak: route through player_powers_
  static void add_player_strength(CompactState& s, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(s.player_powers_, s.player_power_count_,
                      sts2::game::PowerKind::kStrength,
                      static_cast<int16_t>(delta));
  }
  static void add_player_weak(CompactState& s, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(s.player_powers_, s.player_power_count_,
                      sts2::game::PowerKind::kWeak,
                      static_cast<int16_t>(delta));
  }
  [[nodiscard]] static sts2::game::Stat& energy(CompactState& s) noexcept {
    return s.energy_;
  }
  [[nodiscard]] static uint16_t& round(CompactState& s) noexcept {
    return s.round_;
  }
  [[nodiscard]] static Phase& phase(CompactState& s) noexcept {
    return s.phase_;
  }
  [[nodiscard]] static std::array<EnemyState, kMaxEnemies>& enemies(
      CompactState& s) noexcept {
    return s.enemies_;
  }
  [[nodiscard]] static uint8_t& enemy_count(CompactState& s) noexcept {
    return s.enemy_count_;
  }
  [[nodiscard]] static CardCounts& hand(CompactState& s) noexcept {
    return s.hand_;
  }
  [[nodiscard]] static CardCounts& draw(CompactState& s) noexcept {
    return s.draw_;
  }
  [[nodiscard]] static CardCounts& discard(CompactState& s) noexcept {
    return s.discard_;
  }
};

}  // namespace detail

namespace {

using sts2::game::CardId;
using sts2::game::EnemySlot;
using sts2::game::TargetType;
using sts2::game::card_effects::card_effect_for;
using sts2::game::card_effects::kCountedCardIds;
using M = detail::StateMutator;

void damage_enemy(EnemyState& enemy, int strength, int weak, int base) {
  const int dmg = sts2::damage::compute_outgoing(base, strength, weak);
  (void)sts2::damage::apply_to_defender(M::hp(enemy), M::block(enemy), dmg);
  if (enemy.get_hp() == sts2::game::Stat{0}) {
    M::alive(enemy) = false;
  }
}

void enemy_act(CompactState& s, EnemyState& e);
void enemy_tick_powers(EnemyState& e);
void roll_next_move(EnemyState& e);

bool apply_player_action_in_place(CompactState& state, const Action& action) {
  assert(state.get_phase() == Phase::kPlayerActing);

  if (action.kind == ActionKind::kEndTurn) {
    M::phase(state) = Phase::kAtChanceDraw;
    return true;
  }

  assert(action.card_id != CardId::kNone);
  const CardId id = action.card_id;
  const auto& fx = card_effect_for(id);
  if (fx.cost > state.get_energy().value()) {
    return false;
  }
  if (state.get_hand()[id] == 0) {
    return false;
  }

  if (fx.target == TargetType::kAnyEnemy) {
    if (!action.target_idx.in_range(state.get_enemies())) {
      return false;
    }
    auto slot = action.target_idx;
    if (!slot.at(state.get_enemies()).get_alive()) {
      return false;
    }
  }

  --M::hand(state)[id];
  M::energy(state) -= fx.cost;

  if (fx.base_damage) {
    EnemyState& e = action.target_idx.at(M::enemies(state));
    damage_enemy(e, state.get_player_strength().value(),
                 state.get_player_weak().value(), fx.base_damage);
  }
  if (fx.base_block) {
    M::player_block(state) += fx.base_block;
  }
  if (fx.weak_to_target) {
    EnemyState& e = action.target_idx.at(M::enemies(state));
    M::add_weak(e, fx.weak_to_target);
  }
  if (fx.requires_discard) {
    if (state.get_hand().total() == 0) {
      if (action.survivor_discard_id != CardId::kNone) {
        return false;
      }
    } else {
      if (action.survivor_discard_id != CardId::kNone &&
          state.get_hand()[action.survivor_discard_id] == 0) {
        return false;
      }
      if (action.survivor_discard_id != CardId::kNone) {
        --M::hand(state)[action.survivor_discard_id];
        ++M::discard(state)[action.survivor_discard_id];
      }
    }
  }

  ++M::discard(state)[id];
  return true;
}

void resolve_end_turn_pre_draw_in_place(CompactState& state) {
  assert(state.get_phase() == Phase::kAtChanceDraw);

  struct EndTurnOps {
    // NOLINTBEGIN(cppcoreguidelines-avoid-const-or-ref-data-members)
    // Local helper struct, never assigned; reference member is intentional.
    CompactState& state;
    // NOLINTEND(cppcoreguidelines-avoid-const-or-ref-data-members)

    void end_player_turn() {
      // Player power tick is a no-op in v1.
      M::discard(state) += state.get_hand();
      M::hand(state) = CardCounts{};
    }
    [[nodiscard]] std::size_t enemy_count() const {
      return state.get_enemy_count();
    }
    [[nodiscard]] bool enemy_alive(std::size_t slot) const {
      return is_alive(state.get_enemy(slot));
    }
    void reset_enemy_block(std::size_t slot) {
      M::block(M::enemies(state)[slot]) = sts2::game::Stat{0};
    }
    void enemy_act(std::size_t slot) {
      transition::enemy_act(state, M::enemies(state)[slot]);
    }
    [[nodiscard]] bool terminal() const {
      return state.get_player_hp() == sts2::game::Stat{0};
    }
    void tick_enemy_powers(std::size_t slot) {
      enemy_tick_powers(M::enemies(state)[slot]);
    }
    void increment_round() {
      M::round(state) = static_cast<uint16_t>(state.get_round() + 1);
    }
    [[nodiscard]] int round() const { return state.get_round(); }
    void roll_enemy_next_move(std::size_t slot) {
      roll_next_move(M::enemies(state)[slot]);
    }
    void reset_player_block() { M::player_block(state) = sts2::game::Stat{0}; }
    void refill_player_energy(int amount) {
      M::energy(state) = sts2::game::Stat{amount};
    }
  };

  EndTurnOps ops{state};
  sts2::game::turn_flow::resolve_end_turn_pre_draw(ops);
  // Phase already kAtChanceDraw; the draw step is the chance node.
}

void apply_draw_in_place(CompactState& state, CardCounts drawn) {
  assert(state.get_phase() == Phase::kAtChanceDraw);
  assert(drawn.total() <= 10);

  // Reshuffle if the draw pile alone can't satisfy the request. Engine drains
  // pre-reshuffle cards first then post-reshuffle; for multiset purposes the
  // unioned outcome is identical, so a single up-front reshuffle is sound.
  if (!state.get_draw().covers(drawn)) {
    M::draw(state) += state.get_discard();
    M::discard(state) = CardCounts{};
  }

  assert(state.get_draw().covers(drawn));

  M::hand(state) += drawn;
  M::draw(state) -= drawn;

  M::phase(state) = Phase::kPlayerActing;
}

}  // namespace

std::vector<Action> legal_actions(const CompactState& state) {
  assert(state.get_phase() == Phase::kPlayerActing);

  std::vector<Action> actions;

  for (CardId id : kCountedCardIds) {
    if (state.get_hand()[id] == 0) {
      continue;
    }
    const auto& fx = card_effect_for(id);
    if (fx.cost > state.get_energy().value()) {
      continue;
    }

    const TargetType tgt = fx.target;
    if (tgt == TargetType::kAnyEnemy) {
      for (uint8_t i = 0; i < state.get_enemy_count(); ++i) {
        if (!state.get_enemy(i).get_alive()) {
          continue;
        }
        Action a;
        a.kind = ActionKind::kPlayCard;
        a.card_id = id;
        a.target_idx = EnemySlot{static_cast<int>(i)};
        actions.push_back(a);
      }
    } else if (fx.requires_discard) {
      CardCounts post = state.get_hand();
      assert(post[CardId::kSurvivor] > 0);
      --post[CardId::kSurvivor];
      if (post.total() == 0) {
        Action a;
        a.kind = ActionKind::kPlayCard;
        a.card_id = CardId::kSurvivor;
        a.target_idx = EnemySlot::none();
        a.survivor_discard_id = CardId::kNone;
        actions.push_back(a);
      } else {
        for (CardId other : kCountedCardIds) {
          if (other == CardId::kSurvivor) {
            continue;
          }
          if (post[other] == 0) {
            continue;
          }
          Action a;
          a.kind = ActionKind::kPlayCard;
          a.card_id = CardId::kSurvivor;
          a.target_idx = EnemySlot::none();
          a.survivor_discard_id = other;
          actions.push_back(a);
        }
      }
    } else {
      Action a;
      a.kind = ActionKind::kPlayCard;
      a.card_id = id;
      a.target_idx = EnemySlot::none();
      actions.push_back(a);
    }
  }

  Action end;
  end.kind = ActionKind::kEndTurn;
  actions.push_back(end);
  return actions;
}

std::optional<CompactState> apply_player_action(const CompactState& state,
                                                const Action& action) {
  CompactState next = state;
  if (!apply_player_action_in_place(next, action)) {
    return std::nullopt;
  }
  return next;
}

namespace {

void enemy_act(CompactState& s, EnemyState& e) {
  sts2::game::move_calc::act_on_intent(
      e.get_current_move(),
      [&]() {
        // Mirrors powers::apply for kRitual: amount accumulates on the Power,
        // but in v1 Ritual is applied once -> Power.amount stays at
        // ritual_amount. We model the dynamic Ritual state purely via
        // just_applied flag on the kRitual PowerInstance.
        M::set_just_applied_ritual(e, true);
      },
      [&]() {
        const int dmg = sts2::damage::compute_outgoing(
            e.get_dark_strike_base().value(), e.get_strength().value(),
            e.get_weak().value());
        (void)sts2::damage::apply_to_defender(M::player_hp(s),
                                              M::player_block(s), dmg);
      });
}

void enemy_tick_powers(EnemyState& e) {
  // ritual_should_grant_strength: if just_applied is true, clears it and
  // returns false (skip strength this turn). If false, returns true (grant).
  bool just_applied = M::get_just_applied_ritual(e);
  if (just_applied) {
    // Spawn-turn: skip strength grant, clear the just_applied flag.
    M::clear_just_applied_ritual(e);
  } else {
    // Subsequent turns: grant Ritual → Strength.
    M::add_strength(e, e.get_ritual_amount().value());
  }
  if (e.get_weak() > sts2::game::Stat{0}) {
    M::decrement_weak(e);
  }
}

void roll_next_move(EnemyState& e) {
  sts2::game::move_calc::advance_intent(M::performed_first_move(e),
                                        M::current_move(e));
}

}  // namespace

bool is_terminal(const CompactState& s) noexcept {
  if (s.get_player_hp() == sts2::game::Stat{0}) {
    return true;
  }
  const auto& enemies = s.get_enemies();
  const uint8_t count = s.get_enemy_count();
  for (uint8_t i = 0; i < count; ++i) {
    if (enemies[i].get_alive()) {
      return false;
    }
  }
  return true;
}

int draw_count(const CompactState& s) noexcept {
  return sts2::game::turn_calc::hand_draw_size(s.get_round());
}

CompactState resolve_end_turn_pre_draw(const CompactState& state) {
  CompactState next = state;
  resolve_end_turn_pre_draw_in_place(next);
  return next;
}

CompactState apply_draw(const CompactState& state, CardCounts drawn) {
  CompactState next = state;
  apply_draw_in_place(next, drawn);
  return next;
}

}  // namespace sts2::ai::transition

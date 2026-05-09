#include "sts2/ai/transition.h"

#include <algorithm>
#include <cassert>

#include "sts2/game/card_effects.h"
#include "sts2/game/combat.h"
#include "sts2/game/damage_calc.h"
#include "sts2/game/index_types.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/stat.h"
#include "sts2/game/turn_calc.h"

namespace sts2::ai::transition {

namespace {

using sts2::game::CardId;
using sts2::game::EnemySlot;
using sts2::game::TargetType;
using sts2::game::card_effects::card_effect_for;
using sts2::game::card_effects::kCountedCardIds;

// Adapter: bridge Stat hp/block fields to the canonical int& overload in
// sts2::damage.
void apply_damage(sts2::game::Stat& hp, sts2::game::Stat& block, int incoming) {
  int hp_i = hp.value();
  int block_i = block.value();
  (void)sts2::damage::apply_to_defender(hp_i, block_i, incoming);
  hp = sts2::game::Stat{hp_i};
  block = sts2::game::Stat{block_i};
}

void damage_enemy(EnemyState& enemy, int strength, int weak, int base) {
  const int dmg = sts2::damage::compute_outgoing(base, strength, weak);
  apply_damage(enemy.hp, enemy.block, dmg);
  if (enemy.hp == sts2::game::Stat{0}) {
    enemy.alive = false;
  }
}

}  // namespace

std::vector<Action> legal_actions(const CompactState& state) {
  assert(state.phase == Phase::kPlayerActing);

  std::vector<Action> actions;

  for (CardId id : kCountedCardIds) {
    if (state.hand[id] == 0) continue;
    const auto& fx = card_effect_for(id);
    if (fx.cost > state.energy.value()) continue;

    const TargetType tgt = fx.target;
    if (tgt == TargetType::kAnyEnemy) {
      for (uint8_t i = 0; i < 2; ++i) {
        if (!state.enemies[i].alive) continue;
        Action a;
        a.kind = ActionKind::kPlayCard;
        a.card_id = id;
        a.target_idx = EnemySlot{static_cast<int>(i)};
        actions.push_back(a);
      }
    } else if (fx.requires_discard) {
      CardCounts post = state.hand;
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
          if (other == CardId::kSurvivor) continue;
          if (post[other] == 0) continue;
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

bool apply_player_action(CompactState& state, const Action& action) {
  assert(state.phase == Phase::kPlayerActing);

  if (action.kind == ActionKind::kEndTurn) {
    state.phase = Phase::kAtChanceDraw;
    return true;
  }

  assert(action.card_id != CardId::kNone);
  const CardId id = action.card_id;
  const auto& fx = card_effect_for(id);
  if (fx.cost > state.energy.value()) return false;
  if (state.hand[id] == 0) return false;

  if (fx.target == TargetType::kAnyEnemy) {
    if (!action.target_idx.in_range(state.enemies)) return false;
    auto slot = action.target_idx;
    if (!slot.at(state.enemies).alive) return false;
  }

  --state.hand[id];
  state.energy -= fx.cost;

  if (fx.base_damage) {
    EnemyState& e = action.target_idx.at(state.enemies);
    damage_enemy(e, state.player_strength.value(), state.player_weak.value(), fx.base_damage);
  }
  if (fx.base_block) {
    state.player_block += fx.base_block;
  }
  if (fx.weak_to_target) {
    EnemyState& e = action.target_idx.at(state.enemies);
    e.weak += fx.weak_to_target;
  }
  if (fx.requires_discard) {
    if (state.hand.total() == 0) {
      assert(action.survivor_discard_id == CardId::kNone);
    } else if (action.survivor_discard_id != CardId::kNone) {
      assert(state.hand[action.survivor_discard_id] > 0);
      --state.hand[action.survivor_discard_id];
      ++state.discard[action.survivor_discard_id];
    }
  }

  ++state.discard[id];
  return true;
}

namespace {

void enemy_act(CompactState& s, EnemyState& e) {
  sts2::game::move_calc::act_on_intent(
      e.current_move,
      [&]() {
        // Mirrors powers::apply for kRitual: amount accumulates on the Power,
        // but in v1 Ritual is applied once -> Power.amount stays at ritual_amount.
        // We model the dynamic Ritual state purely via just_applied_ritual.
        e.just_applied_ritual = true;
      },
      [&]() {
        const int dmg = sts2::damage::compute_outgoing(
            e.dark_strike_base.value(), e.strength.value(), e.weak.value());
        apply_damage(s.player_hp, s.player_block, dmg);
      });
}

void enemy_tick_powers(EnemyState& e) {
  if (sts2::game::move_calc::ritual_should_grant_strength(
          e.just_applied_ritual)) {
    e.strength += e.ritual_amount.value();
  }
  if (e.weak > sts2::game::Stat{0}) {
    e.weak -= 1;
  }
}

void roll_next_move(EnemyState& e) {
  sts2::game::move_calc::advance_intent(e.performed_first_move, e.current_move);
}

}  // namespace

bool is_terminal(const CompactState& s) noexcept {
  if (s.player_hp == sts2::game::Stat{0}) return true;
  return std::all_of(s.enemies.begin(), s.enemies.end(),
                     [](const EnemyState& e) { return !e.alive; });
}

int draw_count(const CompactState& s) noexcept {
  return sts2::game::turn_calc::hand_draw_size(s.round);
}

void resolve_end_turn_pre_draw(CompactState& state) {
  assert(state.phase == Phase::kAtChanceDraw);

  // end_player_turn: hand -> discard. Player power tick is a no-op in v1
  // (no Ritual on player; Weak hard-asserted 0 by from_combat).
  state.discard += state.hand;
  state.hand = CardCounts{};

  // enemy_phase: zero block on alive enemies, then act in slot order; bail
  // early if player dies mid-phase (mirrors Combat::enemy_phase combat_over_).
  for (auto& e : state.enemies) {
    if (e.alive) e.block = sts2::game::Stat{0};
  }
  for (auto& e : state.enemies) {
    if (!e.alive) continue;
    enemy_act(state, e);
    if (state.player_hp == sts2::game::Stat{0}) return;
  }
  // Tick AFTER all acts.
  for (auto& e : state.enemies) {
    if (e.alive) enemy_tick_powers(e);
  }

  state.round = static_cast<uint16_t>(state.round + 1);

  for (auto& e : state.enemies) {
    if (e.alive) roll_next_move(e);
  }

  if (sts2::game::turn_calc::round_resets_block(state.round)) {
    state.player_block = sts2::game::Stat{0};
  }
  state.energy = sts2::game::Stat{sts2::game::turn_calc::starting_energy()};
  // Phase already kAtChanceDraw; the draw step is the chance node.
}

void apply_draw(CompactState& state, CardCounts drawn) {
  assert(state.phase == Phase::kAtChanceDraw);
  assert(drawn.total() <= 10);

  // Reshuffle if the draw pile alone can't satisfy the request. Engine drains
  // pre-reshuffle cards first then post-reshuffle; for multiset purposes the
  // unioned outcome is identical, so a single up-front reshuffle is sound.
  if (!state.draw.covers(drawn)) {
    state.draw += state.discard;
    state.discard = CardCounts{};
  }

  assert(state.draw.covers(drawn));

  state.hand += drawn;
  state.draw -= drawn;

  state.phase = Phase::kPlayerActing;
}

}  // namespace sts2::ai::transition

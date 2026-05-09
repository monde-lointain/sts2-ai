#include "sts2/ai/transition.h"

#include <algorithm>
#include <cassert>
#include <cstdint>

#include "sts2/game/damage_calc.h"

namespace sts2::ai::transition {

namespace {

using sts2::game::CardId;
using sts2::game::TargetType;

constexpr CardId kAllCards[] = {CardId::kStrike, CardId::kDefend,
                                CardId::kNeutralize, CardId::kSurvivor};

// Adapter: bridge uint8_t hp/block fields to the canonical int& overload in
// sts2::damage.
void apply_damage_u8(uint8_t& hp, uint8_t& block, int incoming) {
  int hp_i = hp;
  int block_i = block;
  sts2::damage::apply_to_defender(hp_i, block_i, incoming);
  hp = static_cast<uint8_t>(hp_i);
  block = static_cast<uint8_t>(block_i);
}

int card_cost(CardId id) {
  switch (id) {
    case CardId::kStrike:
      return 1;
    case CardId::kDefend:
      return 1;
    case CardId::kNeutralize:
      return 0;
    case CardId::kSurvivor:
      return 1;
    case CardId::kNone:
      break;
  }
  assert(false && "card_cost: invalid CardId");
  return 0;
}

TargetType card_target_kind(CardId id) {
  switch (id) {
    case CardId::kStrike:
      return TargetType::kAnyEnemy;
    case CardId::kDefend:
      return TargetType::kSelf;
    case CardId::kNeutralize:
      return TargetType::kAnyEnemy;
    case CardId::kSurvivor:
      return TargetType::kSelf;
    case CardId::kNone:
      break;
  }
  assert(false && "card_target_kind: invalid CardId");
  return TargetType::kNoTarget;
}

uint8_t count_in_hand(const CardCounts& hand, CardId id) {
  switch (id) {
    case CardId::kStrike:
      return hand.strike;
    case CardId::kDefend:
      return hand.defend;
    case CardId::kNeutralize:
      return hand.neutralize;
    case CardId::kSurvivor:
      return hand.survivor;
    case CardId::kNone:
      break;
  }
  assert(false && "count_in_hand: invalid CardId");
  return 0;
}

void dec_count(CardCounts& counts, CardId id) {
  switch (id) {
    case CardId::kStrike:
      assert(counts.strike > 0);
      --counts.strike;
      return;
    case CardId::kDefend:
      assert(counts.defend > 0);
      --counts.defend;
      return;
    case CardId::kNeutralize:
      assert(counts.neutralize > 0);
      --counts.neutralize;
      return;
    case CardId::kSurvivor:
      assert(counts.survivor > 0);
      --counts.survivor;
      return;
    case CardId::kNone:
      break;
  }
  assert(false && "dec_count: invalid CardId");
}

void inc_count(CardCounts& counts, CardId id) {
  switch (id) {
    case CardId::kStrike:
      ++counts.strike;
      return;
    case CardId::kDefend:
      ++counts.defend;
      return;
    case CardId::kNeutralize:
      ++counts.neutralize;
      return;
    case CardId::kSurvivor:
      ++counts.survivor;
      return;
    case CardId::kNone:
      break;
  }
  assert(false && "inc_count: invalid CardId");
}

void damage_enemy(EnemyState& enemy, int strength, int weak, int base) {
  const int dmg = sts2::damage::compute_outgoing(base, strength, weak);
  apply_damage_u8(enemy.hp, enemy.block, dmg);
  if (enemy.hp == 0) {
    enemy.alive = false;
  }
}

}  // namespace

std::vector<Action> legal_actions(const CompactState& state) {
  assert(state.phase == Phase::kPlayerActing);

  std::vector<Action> actions;

  for (CardId id : kAllCards) {
    if (count_in_hand(state.hand, id) == 0) continue;
    const int cost = card_cost(id);
    if (cost > state.energy) continue;

    const TargetType tgt = card_target_kind(id);
    if (tgt == TargetType::kAnyEnemy) {
      for (uint8_t i = 0; i < 2; ++i) {
        if (!state.enemies[i].alive) continue;
        Action a;
        a.kind = ActionKind::kPlayCard;
        a.card_id = id;
        a.target_idx = i;
        actions.push_back(a);
      }
    } else if (id == CardId::kSurvivor) {
      CardCounts post = state.hand;
      dec_count(post, CardId::kSurvivor);
      if (post.total() == 0) {
        Action a;
        a.kind = ActionKind::kPlayCard;
        a.card_id = CardId::kSurvivor;
        a.target_idx = kNoTarget;
        a.survivor_discard_id = CardId::kNone;
        actions.push_back(a);
      } else {
        for (CardId other : kAllCards) {
          if (other == CardId::kSurvivor) continue;
          if (count_in_hand(post, other) == 0) continue;
          Action a;
          a.kind = ActionKind::kPlayCard;
          a.card_id = CardId::kSurvivor;
          a.target_idx = kNoTarget;
          a.survivor_discard_id = other;
          actions.push_back(a);
        }
      }
    } else {
      Action a;
      a.kind = ActionKind::kPlayCard;
      a.card_id = id;
      a.target_idx = kNoTarget;
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
  const int cost = card_cost(id);
  if (cost > state.energy) return false;
  if (count_in_hand(state.hand, id) == 0) return false;

  const TargetType tgt = card_target_kind(id);
  if (tgt == TargetType::kAnyEnemy) {
    if (action.target_idx >= 2) return false;
    if (!state.enemies[action.target_idx].alive) return false;
  }

  dec_count(state.hand, id);
  state.energy = static_cast<uint8_t>(state.energy - cost);

  // Card costs/effects mirror src/game/cards.cc make_*() factories; keep in sync.
  switch (id) {
    case CardId::kStrike: {
      EnemyState& e = state.enemies[action.target_idx];
      damage_enemy(e, state.player_strength, state.player_weak, 6);
      break;
    }
    case CardId::kDefend: {
      state.player_block = static_cast<uint8_t>(state.player_block + 5);
      break;
    }
    case CardId::kNeutralize: {
      EnemyState& e = state.enemies[action.target_idx];
      damage_enemy(e, state.player_strength, state.player_weak, 3);
      e.weak = static_cast<uint8_t>(e.weak + 1);
      break;
    }
    case CardId::kSurvivor: {
      state.player_block = static_cast<uint8_t>(state.player_block + 8);
      if (state.hand.total() == 0) {
        assert(action.survivor_discard_id == sts2::game::CardId::kNone);
      } else if (action.survivor_discard_id != CardId::kNone) {
        assert(count_in_hand(state.hand, action.survivor_discard_id) > 0);
        dec_count(state.hand, action.survivor_discard_id);
        inc_count(state.discard, action.survivor_discard_id);
      }
      break;
    }
    case CardId::kNone:
      assert(false && "unreachable");
      break;
  }

  inc_count(state.discard, id);
  return true;
}

namespace {

constexpr int kBaseHandDraw = 5;
constexpr int kRingOfTheSnakeBonus = 2;

void merge_hand_to_discard(CompactState& s) {
  s.discard.strike = static_cast<uint8_t>(s.discard.strike + s.hand.strike);
  s.discard.defend = static_cast<uint8_t>(s.discard.defend + s.hand.defend);
  s.discard.neutralize =
      static_cast<uint8_t>(s.discard.neutralize + s.hand.neutralize);
  s.discard.survivor =
      static_cast<uint8_t>(s.discard.survivor + s.hand.survivor);
  s.hand = CardCounts{};
}

void enemy_act(CompactState& s, EnemyState& e) {
  switch (e.current_move) {
    case sts2::game::MoveId::kIncantation:
      // Mirrors powers::apply for kRitual: amount accumulates on the Power,
      // but in v1 Ritual is applied once -> Power.amount stays at ritual_amount.
      // We model the dynamic Ritual state purely via just_applied_ritual.
      e.just_applied_ritual = true;
      break;
    case sts2::game::MoveId::kDarkStrike: {
      const int dmg =
          sts2::damage::compute_outgoing(e.dark_strike_base, e.strength, e.weak);
      apply_damage_u8(s.player_hp, s.player_block, dmg);
      break;
    }
  }
}

void enemy_tick_powers(EnemyState& e) {
  if (e.just_applied_ritual) {
    e.just_applied_ritual = false;
  } else {
    e.strength = static_cast<uint8_t>(e.strength + e.ritual_amount);
  }
  if (e.weak > 0) {
    e.weak = static_cast<uint8_t>(e.weak - 1);
  }
}

void roll_next_move(EnemyState& e) {
  if (!e.performed_first_move) {
    e.performed_first_move = true;
    return;
  }
  if (e.current_move == sts2::game::MoveId::kIncantation) {
    e.current_move = sts2::game::MoveId::kDarkStrike;
  }
}

}  // namespace

bool is_terminal(const CompactState& s) noexcept {
  if (s.player_hp == 0) return true;
  return std::all_of(s.enemies.begin(), s.enemies.end(),
                     [](const EnemyState& e) { return !e.alive; });
}

int draw_count(const CompactState& s) noexcept {
  return kBaseHandDraw + (s.round == 1 ? kRingOfTheSnakeBonus : 0);
}

void resolve_end_turn_pre_draw(CompactState& state) {
  assert(state.phase == Phase::kAtChanceDraw);

  // end_player_turn: hand -> discard. Player power tick is a no-op in v1
  // (no Ritual on player; Weak hard-asserted 0 by from_combat).
  merge_hand_to_discard(state);

  // enemy_phase: zero block on alive enemies, then act in slot order; bail
  // early if player dies mid-phase (mirrors Combat::enemy_phase combat_over_).
  for (auto& e : state.enemies) {
    if (e.alive) e.block = 0;
  }
  for (auto& e : state.enemies) {
    if (!e.alive) continue;
    enemy_act(state, e);
    if (state.player_hp == 0) return;
  }
  // Tick AFTER all acts.
  for (auto& e : state.enemies) {
    if (e.alive) enemy_tick_powers(e);
  }

  state.round = static_cast<uint16_t>(state.round + 1);

  for (auto& e : state.enemies) {
    if (e.alive) roll_next_move(e);
  }

  if (state.round > 1) {
    state.player_block = 0;
  }
  state.energy = 3;
  // Phase already kAtChanceDraw; the draw step is the chance node.
}

void apply_draw(CompactState& state, CardCounts drawn) {
  assert(state.phase == Phase::kAtChanceDraw);
  assert(drawn.total() <= 10);

  // Reshuffle if the draw pile alone can't satisfy the request. Engine drains
  // pre-reshuffle cards first then post-reshuffle; for multiset purposes the
  // unioned outcome is identical, so a single up-front reshuffle is sound.
  const bool need_reshuffle = drawn.strike > state.draw.strike ||
                              drawn.defend > state.draw.defend ||
                              drawn.neutralize > state.draw.neutralize ||
                              drawn.survivor > state.draw.survivor;
  if (need_reshuffle) {
    state.draw.strike =
        static_cast<uint8_t>(state.draw.strike + state.discard.strike);
    state.draw.defend =
        static_cast<uint8_t>(state.draw.defend + state.discard.defend);
    state.draw.neutralize =
        static_cast<uint8_t>(state.draw.neutralize + state.discard.neutralize);
    state.draw.survivor =
        static_cast<uint8_t>(state.draw.survivor + state.discard.survivor);
    state.discard = CardCounts{};
  }

  assert(drawn.strike <= state.draw.strike);
  assert(drawn.defend <= state.draw.defend);
  assert(drawn.neutralize <= state.draw.neutralize);
  assert(drawn.survivor <= state.draw.survivor);

  state.hand.strike = static_cast<uint8_t>(state.hand.strike + drawn.strike);
  state.hand.defend = static_cast<uint8_t>(state.hand.defend + drawn.defend);
  state.hand.neutralize =
      static_cast<uint8_t>(state.hand.neutralize + drawn.neutralize);
  state.hand.survivor =
      static_cast<uint8_t>(state.hand.survivor + drawn.survivor);

  state.draw.strike = static_cast<uint8_t>(state.draw.strike - drawn.strike);
  state.draw.defend = static_cast<uint8_t>(state.draw.defend - drawn.defend);
  state.draw.neutralize =
      static_cast<uint8_t>(state.draw.neutralize - drawn.neutralize);
  state.draw.survivor =
      static_cast<uint8_t>(state.draw.survivor - drawn.survivor);

  state.phase = Phase::kPlayerActing;
}

}  // namespace sts2::ai::transition

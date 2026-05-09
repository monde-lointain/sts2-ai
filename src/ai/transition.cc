#include "sts2/ai/transition.h"

#include <cassert>
#include <cstdint>

namespace sts2::ai::transition {

namespace {

using sts2::game::CardId;
using sts2::game::TargetType;

constexpr CardId kAllCards[] = {CardId::kStrike, CardId::kDefend,
                                CardId::kNeutralize, CardId::kSurvivor};

// Mirrors src/game/damage.cc compute_outgoing/apply_to_defender; keep in sync.
int compute_outgoing(int strength, int weak, int base) {
  int d = base + strength;
  if (weak > 0) {
    d = static_cast<int>(d * 0.75);
  }
  return d < 0 ? 0 : d;
}

void apply_to_defender(uint8_t& hp, uint8_t& block, int incoming) {
  if (incoming <= block) {
    block = static_cast<uint8_t>(block - incoming);
    return;
  }
  incoming -= block;
  block = 0;
  int loss = incoming < hp ? incoming : hp;
  hp = static_cast<uint8_t>(hp - loss);
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
  const int dmg = compute_outgoing(strength, weak, base);
  apply_to_defender(enemy.hp, enemy.block, dmg);
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

}  // namespace sts2::ai::transition

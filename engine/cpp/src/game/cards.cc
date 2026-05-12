#include "sts2/game/cards.h"

#include "sts2/game/card_effects.h"
#include "sts2/game/combat.h"
#include "sts2/game/index_types.h"

namespace sts2::cards {

sts2::game::Card make_card(sts2::game::CardId id) {
  const auto& fx = sts2::game::card_effects::card_effect_for(id);
  sts2::game::Card c;
  c.id = fx.id;
  c.name = fx.name;
  c.cost = fx.cost;
  c.type = fx.type;
  c.target = fx.target;
  c.base_damage = fx.base_damage;
  c.base_block = fx.base_block;
  c.short_stats = fx.short_stats;
  for (const auto& line : fx.description) {
    if (!line.empty()) {
      c.description.emplace_back(line);
    }
  }
  // fx aliases inline constexpr kCardEffects[]; capture by reference is safe
  // (static storage).
  c.on_play = [&fx](sts2::game::Combat& combat, sts2::game::EnemySlot target) {
    if (fx.base_damage) {
      combat.deal_damage_to_enemy(target, fx.base_damage);
    }
    if (fx.base_block) {
      combat.gain_player_block(fx.base_block);
    }
    if (fx.weak_to_target) {
      combat.apply_power_to_enemy(target, sts2::game::PowerKind::kWeak,
                                  fx.weak_to_target);
    }
    if (fx.requires_discard) {
      combat.discard_chosen_from_hand();
    }
  };
  return c;
}

std::vector<sts2::game::Card> make_silent_starter_deck() {
  std::vector<sts2::game::Card> deck;
  deck.reserve(12);
  for (int i = 0; i < 5; ++i) {
    deck.push_back(make_card(sts2::game::CardId::kStrike));
  }
  for (int i = 0; i < 5; ++i) {
    deck.push_back(make_card(sts2::game::CardId::kDefend));
  }
  deck.push_back(make_card(sts2::game::CardId::kNeutralize));
  deck.push_back(make_card(sts2::game::CardId::kSurvivor));
  return deck;
}

}  // namespace sts2::cards

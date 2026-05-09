#include "sts2/game/cards.h"

#include "sts2/game/card_effects.h"
#include "sts2/game/combat.h"

namespace sts2::cards {

sts2::game::Card make_strike() {
  const auto& fx = sts2::game::card_effects::card_effect_for(
      sts2::game::CardId::kStrike);
  sts2::game::Card c;
  c.id = fx.id;
  c.name = fx.name;
  c.cost = fx.cost;
  c.type = sts2::game::CardType::kAttack;
  c.target = fx.target;
  c.base_damage = fx.base_damage;
  c.short_stats = "6dmg";
  c.description = {"Deal 6 damage."};
  c.on_play = [base = c.base_damage](sts2::game::Combat& combat,
                                     int target_idx) {
    combat.deal_damage_to_enemy(target_idx, base);
  };
  return c;
}

sts2::game::Card make_defend() {
  const auto& fx = sts2::game::card_effects::card_effect_for(
      sts2::game::CardId::kDefend);
  sts2::game::Card c;
  c.id = fx.id;
  c.name = fx.name;
  c.cost = fx.cost;
  c.type = sts2::game::CardType::kSkill;
  c.target = fx.target;
  c.base_block = fx.base_block;
  c.short_stats = "5blk";
  c.description = {"Gain 5 Block."};
  c.on_play = [base = c.base_block](sts2::game::Combat& combat, int) {
    combat.gain_player_block(base);
  };
  return c;
}

sts2::game::Card make_neutralize() {
  const auto& fx = sts2::game::card_effects::card_effect_for(
      sts2::game::CardId::kNeutralize);
  sts2::game::Card c;
  c.id = fx.id;
  c.name = fx.name;
  c.cost = fx.cost;
  c.type = sts2::game::CardType::kAttack;
  c.target = fx.target;
  c.base_damage = fx.base_damage;
  c.short_stats = "3dmg";
  c.description = {"Deal 3 damage.", "Apply 1 Weak."};
  c.on_play = [base = c.base_damage](sts2::game::Combat& combat,
                                     int target_idx) {
    combat.deal_damage_to_enemy(target_idx, base);
    combat.apply_power_to_enemy(target_idx, sts2::game::PowerKind::kWeak, 1);
  };
  return c;
}

sts2::game::Card make_survivor() {
  const auto& fx = sts2::game::card_effects::card_effect_for(
      sts2::game::CardId::kSurvivor);
  sts2::game::Card c;
  c.id = fx.id;
  c.name = fx.name;
  c.cost = fx.cost;
  c.type = sts2::game::CardType::kSkill;
  c.target = fx.target;
  c.base_block = fx.base_block;
  c.short_stats = "8blk";
  c.description = {"Gain 8 Block.", "Discard 1 card."};
  c.on_play = [base = c.base_block](sts2::game::Combat& combat, int) {
    combat.gain_player_block(base);
    combat.discard_chosen_from_hand();
  };
  return c;
}

std::vector<sts2::game::Card> make_silent_starter_deck() {
  std::vector<sts2::game::Card> deck;
  deck.reserve(12);
  for (int i = 0; i < 5; ++i) {
    deck.push_back(make_strike());
  }
  for (int i = 0; i < 5; ++i) {
    deck.push_back(make_defend());
  }
  deck.push_back(make_neutralize());
  deck.push_back(make_survivor());
  return deck;
}

}  // namespace sts2::cards

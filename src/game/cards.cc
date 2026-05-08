#include "sts2/game/cards.h"

#include <utility>

#include "sts2/game/combat.h"
#include "sts2/game/damage.h"
#include "sts2/game/powers.h"

namespace sts2::cards {

sts2::game::Card make_strike() {
  sts2::game::Card c;
  c.id = sts2::game::CardId::Strike;
  c.name = "Strike";
  c.cost = 1;
  c.type = sts2::game::CardType::Attack;
  c.target = sts2::game::TargetType::AnyEnemy;
  c.base_damage = 6;
  c.short_stats = "6dmg";
  c.description = {"Deal 6 damage."};
  c.on_play = [base = c.base_damage](sts2::game::Combat& combat,
                                     int target_idx) {
    combat.deal_damage_to_enemy(target_idx, base);
  };
  return c;
}

sts2::game::Card make_defend() {
  sts2::game::Card c;
  c.id = sts2::game::CardId::Defend;
  c.name = "Defend";
  c.cost = 1;
  c.type = sts2::game::CardType::Skill;
  c.target = sts2::game::TargetType::Self;
  c.base_block = 5;
  c.short_stats = "5blk";
  c.description = {"Gain 5 Block."};
  c.on_play = [base = c.base_block](sts2::game::Combat& combat, int) {
    combat.gain_player_block(base);
  };
  return c;
}

sts2::game::Card make_neutralize() {
  sts2::game::Card c;
  c.id = sts2::game::CardId::Neutralize;
  c.name = "Neutralize";
  c.cost = 0;
  c.type = sts2::game::CardType::Attack;
  c.target = sts2::game::TargetType::AnyEnemy;
  c.base_damage = 3;
  c.short_stats = "3dmg";
  c.description = {"Deal 3 damage.", "Apply 1 Weak."};
  c.on_play = [base = c.base_damage](sts2::game::Combat& combat,
                                     int target_idx) {
    combat.deal_damage_to_enemy(target_idx, base);
    combat.apply_power_to_enemy(target_idx, sts2::game::PowerKind::Weak, 1);
  };
  return c;
}

sts2::game::Card make_survivor() {
  sts2::game::Card c;
  c.id = sts2::game::CardId::Survivor;
  c.name = "Survivor";
  c.cost = 1;
  c.type = sts2::game::CardType::Skill;
  c.target = sts2::game::TargetType::Self;
  c.base_block = 8;
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
  for (int i = 0; i < 5; ++i) deck.push_back(make_strike());
  for (int i = 0; i < 5; ++i) deck.push_back(make_defend());
  deck.push_back(make_neutralize());
  deck.push_back(make_survivor());
  return deck;
}

}  // namespace sts2::cards

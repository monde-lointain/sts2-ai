#pragma once

#include <cstdint>
#include <functional>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/enemy.h"
#include "sts2/game/index_types.h"
#include "sts2/game/player.h"
#include "sts2/game/rng.h"
#include "sts2/game/turn_calc.h"
#include "sts2/game/types.h"

namespace sts2::game {

class Combat {
 public:
  static constexpr int kPlayerMaxEnergy = turn_calc::kPlayerStartingEnergy;

  explicit Combat(uint64_t seed);

  void start(std::vector<Card> starter_deck);

  void start_player_turn();
  void end_player_turn();
  void enemy_phase();
  void end_turn();

  [[nodiscard]] bool can_play(HandIndex idx) const;
  bool play_card(HandIndex hand_idx, EnemySlot target = EnemySlot::none());

  [[nodiscard]] bool is_player_dead() const;
  [[nodiscard]] bool all_enemies_dead() const;
  void check_win_or_lose();

  [[nodiscard]] const Player& player() const { return player_; }
  [[nodiscard]] const std::vector<Enemy>& enemies() const { return enemies_; }
  [[nodiscard]] int round() const { return round_; }
  [[nodiscard]] bool combat_over() const { return combat_over_; }

  // Query helpers
  [[nodiscard]] std::vector<EnemySlot> alive_enemy_indices() const;
  [[nodiscard]] HandIndex find_card_in_hand(CardId id) const;

  void add_enemy(Enemy e);
  // Overrides the player's vitals BEFORE start() is called. Used by the
  // scenario loader to inject custom HP/max_hp/powers from JSON. start() will
  // zero block via start_player_turn(); hp/max_hp/powers persist as set.
  void set_player_vitals(Vitals v);
  void set_pick_discard_callback(std::function<HandIndex(const Combat&)> cb);
  void deal_damage_to_enemy(EnemySlot slot, int base_damage);
  void enemy_attack_player(const Enemy& source, int base_damage);
  void gain_player_block(int amt);
  void apply_power_to_enemy(EnemySlot slot, PowerKind kind, int amt);
  void discard_chosen_from_hand();

 private:
  Player player_;
  std::vector<Enemy> enemies_;
  Rng rng_;
  std::function<HandIndex(const Combat&)> on_pick_discard_;
  int round_ = 1;
  bool combat_over_ = false;
};

}  // namespace sts2::game

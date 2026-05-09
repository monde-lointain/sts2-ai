#pragma once

#include <cstddef>
#include <cstdint>
#include <functional>
#include <span>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/enemy.h"
#include "sts2/game/player.h"
#include "sts2/game/power.h"
#include "sts2/game/rng.h"
#include "sts2/game/types.h"

namespace sts2::game {

class Combat {
 public:
  explicit Combat(uint64_t seed);

  void start(std::vector<Card> starter_deck);

  void start_player_turn();
  void end_player_turn();
  void enemy_phase();
  void end_turn();

  [[nodiscard]] bool can_play(int hand_idx) const;
  bool play_card(int hand_idx, int target_idx = -1);

  void draw(int n);
  void reshuffle();

  [[nodiscard]] bool is_player_dead() const;
  [[nodiscard]] bool all_enemies_dead() const;
  void check_win_or_lose();

  [[nodiscard]] const Player& player() const { return player_; }
  [[nodiscard]] const std::vector<Enemy>& enemies() const { return enemies_; }
  [[nodiscard]] int round() const { return round_; }
  [[nodiscard]] bool combat_over() const { return combat_over_; }

  // Query helpers — adapt callers off direct vector/struct poking.
  [[nodiscard]] bool is_enemy_alive(int idx) const;
  [[nodiscard]] std::vector<int> alive_enemy_indices() const;
  [[nodiscard]] TargetType card_target_kind(int hand_idx) const;
  [[nodiscard]] std::size_t hand_size() const;
  [[nodiscard]] int find_card_in_hand(CardId id) const;

  [[nodiscard]] int player_hp() const;
  [[nodiscard]] int player_max_hp() const;
  [[nodiscard]] int player_block() const;
  [[nodiscard]] int player_energy() const;
  [[nodiscard]] int player_max_energy() const;
  [[nodiscard]] std::span<const Power> player_powers() const;
  [[nodiscard]] const Card& player_hand_at(std::size_t i) const;
  [[nodiscard]] std::size_t draw_pile_size() const;
  [[nodiscard]] std::size_t discard_pile_size() const;
  [[nodiscard]] int total_deck_size() const;
  [[nodiscard]] const Enemy& enemy_at(int slot) const;
  [[nodiscard]] int display_index_of(int slot) const;

  void add_enemy(Enemy e);
  void set_pick_discard_callback(std::function<int(const Combat&)> cb);
  void deal_damage_to_enemy(int idx, int base_damage);
  void enemy_attack_player(const Enemy& source, int base_damage);
  void gain_player_block(int amt);
  void apply_power_to_enemy(int idx, PowerKind kind, int amt);
  static void apply_power_to_enemy_self(Enemy& e, PowerKind kind, int amt);
  void discard_chosen_from_hand();

 private:
  Player player_;
  std::vector<Enemy> enemies_;
  Rng rng_;
  std::function<int(const Combat&)> on_pick_discard_;
  int round_ = 1;
  bool combat_over_ = false;
};

}  // namespace sts2::game

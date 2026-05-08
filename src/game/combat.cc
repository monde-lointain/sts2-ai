#include "sts2/game/combat.h"

#include <algorithm>
#include <cassert>
#include <utility>

#include "sts2/game/damage.h"
#include "sts2/game/enemies.h"
#include "sts2/game/powers.h"

namespace sts2::game {

namespace {
constexpr int kMaxHandSize = 10;
constexpr int kBaseHandDraw = 5;
constexpr int kRingOfTheSnakeBonus = 2;
}  // namespace

Combat::Combat(uint64_t seed)
    : rng_(seed), on_pick_discard_([](const Combat&) { return 0; }) {}

void Combat::start(std::vector<Card> starter_deck) {
  player_.draw_pile = std::move(starter_deck);
  rng_.shuffle(player_.draw_pile);
  round_ = 1;
  combat_over_ = false;
  start_player_turn();
}

void Combat::start_player_turn() {
  for (auto& e : enemies_) {
    if (e.vitals.hp > 0) sts2::enemies::roll_next_move(e);
  }

  if (round_ > 1) player_.vitals.block = 0;

  player_.energy = player_.max_energy;

  int draw_count = kBaseHandDraw + (round_ == 1 ? kRingOfTheSnakeBonus : 0);
  draw(draw_count);
}

void Combat::end_player_turn() {
  while (!player_.hand.empty()) {
    player_.discard_pile.push_back(std::move(player_.hand.back()));
    player_.hand.pop_back();
  }
  sts2::powers::tick_at_turn_end(player_.vitals.powers);
}

void Combat::enemy_phase() {
  for (auto& e : enemies_) {
    if (e.vitals.hp > 0) e.vitals.block = 0;
  }
  for (auto& e : enemies_) {
    if (e.vitals.hp <= 0) continue;
    sts2::enemies::act(e, *this);
    if (combat_over_) return;
  }
  for (auto& e : enemies_) {
    if (e.vitals.hp > 0) sts2::powers::tick_at_turn_end(e.vitals.powers);
  }
}

void Combat::end_turn() {
  if (combat_over_) return;
  end_player_turn();
  enemy_phase();
  if (combat_over_) return;
  round_ += 1;
  start_player_turn();
  check_win_or_lose();
}

bool Combat::can_play(int hand_idx) const {
  if (hand_idx < 0 || static_cast<size_t>(hand_idx) >= player_.hand.size())
    return false;
  const Card& card = player_.hand[static_cast<size_t>(hand_idx)];
  return card.cost <= player_.energy;
}

bool Combat::play_card(int hand_idx, int target_idx) {
  if (!can_play(hand_idx)) return false;
  Card card = std::move(player_.hand[static_cast<size_t>(hand_idx)]);
  player_.hand.erase(player_.hand.begin() + hand_idx);
  player_.energy -= card.cost;
  if (card.on_play) card.on_play(*this, target_idx);
  player_.discard_pile.push_back(std::move(card));
  // Backstop: card lambdas may invoke only non-HP-mutating verbs; ensure
  // terminal check.
  check_win_or_lose();
  return true;
}

void Combat::draw(int n) {
  for (int i = 0; i < n; ++i) {
    if (static_cast<int>(player_.hand.size()) >= kMaxHandSize) return;
    if (player_.draw_pile.empty()) reshuffle();
    if (player_.draw_pile.empty()) return;
    player_.hand.push_back(std::move(player_.draw_pile.back()));
    player_.draw_pile.pop_back();
  }
}

void Combat::reshuffle() {
  while (!player_.discard_pile.empty()) {
    player_.draw_pile.push_back(std::move(player_.discard_pile.back()));
    player_.discard_pile.pop_back();
  }
  rng_.shuffle(player_.draw_pile);
}

bool Combat::is_player_dead() const { return player_.vitals.hp <= 0; }

bool Combat::all_enemies_dead() const {
  for (const auto& e : enemies_) {
    if (e.vitals.hp > 0) return false;
  }
  return !enemies_.empty();
}

void Combat::check_win_or_lose() {
  if (is_player_dead() || all_enemies_dead()) combat_over_ = true;
}

void Combat::add_enemy(Enemy e) { enemies_.push_back(std::move(e)); }

void Combat::set_pick_discard_callback(std::function<int(const Combat&)> cb) {
  on_pick_discard_ = std::move(cb);
}

void Combat::deal_damage_to_enemy(int idx, int base_damage) {
  assert(idx >= 0 && static_cast<size_t>(idx) < enemies_.size());
  Enemy& e = enemies_[static_cast<size_t>(idx)];
  int dmg = sts2::damage::compute_outgoing(player_.vitals.powers, base_damage);
  sts2::damage::apply_to_defender(e.vitals, dmg);
  check_win_or_lose();
}

void Combat::enemy_attack_player(Enemy& source, int base_damage) {
  int dmg = sts2::damage::compute_outgoing(source.vitals.powers, base_damage);
  sts2::damage::apply_to_defender(player_.vitals, dmg);
  check_win_or_lose();
}

void Combat::gain_player_block(int amt) { player_.vitals.block += amt; }

void Combat::apply_power_to_enemy(int idx, PowerKind kind, int amt) {
  assert(idx >= 0 && static_cast<size_t>(idx) < enemies_.size());
  Enemy& e = enemies_[static_cast<size_t>(idx)];
  sts2::powers::apply(e.vitals.powers, kind, amt);
}

void Combat::apply_power_to_enemy_self(Enemy& e, PowerKind kind, int amt) {
  sts2::powers::apply(e.vitals.powers, kind, amt);
}

void Combat::discard_chosen_from_hand() {
  if (player_.hand.empty()) return;
  int idx = on_pick_discard_(*this);
  if (idx < 0 || static_cast<size_t>(idx) >= player_.hand.size()) return;
  Card chosen = std::move(player_.hand[static_cast<size_t>(idx)]);
  player_.hand.erase(player_.hand.begin() + idx);
  player_.discard_pile.push_back(std::move(chosen));
}

}  // namespace sts2::game

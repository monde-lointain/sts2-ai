#include "sts2/game/combat.h"

#include <algorithm>
#include <cassert>
#include <utility>

#include "sts2/game/damage.h"
#include "sts2/game/enemies.h"
#include "sts2/game/powers.h"
#include "sts2/game/turn_calc.h"

namespace sts2::game {

namespace {
constexpr int kMaxHandSize = 10;
static_assert(Combat::kPlayerMaxEnergy == turn_calc::kPlayerStartingEnergy,
              "energy constant drift");
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
    if (e.vitals.hp > 0) {
      sts2::enemies::roll_next_move(e);
    }
  }

  if (turn_calc::round_resets_block(round_)) {
    player_.vitals.block = 0;
  }

  player_.energy = turn_calc::starting_energy();

  draw(turn_calc::hand_draw_size(round_));
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
    if (e.vitals.hp > 0) {
      e.vitals.block = 0;
    }
  }
  for (auto& e : enemies_) {
    if (e.vitals.hp <= 0) {
      continue;
    }
    sts2::enemies::act(e, *this);
    if (combat_over_) {
      return;
    }
  }
  for (auto& e : enemies_) {
    if (e.vitals.hp > 0) {
      sts2::powers::tick_at_turn_end(e.vitals.powers);
    }
  }
}

void Combat::end_turn() {
  if (combat_over_) {
    return;
  }
  end_player_turn();
  enemy_phase();
  if (combat_over_) {
    return;
  }
  round_ += 1;
  start_player_turn();
  check_win_or_lose();
}

bool Combat::can_play(HandIndex idx) const { return can_play(idx.raw()); }

bool Combat::can_play(int hand_idx) const {
  if (hand_idx < 0 || static_cast<size_t>(hand_idx) >= player_.hand.size()) {
    return false;
  }
  const Card& card = player_.hand[static_cast<size_t>(hand_idx)];
  return card.cost <= player_.energy;
}

bool Combat::play_card(HandIndex hand_idx, EnemySlot target) {
  return play_card(hand_idx.raw(), target.raw());
}

bool Combat::play_card(int hand_idx, int target_idx) {
  if (!can_play(hand_idx)) {
    return false;
  }
  Card card = std::move(player_.hand[static_cast<size_t>(hand_idx)]);
  player_.hand.erase(player_.hand.begin() + hand_idx);
  player_.energy -= card.cost;
  if (card.on_play) {
    card.on_play(*this, target_idx);
  }
  player_.discard_pile.push_back(std::move(card));
  // Backstop: card lambdas may invoke only non-HP-mutating verbs; ensure
  // terminal check.
  check_win_or_lose();
  return true;
}

void Combat::draw(int n) {
  for (int i = 0; i < n; ++i) {
    if (static_cast<int>(player_.hand.size()) >= kMaxHandSize) {
      return;
    }
    if (player_.draw_pile.empty()) {
      reshuffle();
    }
    if (player_.draw_pile.empty()) {
      return;
    }
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

bool Combat::is_enemy_alive(EnemySlot slot) const {
  return is_enemy_alive(slot.raw());
}

bool Combat::is_enemy_alive(int idx) const {
  if (idx < 0 || static_cast<std::size_t>(idx) >= enemies_.size()) {
    return false;
  }
  return enemies_[static_cast<std::size_t>(idx)].vitals.hp > 0;
}

std::vector<int> Combat::alive_enemy_indices() const {
  std::vector<int> out;
  for (std::size_t i = 0; i < enemies_.size(); ++i) {
    if (enemies_[i].vitals.hp > 0) {
      out.push_back(static_cast<int>(i));
    }
  }
  return out;
}

TargetType Combat::card_target_kind(HandIndex idx) const {
  return card_target_kind(idx.raw());
}

TargetType Combat::card_target_kind(int hand_idx) const {
  if (hand_idx < 0 ||
      static_cast<std::size_t>(hand_idx) >= player_.hand.size()) {
    return TargetType::kNoTarget;
  }
  return player_.hand[static_cast<std::size_t>(hand_idx)].target;
}

std::size_t Combat::hand_size() const { return player_.hand.size(); }

int Combat::find_card_in_hand(CardId id) const {
  for (std::size_t i = 0; i < player_.hand.size(); ++i) {
    if (player_.hand[i].id == id) {
      return static_cast<int>(i);
    }
  }
  return -1;
}

int Combat::player_hp() const { return player_.vitals.hp; }
int Combat::player_max_hp() const { return player_.vitals.max_hp; }
int Combat::player_block() const { return player_.vitals.block; }
int Combat::player_energy() const { return player_.energy; }

std::span<const Power> Combat::player_powers() const {
  return player_.vitals.powers;
}

const Card& Combat::player_hand_at(HandIndex idx) const {
  return player_hand_at(static_cast<std::size_t>(idx.raw()));
}

const Card& Combat::player_hand_at(std::size_t i) const {
  assert(i < player_.hand.size());
  return player_.hand[i];
}

std::size_t Combat::draw_pile_size() const { return player_.draw_pile.size(); }
std::size_t Combat::discard_pile_size() const {
  return player_.discard_pile.size();
}

int Combat::total_deck_size() const {
  return static_cast<int>(player_.draw_pile.size() + player_.hand.size() +
                          player_.discard_pile.size());
}

const Enemy& Combat::enemy_at(EnemySlot slot) const {
  return enemy_at(slot.raw());
}

const Enemy& Combat::enemy_at(int slot) const {
  assert(slot >= 0 && static_cast<std::size_t>(slot) < enemies_.size());
  return enemies_[static_cast<std::size_t>(slot)];
}

int Combat::display_index_of(EnemySlot slot) const {
  return display_index_of(slot.raw());
}

int Combat::display_index_of(int slot) const {
  if (!is_enemy_alive(slot)) {
    return -1;
  }
  int display = 0;
  for (int i = 0; i < slot; ++i) {
    if (is_enemy_alive(i)) {
      ++display;
    }
  }
  return display;
}

bool Combat::is_player_dead() const { return player_.vitals.hp <= 0; }

bool Combat::all_enemies_dead() const {
  return !enemies_.empty() &&
         std::all_of(enemies_.begin(), enemies_.end(),
                     [](const Enemy& e) { return e.vitals.hp <= 0; });
}

void Combat::check_win_or_lose() {
  if (is_player_dead() || all_enemies_dead()) {
    combat_over_ = true;
  }
}

void Combat::add_enemy(Enemy e) { enemies_.push_back(std::move(e)); }

void Combat::set_pick_discard_callback(std::function<int(const Combat&)> cb) {
  on_pick_discard_ = std::move(cb);
}

void Combat::deal_damage_to_enemy(EnemySlot slot, int base_damage) {
  deal_damage_to_enemy(slot.raw(), base_damage);
}

void Combat::deal_damage_to_enemy(int idx, int base_damage) {
  assert(idx >= 0 && static_cast<size_t>(idx) < enemies_.size());
  Enemy& e = enemies_[static_cast<size_t>(idx)];
  int dmg = sts2::damage::compute_outgoing(player_.vitals.powers, base_damage);
  sts2::damage::apply_to_defender(e.vitals, dmg);
  check_win_or_lose();
}

void Combat::enemy_attack_player(const Enemy& source, int base_damage) {
  int dmg = sts2::damage::compute_outgoing(source.vitals.powers, base_damage);
  sts2::damage::apply_to_defender(player_.vitals, dmg);
  check_win_or_lose();
}

void Combat::gain_player_block(int amt) { player_.vitals.block += amt; }

void Combat::apply_power_to_enemy(EnemySlot slot, PowerKind kind, int amt) {
  apply_power_to_enemy(slot.raw(), kind, amt);
}

void Combat::apply_power_to_enemy(int idx, PowerKind kind, int amt) {
  assert(idx >= 0 && static_cast<size_t>(idx) < enemies_.size());
  Enemy& e = enemies_[static_cast<size_t>(idx)];
  sts2::powers::apply(e.vitals.powers, kind, amt);
}

void Combat::apply_power_to_enemy_self(Enemy& e, PowerKind kind, int amt) {
  sts2::powers::apply(e.vitals.powers, kind, amt);
}

void Combat::discard_chosen_from_hand() {
  if (player_.hand.empty()) {
    return;
  }
  int idx = on_pick_discard_(*this);
  if (idx < 0 || static_cast<size_t>(idx) >= player_.hand.size()) {
    return;
  }
  Card chosen = std::move(player_.hand[static_cast<size_t>(idx)]);
  player_.hand.erase(player_.hand.begin() + idx);
  player_.discard_pile.push_back(std::move(chosen));
}

}  // namespace sts2::game

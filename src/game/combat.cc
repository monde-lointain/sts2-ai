#include "sts2/game/combat.h"

#include <algorithm>
#include <cassert>

#include "sts2/game/damage.h"
#include "sts2/game/enemies.h"
#include "sts2/game/hand.h"
#include "sts2/game/powers.h"
#include "sts2/game/turn_calc.h"

namespace sts2::game {

namespace {
static_assert(Combat::kPlayerMaxEnergy == turn_calc::kPlayerStartingEnergy,
              "energy constant drift");
static_assert(Hand::kMaxSize == 10, "Hand::kMaxSize must match game rules");
}  // namespace

Combat::Combat(uint64_t seed)
    : rng_(seed),
      on_pick_discard_([](const Combat&) { return HandIndex{0}; }) {}

void Combat::start(std::vector<Card> starter_deck) {
  player_.deck.load_starter(std::move(starter_deck), rng_);
  round_ = 1;
  combat_over_ = false;
  start_player_turn();
}

void Combat::start_player_turn() {
  for_each_alive_enemy(enemies_,
                       [](Enemy& e) { sts2::enemies::roll_next_move(e); });

  if (turn_calc::round_resets_block(round_)) {
    player_.vitals.block = sts2::game::Stat{0};
  }

  player_.energy = turn_calc::starting_energy();

  player_.hand.draw_from(player_.deck, rng_, turn_calc::hand_draw_size(round_));
}

void Combat::end_player_turn() {
  player_.hand.dump_into(player_.deck);
  sts2::powers::tick_at_turn_end(player_.vitals.powers);
}

void Combat::enemy_phase() {
  for_each_alive_enemy(enemies_,
                       [](Enemy& e) { e.vitals.block = sts2::game::Stat{0}; });
  for (auto& e : enemies_) {
    if (!is_alive(e)) {
      continue;
    }
    sts2::enemies::act(e, *this);
    if (combat_over_) {
      return;
    }
  }
  for_each_alive_enemy(enemies_, [](Enemy& e) {
    sts2::powers::tick_at_turn_end(e.vitals.powers);
  });
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

bool Combat::can_play(HandIndex idx) const {
  if (!player_.hand.valid(idx)) {
    return false;
  }
  return player_.hand.at(idx).cost <= player_.energy;
}

bool Combat::play_card(HandIndex hand_idx, EnemySlot target) {
  if (!can_play(hand_idx)) {
    return false;
  }
  Card card = player_.hand.play(hand_idx);
  player_.energy -= card.cost;
  if (card.on_play) {
    card.on_play(*this, target);
  }
  player_.deck.discard(std::move(card));
  // Backstop: card lambdas may invoke only non-HP-mutating verbs; ensure
  // terminal check.
  check_win_or_lose();
  return true;
}

std::vector<EnemySlot> Combat::alive_enemy_indices() const {
  std::vector<EnemySlot> out;
  for (std::size_t i = 0; i < enemies_.size(); ++i) {
    if (is_alive(enemies_[i])) {
      out.push_back(EnemySlot{static_cast<int>(i)});
    }
  }
  return out;
}

HandIndex Combat::find_card_in_hand(CardId id) const {
  return player_.hand.find(id);
}

bool Combat::is_player_dead() const {
  return player_.vitals.hp <= sts2::game::Stat{0};
}

bool Combat::all_enemies_dead() const {
  return !enemies_.empty() &&
         std::all_of(enemies_.begin(), enemies_.end(),
                     [](const Enemy& e) { return !is_alive(e); });
}

void Combat::check_win_or_lose() {
  if (is_player_dead() || all_enemies_dead()) {
    combat_over_ = true;
  }
}

void Combat::add_enemy(Enemy e) { enemies_.push_back(std::move(e)); }

void Combat::set_pick_discard_callback(
    std::function<HandIndex(const Combat&)> cb) {
  on_pick_discard_ = std::move(cb);
}

void Combat::deal_damage_to_enemy(EnemySlot slot, int base_damage) {
  assert(slot.in_range(enemies_));
  Enemy& e = slot.at(enemies_);
  int dmg = sts2::damage::compute_outgoing(player_.vitals.powers, base_damage);
  (void)sts2::damage::apply_to_defender(e.vitals, dmg);
  check_win_or_lose();
}

void Combat::enemy_attack_player(const Enemy& source, int base_damage) {
  int dmg = sts2::damage::compute_outgoing(source.vitals.powers, base_damage);
  (void)sts2::damage::apply_to_defender(player_.vitals, dmg);
  check_win_or_lose();
}

void Combat::gain_player_block(int amt) { player_.vitals.block += amt; }

void Combat::apply_power_to_enemy(EnemySlot slot, PowerKind kind, int amt) {
  assert(slot.in_range(enemies_));
  Enemy& e = slot.at(enemies_);
  sts2::powers::apply(e.vitals.powers, kind, amt);
}

void Combat::discard_chosen_from_hand() {
  if (player_.hand.empty()) {
    return;
  }
  HandIndex idx = on_pick_discard_(*this);
  if (player_.hand.valid(idx)) {
    player_.deck.discard(player_.hand.discard_at(idx));
  }
}

}  // namespace sts2::game

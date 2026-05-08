#include "game/Combat.h"

#include <algorithm>
#include <utility>

#include "game/Damage.h"
#include "game/Enemies.h"
#include "game/Powers.h"

namespace {
constexpr int kMaxHandSize = 10;
constexpr int kBaseHandDraw = 5;
constexpr int kRingOfTheSnakeBonus = 2;
}

Combat::Combat(uint64_t seed)
    : rng(seed),
      on_pick_discard([](const Combat&) { return 0; }) {}

void Combat::start(std::vector<Card> starter_deck) {
    player.draw_pile = std::move(starter_deck);
    rng.shuffle(player.draw_pile);
    round = 1;
    combat_over = false;
    start_player_turn();
}

void Combat::start_player_turn() {
    for (auto& e : enemies) {
        if (e.vitals.hp > 0) enemies::roll_next_move(e);
    }

    if (round > 1) player.vitals.block = 0;

    player.energy = player.max_energy;

    int draw_count = kBaseHandDraw + (round == 1 ? kRingOfTheSnakeBonus : 0);
    draw(draw_count);
}

void Combat::end_player_turn() {
    while (!player.hand.empty()) {
        player.discard_pile.push_back(std::move(player.hand.back()));
        player.hand.pop_back();
    }
    powers::tick_at_turn_end(player.vitals.powers);
}

void Combat::enemy_phase() {
    for (auto& e : enemies) {
        if (e.vitals.hp > 0) e.vitals.block = 0;
    }
    for (auto& e : enemies) {
        if (e.vitals.hp <= 0) continue;
        enemies::act(e, *this);
        check_win_or_lose();
        if (combat_over) return;
    }
    for (auto& e : enemies) {
        if (e.vitals.hp > 0) powers::tick_at_turn_end(e.vitals.powers);
    }
}

void Combat::end_turn() {
    if (combat_over) return;
    end_player_turn();
    enemy_phase();
    if (combat_over) return;
    round += 1;
    start_player_turn();
    check_win_or_lose();
}

bool Combat::can_play(int hand_idx) const {
    if (hand_idx < 0 || static_cast<size_t>(hand_idx) >= player.hand.size()) return false;
    const Card& card = player.hand[static_cast<size_t>(hand_idx)];
    return card.cost <= player.energy;
}

bool Combat::play_card(int hand_idx, int target_idx) {
    if (!can_play(hand_idx)) return false;
    Card card = std::move(player.hand[static_cast<size_t>(hand_idx)]);
    player.hand.erase(player.hand.begin() + hand_idx);
    player.energy -= card.cost;
    if (card.on_play) card.on_play(*this, target_idx);
    player.discard_pile.push_back(std::move(card));
    check_win_or_lose();
    return true;
}

void Combat::draw(int n) {
    for (int i = 0; i < n; ++i) {
        if (static_cast<int>(player.hand.size()) >= kMaxHandSize) return;
        if (player.draw_pile.empty()) reshuffle();
        if (player.draw_pile.empty()) return;
        player.hand.push_back(std::move(player.draw_pile.back()));
        player.draw_pile.pop_back();
    }
}

void Combat::reshuffle() {
    while (!player.discard_pile.empty()) {
        player.draw_pile.push_back(std::move(player.discard_pile.back()));
        player.discard_pile.pop_back();
    }
    rng.shuffle(player.draw_pile);
}

bool Combat::is_player_dead() const { return player.vitals.hp <= 0; }

bool Combat::all_enemies_dead() const {
    for (const auto& e : enemies) {
        if (e.vitals.hp > 0) return false;
    }
    return !enemies.empty();
}

void Combat::check_win_or_lose() {
    if (is_player_dead() || all_enemies_dead()) combat_over = true;
}

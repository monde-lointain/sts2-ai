#pragma once

#include <cstdint>
#include <functional>
#include <vector>

#include "game/Card.h"
#include "game/Enemy.h"
#include "game/Player.h"
#include "game/Rng.h"

class Combat {
public:
    Player player;
    std::vector<Enemy> enemies;
    Rng rng;
    std::function<int(const Combat&)> on_pick_discard;
    int round = 1;
    bool combat_over = false;

    explicit Combat(uint64_t seed);

    void start(std::vector<Card> starter_deck);

    void start_player_turn();
    void end_player_turn();
    void enemy_phase();
    void end_turn();

    bool can_play(int hand_idx) const;
    bool play_card(int hand_idx, int target_idx = -1);

    void draw(int n);
    void reshuffle();

    bool is_player_dead() const;
    bool all_enemies_dead() const;
    void check_win_or_lose();
};

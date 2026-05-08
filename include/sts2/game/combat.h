#pragma once

#include <cstdint>
#include <functional>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/enemy.h"
#include "sts2/game/player.h"
#include "sts2/game/rng.h"
#include "sts2/game/types.h"

class Combat {
public:
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

    const Player&             player() const       { return player_; }
    const std::vector<Enemy>& enemies() const      { return enemies_; }
    int                       round() const        { return round_; }
    bool                      combat_over() const  { return combat_over_; }

    void add_enemy(Enemy e);
    void set_pick_discard_callback(std::function<int(const Combat&)> cb);
    void deal_damage_to_enemy(int idx, int base_damage);
    void enemy_attack_player(Enemy& source, int base_damage);
    void gain_player_block(int amt);
    void apply_power_to_enemy(int idx, PowerKind kind, int amt);
    void apply_power_to_enemy_self(Enemy& e, PowerKind kind, int amt);
    void discard_chosen_from_hand();

private:
    Player player_;
    std::vector<Enemy> enemies_;
    Rng rng_;
    std::function<int(const Combat&)> on_pick_discard_;
    int round_ = 1;
    bool combat_over_ = false;

    friend class CombatTestAccess;
};

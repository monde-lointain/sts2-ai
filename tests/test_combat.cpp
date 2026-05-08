#include <vector>

#include "game/Cards.h"
#include "game/Combat.h"
#include "game/Damage.h"
#include "game/Enemies.h"
#include "game/Enemy.h"
#include "game/Player.h"
#include "game/Power.h"
#include "game/Powers.h"
#include "game/Rng.h"
#include "game/Types.h"
#include "test_helpers.h"
#include "test_runner.h"

static int total_cards_in_play(const Combat& c) {
    return static_cast<int>(c.player.draw_pile.size() + c.player.hand.size()
                          + c.player.discard_pile.size() + c.player.exhaust_pile.size());
}

TEST(combat_start_player_state_defaults) {
    Combat c{42};
    Rng r(7);
    c.enemies.push_back(enemies::make_calcified_cultist(r));
    c.enemies.push_back(enemies::make_damp_cultist(r));
    c.start(cards::make_silent_starter_deck());
    CHECK(c.player.hp == 70);
    CHECK(c.player.max_hp == 70);
    CHECK(c.player.max_energy == 3);
    CHECK(c.player.energy == 3);
    CHECK(c.round == 1);
    CHECK(!c.combat_over);
}

TEST(combat_start_shuffles_starter_deck_into_draw) {
    Combat c{42};
    c.start(cards::make_silent_starter_deck());
    CHECK(total_cards_in_play(c) == 12);
    CHECK(c.player.hand.size() == 7u);
    CHECK(c.player.draw_pile.size() == 5u);
    CHECK(c.player.discard_pile.empty());
    CHECK(c.player.exhaust_pile.empty());
}

TEST(combat_round_one_draws_seven_with_ring_of_snake) {
    Combat c{1};
    c.start(cards::make_silent_starter_deck());
    CHECK(c.player.hand.size() == 7u);
    CHECK(c.round == 1);
}

TEST(combat_round_two_draws_five) {
    Combat c{1};
    Rng r(2);
    c.enemies.push_back(make_dummy_enemy(100));
    c.start(cards::make_silent_starter_deck());
    c.end_turn();
    CHECK(c.round == 2);
    CHECK(c.player.hand.size() == 5u);
}

TEST(combat_round_one_does_not_clear_player_block) {
    Combat c{1};
    c.start(cards::make_silent_starter_deck());
    c.player.block = 5;
    CHECK(c.player.block == 5);
}

TEST(combat_round_two_clears_player_block) {
    Combat c{1};
    c.enemies.push_back(make_dummy_enemy(100));
    c.start(cards::make_silent_starter_deck());
    c.player.block = 12;
    c.end_turn();
    CHECK(c.player.block == 0);
    CHECK(c.round == 2);
}

TEST(combat_enemy_phase_clears_enemy_block_each_turn) {
    Combat c{1};
    Enemy dummy = make_dummy_enemy(100);
    dummy.block = 7;
    c.enemies.push_back(std::move(dummy));
    c.start(cards::make_silent_starter_deck());
    c.end_player_turn();
    c.enemy_phase();
    CHECK(c.enemies[0].block == 0);
}

TEST(combat_can_play_returns_true_for_affordable_card) {
    Combat c{1};
    c.player.energy = 3;
    c.player.hand.push_back(cards::make_strike());
    CHECK(c.can_play(0));
}

TEST(combat_can_play_returns_false_when_no_energy) {
    Combat c{1};
    c.player.energy = 0;
    c.player.hand.push_back(cards::make_strike());
    CHECK(!c.can_play(0));
}

TEST(combat_can_play_returns_false_for_invalid_index) {
    Combat c{1};
    c.player.energy = 3;
    c.player.hand.push_back(cards::make_strike());
    CHECK(!c.can_play(-1));
    CHECK(!c.can_play(1));
    CHECK(!c.can_play(99));
}

TEST(combat_play_card_deducts_energy_and_moves_to_discard) {
    Combat c{1};
    c.player.energy = 3;
    c.enemies.push_back(make_dummy_enemy(100));
    c.player.hand.push_back(cards::make_strike());
    c.play_card(0, 0);
    CHECK(c.player.energy == 2);
    CHECK(c.player.hand.empty());
    CHECK(c.player.discard_pile.size() == 1u);
    CHECK(c.player.discard_pile[0].id == cards::IdStrike);
}

TEST(combat_play_card_no_op_when_unplayable) {
    Combat c{1};
    c.player.energy = 0;
    c.player.hand.push_back(cards::make_strike());
    c.play_card(0, -1);
    CHECK(c.player.energy == 0);
    CHECK(c.player.hand.size() == 1u);
    CHECK(c.player.discard_pile.empty());
}

TEST(combat_play_strike_kills_low_hp_enemy) {
    Combat c{1};
    c.player.energy = 3;
    c.enemies.push_back(make_dummy_enemy(5));
    c.player.hand.push_back(cards::make_strike());
    c.play_card(0, 0);
    CHECK(c.enemies[0].hp == 0);
    CHECK(c.combat_over);
}

TEST(combat_end_turn_discards_hand) {
    Combat c{1};
    c.enemies.push_back(make_dummy_enemy(100));
    c.start(cards::make_silent_starter_deck());
    size_t hand_before = c.player.hand.size();
    c.end_turn();
    CHECK(c.player.hand.size() == 5u);
    CHECK(c.player.discard_pile.size() >= hand_before);
    CHECK(total_cards_in_play(c) == 12);
}

TEST(combat_end_turn_advances_round) {
    Combat c{1};
    c.enemies.push_back(make_dummy_enemy(100));
    c.start(cards::make_silent_starter_deck());
    CHECK(c.round == 1);
    c.end_turn();
    CHECK(c.round == 2);
    c.end_turn();
    CHECK(c.round == 3);
}

TEST(combat_enemy_phase_executes_acts) {
    Combat c{1};
    Rng r(5);
    c.enemies.push_back(enemies::make_calcified_cultist(r));
    c.start(cards::make_silent_starter_deck());
    int hp_before = c.player.hp;
    c.end_turn();
    CHECK(c.player.hp == hp_before);
    int hp_after_t1 = c.player.hp;
    c.end_turn();
    CHECK(c.player.hp == hp_after_t1 - 9);
}

TEST(combat_enemy_phase_ticks_powers_grants_strength) {
    Combat c{1};
    Rng r(5);
    c.enemies.push_back(enemies::make_calcified_cultist(r));
    c.start(cards::make_silent_starter_deck());
    c.end_turn();
    CHECK(powers::amount(c.enemies[0].powers, PowerKind::Strength) == 0);
    CHECK(powers::amount(c.enemies[0].powers, PowerKind::Ritual) == 2);
    c.end_turn();
    CHECK(powers::amount(c.enemies[0].powers, PowerKind::Strength) == 2);
}

TEST(combat_enemy_phase_skips_dead_enemies) {
    Combat c{1};
    Enemy dead = make_dummy_enemy(0);
    dead.dark_strike_base = 99;
    Enemy alive = make_dummy_enemy(100);
    alive.dark_strike_base = 3;
    alive.current_move = MoveId::DarkStrike;
    alive.performed_first_move = true;
    c.enemies.push_back(std::move(dead));
    c.enemies.push_back(std::move(alive));
    c.start(cards::make_silent_starter_deck());
    int hp_before = c.player.hp;
    c.end_turn();
    CHECK(c.player.hp == hp_before - 3);
}

TEST(combat_player_dies_marks_combat_over) {
    Combat c{1};
    Enemy big = make_dummy_enemy(100);
    big.dark_strike_base = 1000;
    big.current_move = MoveId::DarkStrike;
    big.performed_first_move = true;
    c.enemies.push_back(std::move(big));
    c.start(cards::make_silent_starter_deck());
    c.end_turn();
    CHECK(c.player.hp == 0);
    CHECK(c.combat_over);
}

TEST(combat_killing_last_enemy_marks_combat_over) {
    Combat c{1};
    c.enemies.push_back(make_dummy_enemy(2));
    c.player.energy = 3;
    c.player.hand.push_back(cards::make_strike());
    c.play_card(0, 0);
    CHECK(c.enemies[0].hp == 0);
    CHECK(c.all_enemies_dead());
    CHECK(c.combat_over);
}

TEST(combat_reshuffle_when_draw_pile_empty) {
    Combat c{1};
    c.player.discard_pile.push_back(cards::make_strike());
    c.player.discard_pile.push_back(cards::make_defend());
    c.player.discard_pile.push_back(cards::make_neutralize());
    c.draw(3);
    CHECK(c.player.hand.size() == 3u);
    CHECK(c.player.discard_pile.empty());
    CHECK(c.player.draw_pile.empty());
}

TEST(combat_deck_size_invariant_across_round) {
    Combat c{1};
    c.enemies.push_back(make_dummy_enemy(100));
    c.start(cards::make_silent_starter_deck());
    CHECK(total_cards_in_play(c) == 12);
    c.end_turn();
    CHECK(total_cards_in_play(c) == 12);
    c.end_turn();
    CHECK(total_cards_in_play(c) == 12);
}

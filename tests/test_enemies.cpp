#include <vector>

#include "game/Combat.h"
#include "game/Enemies.h"
#include "game/Enemy.h"
#include "game/Player.h"
#include "game/Power.h"
#include "game/Powers.h"
#include "game/Rng.h"
#include "game/Types.h"
#include "test_helpers.h"
#include "test_runner.h"

TEST(enemy_calcified_factory_hp_in_range) {
    Rng r(1);
    for (int i = 0; i < 200; ++i) {
        Enemy e = enemies::make_calcified_cultist(r);
        CHECK(e.vitals.hp >= 38 && e.vitals.hp <= 41);
        CHECK(e.vitals.max_hp == e.vitals.hp);
        CHECK(e.dark_strike_base == 9);
        CHECK(e.ritual_amount == 2);
        CHECK(e.name == std::string("Calcified Cultist"));
        CHECK(e.current_move == MoveId::Incantation);
        CHECK(!e.performed_first_move);
        CHECK(e.vitals.powers.empty());
    }
}

TEST(enemy_damp_factory_hp_in_range) {
    Rng r(1);
    for (int i = 0; i < 200; ++i) {
        Enemy e = enemies::make_damp_cultist(r);
        CHECK(e.vitals.hp >= 51 && e.vitals.hp <= 53);
        CHECK(e.vitals.max_hp == e.vitals.hp);
        CHECK(e.dark_strike_base == 1);
        CHECK(e.ritual_amount == 5);
        CHECK(e.name == std::string("Damp Cultist"));
    }
}

TEST(enemy_state_machine_initial_state) {
    Rng r(1);
    Enemy e = enemies::make_calcified_cultist(r);
    CHECK(e.current_move == MoveId::Incantation);
    CHECK(!e.performed_first_move);
}

TEST(enemy_state_machine_first_roll_keeps_incantation) {
    Rng r(1);
    Enemy e = enemies::make_calcified_cultist(r);
    enemies::roll_next_move(e);
    CHECK(e.current_move == MoveId::Incantation);
    CHECK(e.performed_first_move);
}

TEST(enemy_state_machine_second_roll_transitions_to_dark_strike) {
    Rng r(1);
    Enemy e = enemies::make_calcified_cultist(r);
    enemies::roll_next_move(e);
    enemies::roll_next_move(e);
    CHECK(e.current_move == MoveId::DarkStrike);
}

TEST(enemy_state_machine_dark_strike_self_loop) {
    Rng r(1);
    Enemy e = enemies::make_damp_cultist(r);
    for (int i = 0; i < 5; ++i) enemies::roll_next_move(e);
    CHECK(e.current_move == MoveId::DarkStrike);
    enemies::roll_next_move(e);
    enemies::roll_next_move(e);
    enemies::roll_next_move(e);
    CHECK(e.current_move == MoveId::DarkStrike);
}

TEST(enemy_act_incantation_applies_ritual_calcified) {
    Rng r(1);
    Enemy e = enemies::make_calcified_cultist(r);
    Combat c = make_combat_with(std::move(e));
    enemies::act(c.enemies[0], c);
    CHECK(powers::amount(c.enemies[0].vitals.powers, PowerKind::Ritual) == 2);
    const Power* p = powers::find(c.enemies[0].vitals.powers, PowerKind::Ritual);
    CHECK(p != nullptr);
    CHECK(p->just_applied);
}

TEST(enemy_act_incantation_applies_ritual_damp) {
    Rng r(1);
    Enemy e = enemies::make_damp_cultist(r);
    Combat c = make_combat_with(std::move(e));
    enemies::act(c.enemies[0], c);
    CHECK(powers::amount(c.enemies[0].vitals.powers, PowerKind::Ritual) == 5);
}

TEST(enemy_act_dark_strike_calcified_deals_nine) {
    Rng r(1);
    Enemy e = enemies::make_calcified_cultist(r);
    e.current_move = MoveId::DarkStrike;
    e.performed_first_move = true;
    Combat c = make_combat_with(std::move(e));
    enemies::act(c.enemies[0], c);
    CHECK(c.player.vitals.hp == 61);
    CHECK(c.player.vitals.block == 0);
}

TEST(enemy_act_dark_strike_damp_deals_one) {
    Rng r(1);
    Enemy e = enemies::make_damp_cultist(r);
    e.current_move = MoveId::DarkStrike;
    e.performed_first_move = true;
    Combat c = make_combat_with(std::move(e));
    enemies::act(c.enemies[0], c);
    CHECK(c.player.vitals.hp == 69);
}

TEST(enemy_act_dark_strike_respects_block) {
    Rng r(1);
    Enemy e = enemies::make_calcified_cultist(r);
    e.current_move = MoveId::DarkStrike;
    e.performed_first_move = true;
    Combat c = make_combat_with(std::move(e));
    c.player.vitals.block = 12;
    enemies::act(c.enemies[0], c);
    CHECK(c.player.vitals.hp == 70);
    CHECK(c.player.vitals.block == 3);
}

TEST(enemy_act_dark_strike_with_strength_calcified) {
    Rng r(1);
    Enemy e = enemies::make_calcified_cultist(r);
    e.current_move = MoveId::DarkStrike;
    e.performed_first_move = true;
    powers::apply(e.vitals.powers, PowerKind::Strength, 2);
    Combat c = make_combat_with(std::move(e));
    enemies::act(c.enemies[0], c);
    CHECK(c.player.vitals.hp == 70 - 11);
}

TEST(enemy_calcified_damage_curve_first_three_turns) {
    Rng r(1);
    Enemy e = enemies::make_calcified_cultist(r);
    Combat c = make_combat_with(std::move(e));
    Enemy& cult = c.enemies[0];

    enemies::roll_next_move(cult);
    enemies::act(cult, c);
    powers::tick_at_turn_end(cult.vitals.powers);
    CHECK(c.player.vitals.hp == 70);
    CHECK(powers::amount(cult.vitals.powers, PowerKind::Strength) == 0);
    CHECK(powers::amount(cult.vitals.powers, PowerKind::Ritual) == 2);

    enemies::roll_next_move(cult);
    enemies::act(cult, c);
    powers::tick_at_turn_end(cult.vitals.powers);
    CHECK(c.player.vitals.hp == 70 - 9);
    CHECK(powers::amount(cult.vitals.powers, PowerKind::Strength) == 2);

    enemies::roll_next_move(cult);
    enemies::act(cult, c);
    powers::tick_at_turn_end(cult.vitals.powers);
    CHECK(c.player.vitals.hp == 70 - 9 - 11);
    CHECK(powers::amount(cult.vitals.powers, PowerKind::Strength) == 4);
}

TEST(enemy_damp_damage_curve_first_three_turns) {
    Rng r(1);
    Enemy e = enemies::make_damp_cultist(r);
    Combat c = make_combat_with(std::move(e));
    Enemy& cult = c.enemies[0];

    enemies::roll_next_move(cult);
    enemies::act(cult, c);
    powers::tick_at_turn_end(cult.vitals.powers);
    CHECK(c.player.vitals.hp == 70);
    CHECK(powers::amount(cult.vitals.powers, PowerKind::Strength) == 0);

    enemies::roll_next_move(cult);
    enemies::act(cult, c);
    powers::tick_at_turn_end(cult.vitals.powers);
    CHECK(c.player.vitals.hp == 70 - 1);
    CHECK(powers::amount(cult.vitals.powers, PowerKind::Strength) == 5);

    enemies::roll_next_move(cult);
    enemies::act(cult, c);
    powers::tick_at_turn_end(cult.vitals.powers);
    CHECK(c.player.vitals.hp == 70 - 1 - 6);
    CHECK(powers::amount(cult.vitals.powers, PowerKind::Strength) == 10);
}

TEST(enemy_dark_strike_with_weak_attacker_truncates) {
    Rng r(1);
    Enemy e = enemies::make_calcified_cultist(r);
    e.current_move = MoveId::DarkStrike;
    e.performed_first_move = true;
    powers::apply(e.vitals.powers, PowerKind::Weak, 1);
    Combat c = make_combat_with(std::move(e));
    enemies::act(c.enemies[0], c);
    CHECK(c.player.vitals.hp == 70 - 6);
}

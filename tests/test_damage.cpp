#include <vector>
#include "game/Damage.h"
#include "game/Power.h"
#include "game/Types.h"
#include "game/Vitals.h"
#include "test_runner.h"

TEST(damage_no_modifiers) {
    std::vector<Power> attacker;
    CHECK(damage::compute_outgoing(attacker, 6) == 6);
}

TEST(damage_strength_additive) {
    std::vector<Power> attacker = { Power{PowerKind::Strength, 2, false} };
    CHECK(damage::compute_outgoing(attacker, 6) == 8);
}

TEST(damage_weak_multiplicative_truncates) {
    std::vector<Power> attacker = { Power{PowerKind::Weak, 1, false} };
    CHECK(damage::compute_outgoing(attacker, 6) == 4);
}

TEST(damage_weak_then_strength_order) {
    std::vector<Power> attacker = {
        Power{PowerKind::Strength, 2, false},
        Power{PowerKind::Weak,     1, false}
    };
    CHECK(damage::compute_outgoing(attacker, 6) == 6);
}

TEST(damage_negative_strength_can_zero) {
    std::vector<Power> attacker = { Power{PowerKind::Strength, -10, false} };
    CHECK(damage::compute_outgoing(attacker, 6) == 0);
}

TEST(damage_apply_to_full_block) {
    Vitals v;
    v.hp = 50;
    v.block = 10;
    int loss = damage::apply_to_defender(v, 7);
    CHECK(loss == 0);
    CHECK(v.block == 3);
    CHECK(v.hp == 50);
}

TEST(damage_apply_partial_block) {
    Vitals v;
    v.hp = 50;
    v.block = 5;
    int loss = damage::apply_to_defender(v, 7);
    CHECK(loss == 2);
    CHECK(v.block == 0);
    CHECK(v.hp == 48);
}

TEST(damage_apply_no_block) {
    Vitals v;
    v.hp = 50;
    int loss = damage::apply_to_defender(v, 7);
    CHECK(loss == 7);
    CHECK(v.block == 0);
    CHECK(v.hp == 43);
}

TEST(damage_apply_overkill_clamps_to_zero) {
    Vitals v;
    v.hp = 5;
    int loss = damage::apply_to_defender(v, 100);
    CHECK(loss == 5);
    CHECK(v.hp == 0);
}

TEST(damage_apply_block_kills_remaining) {
    Vitals v;
    v.hp = 5;
    v.block = 5;
    int loss = damage::apply_to_defender(v, 100);
    CHECK(loss == 5);
    CHECK(v.block == 0);
    CHECK(v.hp == 0);
}

TEST(damage_zero_incoming_no_op) {
    Vitals v;
    v.hp = 50;
    v.block = 5;
    int loss = damage::apply_to_defender(v, 0);
    CHECK(loss == 0);
    CHECK(v.block == 5);
    CHECK(v.hp == 50);
}

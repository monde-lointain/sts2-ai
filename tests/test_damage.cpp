#include <vector>
#include "game/Damage.h"
#include "game/Power.h"
#include "game/Types.h"
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
    CHECK(damage::compute_outgoing(attacker, 6) == 4);   // 6*0.75 = 4.5 -> 4
}

TEST(damage_weak_then_strength_order) {
    // Strength applied additively first, then Weak multiplicative:
    // (6 + 2) * 0.75 = 6
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
    int block = 10, hp = 50;
    int loss = damage::apply_to_defender(block, hp, 7);
    CHECK(loss == 0);
    CHECK(block == 3);
    CHECK(hp == 50);
}

TEST(damage_apply_partial_block) {
    int block = 5, hp = 50;
    int loss = damage::apply_to_defender(block, hp, 7);
    CHECK(loss == 2);
    CHECK(block == 0);
    CHECK(hp == 48);
}

TEST(damage_apply_no_block) {
    int block = 0, hp = 50;
    int loss = damage::apply_to_defender(block, hp, 7);
    CHECK(loss == 7);
    CHECK(block == 0);
    CHECK(hp == 43);
}

TEST(damage_apply_overkill_clamps_to_zero) {
    int block = 0, hp = 5;
    int loss = damage::apply_to_defender(block, hp, 100);
    CHECK(loss == 5);
    CHECK(hp == 0);
}

TEST(damage_apply_block_kills_remaining) {
    int block = 5, hp = 5;
    int loss = damage::apply_to_defender(block, hp, 100);
    CHECK(loss == 5);
    CHECK(block == 0);
    CHECK(hp == 0);
}

TEST(damage_zero_incoming_no_op) {
    int block = 5, hp = 50;
    int loss = damage::apply_to_defender(block, hp, 0);
    CHECK(loss == 0);
    CHECK(block == 5);
    CHECK(hp == 50);
}

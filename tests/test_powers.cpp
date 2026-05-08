#include <vector>
#include "game/Power.h"
#include "game/Powers.h"
#include "game/Types.h"
#include "test_runner.h"

TEST(powers_find_returns_null_when_absent) {
    std::vector<Power> ps;
    CHECK(powers::find(ps, PowerKind::Weak) == nullptr);
}

TEST(powers_find_returns_pointer_when_present) {
    std::vector<Power> ps = { Power{PowerKind::Weak, 2, false} };
    Power* p = powers::find(ps, PowerKind::Weak);
    CHECK(p != nullptr);
    CHECK(p->amount == 2);
}

TEST(powers_amount_returns_zero_when_absent) {
    std::vector<Power> ps;
    CHECK(powers::amount(ps, PowerKind::Strength) == 0);
}

TEST(powers_amount_returns_value_when_present) {
    std::vector<Power> ps = { Power{PowerKind::Strength, 3, false} };
    CHECK(powers::amount(ps, PowerKind::Strength) == 3);
}

TEST(powers_apply_creates_new_power) {
    std::vector<Power> ps;
    powers::apply(ps, PowerKind::Weak, 1);
    CHECK(ps.size() == 1);
    CHECK(ps[0].kind == PowerKind::Weak);
    CHECK(ps[0].amount == 1);
    CHECK(!ps[0].just_applied);
}

TEST(powers_apply_stacks_existing) {
    std::vector<Power> ps = { Power{PowerKind::Weak, 1, false} };
    powers::apply(ps, PowerKind::Weak, 2);
    CHECK(ps.size() == 1);
    CHECK(ps[0].amount == 3);
}

TEST(powers_apply_strength_can_stack_negative) {
    std::vector<Power> ps = { Power{PowerKind::Strength, 2, false} };
    powers::apply(ps, PowerKind::Strength, -5);
    CHECK(ps[0].amount == -3);
}

TEST(powers_apply_ritual_sets_just_applied_on_creation) {
    std::vector<Power> ps;
    powers::apply(ps, PowerKind::Ritual, 2);
    CHECK(ps[0].just_applied);
}

TEST(powers_apply_ritual_sets_just_applied_on_stack) {
    std::vector<Power> ps = { Power{PowerKind::Ritual, 2, false} };
    powers::apply(ps, PowerKind::Ritual, 1);
    CHECK(ps[0].amount == 3);
    CHECK(ps[0].just_applied);
}

TEST(powers_tick_weak_decrements) {
    std::vector<Power> ps = { Power{PowerKind::Weak, 2, false} };
    powers::tick_at_turn_end(ps);
    CHECK(ps.size() == 1);
    CHECK(ps[0].amount == 1);
}

TEST(powers_tick_weak_at_one_removes) {
    std::vector<Power> ps = { Power{PowerKind::Weak, 1, false} };
    powers::tick_at_turn_end(ps);
    CHECK(ps.empty());
}

TEST(powers_tick_strength_unchanged) {
    std::vector<Power> ps = { Power{PowerKind::Strength, 3, false} };
    powers::tick_at_turn_end(ps);
    CHECK(ps.size() == 1);
    CHECK(ps[0].amount == 3);
}

TEST(powers_tick_ritual_just_applied_skips_strength) {
    std::vector<Power> ps = { Power{PowerKind::Ritual, 2, true} };
    powers::tick_at_turn_end(ps);
    CHECK(ps.size() == 1);
    CHECK(ps[0].kind == PowerKind::Ritual);
    CHECK(ps[0].amount == 2);
    CHECK(!ps[0].just_applied);  // flag cleared
    CHECK(powers::amount(ps, PowerKind::Strength) == 0);
}

TEST(powers_tick_ritual_normal_grants_strength) {
    std::vector<Power> ps = { Power{PowerKind::Ritual, 2, false} };
    powers::tick_at_turn_end(ps);
    CHECK(powers::amount(ps, PowerKind::Strength) == 2);
    CHECK(powers::amount(ps, PowerKind::Ritual) == 2);
}

TEST(powers_tick_ritual_stacks_with_existing_strength) {
    std::vector<Power> ps = {
        Power{PowerKind::Ritual, 5, false},
        Power{PowerKind::Strength, 1, false}
    };
    powers::tick_at_turn_end(ps);
    CHECK(powers::amount(ps, PowerKind::Strength) == 6);
}

TEST(powers_tick_combined_ritual_and_weak) {
    std::vector<Power> ps = {
        Power{PowerKind::Ritual, 2, false},
        Power{PowerKind::Weak,   2, false}
    };
    powers::tick_at_turn_end(ps);
    CHECK(powers::amount(ps, PowerKind::Strength) == 2);
    CHECK(powers::amount(ps, PowerKind::Weak) == 1);
}

// Tests for include/sts2/game/stat.h.
//
// Covers: default construction, explicit int construction with +/-1e9
// saturation, value(), pack16() 16-bit view, operator+= / operator-= with
// saturating arithmetic (allows negative), and all six comparison operators.

#include <gtest/gtest.h>

#include "sts2/game/stat.h"

namespace {

using sts2::game::Stat;

// --- Construction ---

TEST(Stat, DefaultConstruct_IsZero) {
  Stat s;
  EXPECT_EQ(s.value(), 0);
  EXPECT_EQ(s.pack16(), 0U);
}

TEST(Stat, ExplicitInt_StoresCorrectValue) {
  EXPECT_EQ(Stat{5}.value(), 5);
  EXPECT_EQ(Stat{255}.value(), 255);
  EXPECT_EQ(Stat{0}.value(), 0);
}

TEST(Stat, ConstructorClampsPositive) {
  EXPECT_EQ(Stat{2'000'000'000}.value(), Stat::kMaxClamp);
}

TEST(Stat, ConstructorClampsNegative) {
  EXPECT_EQ(Stat{-2'000'000'000}.value(), -Stat::kMaxClamp);
}

// --- pack16 16-bit view ---

TEST(Stat, Pack16_InRange) {
  EXPECT_EQ(Stat{50}.pack16(), static_cast<uint16_t>(50));
  EXPECT_EQ(Stat{255}.pack16(), static_cast<uint16_t>(255));
  EXPECT_EQ(Stat{0}.pack16(), static_cast<uint16_t>(0));
  EXPECT_EQ(Stat{1000}.pack16(), static_cast<uint16_t>(1000));
  EXPECT_EQ(Stat{65535}.pack16(), static_cast<uint16_t>(65535));
}

// --- Arithmetic ---

TEST(Stat, PlusEquals_IncrementsValue) {
  Stat s{10};
  s += 5;
  EXPECT_EQ(s.value(), 15);
}

TEST(Stat, MinusEquals_DecrementsValue) {
  Stat s{10};
  s -= 3;
  EXPECT_EQ(s.value(), 7);
}

TEST(Stat, PlusEquals_Zero_NoChange) {
  Stat s{7};
  s += 0;
  EXPECT_EQ(s.value(), 7);
}

TEST(Stat, MinusEquals_ToZero) {
  Stat s{5};
  s -= 5;
  EXPECT_EQ(s.value(), 0);
}

// Saturating arithmetic: no wrap at byte boundary; mirrors Godot
// PowerModel.SetAmount semantics with Math.Clamp(value, -1e9, +1e9).

TEST(Stat, PlusEquals_NoWrapAt256) {
  Stat s{200};
  s += 100;
  EXPECT_EQ(s.value(), 300);
}

TEST(Stat, MinusEquals_AllowsNegative) {
  Stat s{0};
  s -= 5;
  EXPECT_EQ(s.value(), -5);
}

TEST(Stat, PlusEquals_SaturatesPositive) {
  Stat s{Stat::kMaxClamp};
  s += 1;
  EXPECT_EQ(s.value(), Stat::kMaxClamp);
}

TEST(Stat, MinusEquals_SaturatesNegative) {
  Stat s{-Stat::kMaxClamp};
  s -= 1;
  EXPECT_EQ(s.value(), -Stat::kMaxClamp);
}

// --- Equality ---

TEST(Stat, Equality_SameValue_Equal) { EXPECT_EQ(Stat{10}, Stat{10}); }

TEST(Stat, Equality_DifferentValue_NotEqual) { EXPECT_NE(Stat{10}, Stat{11}); }

// --- Ordering ---

TEST(Stat, LessThan) {
  EXPECT_LT(Stat{3}, Stat{4});
  EXPECT_FALSE(Stat{4} < Stat{3});
  EXPECT_FALSE(Stat{4} < Stat{4});
}

TEST(Stat, GreaterThan) {
  EXPECT_GT(Stat{5}, Stat{4});
  EXPECT_FALSE(Stat{4} > Stat{5});
  EXPECT_FALSE(Stat{4} > Stat{4});
}

TEST(Stat, LessEqual) {
  EXPECT_LE(Stat{3}, Stat{4});
  EXPECT_LE(Stat{4}, Stat{4});
  EXPECT_FALSE(Stat{5} <= Stat{4});
}

TEST(Stat, GreaterEqual) {
  EXPECT_GE(Stat{5}, Stat{4});
  EXPECT_GE(Stat{4}, Stat{4});
  EXPECT_FALSE(Stat{3} >= Stat{4});
}

// --- Type isolation: Stat must not implicitly convert from int ---

static_assert(!std::is_convertible_v<int, Stat>,
              "Stat must not be implicitly constructible from int");

}  // namespace

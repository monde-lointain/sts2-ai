// Tests for include/sts2/game/stat.h.
//
// Covers: default construction, explicit int construction, value()/raw(),
// operator+= / operator-=, all six comparison operators, and uint8_t
// wraparound behaviour.

#include <gtest/gtest.h>

#include "sts2/game/stat.h"

namespace {

using sts2::game::Stat;

// --- Construction ---

TEST(Stat, DefaultConstruct_IsZero) {
  Stat s;
  EXPECT_EQ(s.value(), 0);
  EXPECT_EQ(s.raw(), 0u);
}

TEST(Stat, ExplicitInt_StoresCorrectValue) {
  EXPECT_EQ(Stat{5}.value(), 5);
  EXPECT_EQ(Stat{255}.value(), 255);
  EXPECT_EQ(Stat{0}.value(), 0);
}

TEST(Stat, Raw_ReturnsSameAsUint8) {
  EXPECT_EQ(Stat{42}.raw(), static_cast<uint8_t>(42));
  EXPECT_EQ(Stat{255}.raw(), static_cast<uint8_t>(255));
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

// uint8_t arithmetic wraps mod 256. Document the behaviour rather than assert
// it's "correct" — callers must not rely on wrap for hp/energy semantics.
TEST(Stat, PlusEquals_WrapsAt256) {
  Stat s{255};
  s += 1;
  EXPECT_EQ(s.value(), 0);  // wraps to 0
}

TEST(Stat, MinusEquals_WrapsBelow0) {
  Stat s{0};
  s -= 1;
  EXPECT_EQ(s.value(), 255);  // wraps to 255
}

// --- Equality ---

TEST(Stat, Equality_SameValue_Equal) {
  EXPECT_EQ(Stat{10}, Stat{10});
}

TEST(Stat, Equality_DifferentValue_NotEqual) {
  EXPECT_NE(Stat{10}, Stat{11});
}

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

// Tests for include/sts2/game/index_types.h.
//
// Covers EnemySlot and HandIndex: construction, valid/raw/none, in_range/at
// against a sample container, equality, and type isolation.

#include <gtest/gtest.h>

#include <array>
#include <vector>

#include "sts2/game/index_types.h"

namespace {

using sts2::game::EnemySlot;
using sts2::game::HandIndex;

// --- EnemySlot ---

TEST(EnemySlot, ValidPositive) {
  EXPECT_TRUE(EnemySlot{0}.valid());
  EXPECT_TRUE(EnemySlot{3}.valid());
}

TEST(EnemySlot, InvalidNegative) {
  EXPECT_FALSE(EnemySlot{-1}.valid());
  EXPECT_FALSE(EnemySlot{-99}.valid());
}

TEST(EnemySlot, NoneIsInvalid) {
  EXPECT_FALSE(EnemySlot::none().valid());
  EXPECT_EQ(EnemySlot::none().raw(), -1);
}

TEST(EnemySlot, Raw) {
  EXPECT_EQ(EnemySlot{0}.raw(), 0);
  EXPECT_EQ(EnemySlot{5}.raw(), 5);
}

TEST(EnemySlot, Equality) {
  EXPECT_EQ(EnemySlot{2}, EnemySlot{2});
  EXPECT_NE(EnemySlot{2}, EnemySlot{3});
  EXPECT_EQ(EnemySlot::none(), EnemySlot{-1});
}

TEST(EnemySlot, InRange_Vector) {
  const std::vector<int> v = {10, 20, 30};
  EXPECT_TRUE(EnemySlot{0}.in_range(v));
  EXPECT_TRUE(EnemySlot{2}.in_range(v));
  EXPECT_FALSE(EnemySlot{3}.in_range(v));
  EXPECT_FALSE(EnemySlot{-1}.in_range(v));
  EXPECT_FALSE(EnemySlot::none().in_range(v));
}

TEST(EnemySlot, At_Vector) {
  std::vector<int> v = {10, 20, 30};
  EXPECT_EQ(EnemySlot{1}.at(v), 20);
  EnemySlot{0}.at(v) = 99;
  EXPECT_EQ(v[0], 99);
}

TEST(EnemySlot, At_Array) {
  std::array<int, 3> a = {1, 2, 3};
  EXPECT_EQ(EnemySlot{2}.at(a), 3);
}

// --- HandIndex ---

TEST(HandIndex, ValidPositive) {
  EXPECT_TRUE(HandIndex{0}.valid());
  EXPECT_TRUE(HandIndex{7}.valid());
}

TEST(HandIndex, InvalidNegative) {
  EXPECT_FALSE(HandIndex{-1}.valid());
}

TEST(HandIndex, NoneIsInvalid) {
  EXPECT_FALSE(HandIndex::none().valid());
  EXPECT_EQ(HandIndex::none().raw(), -1);
}

TEST(HandIndex, Raw) {
  EXPECT_EQ(HandIndex{0}.raw(), 0);
  EXPECT_EQ(HandIndex{4}.raw(), 4);
}

TEST(HandIndex, Equality) {
  EXPECT_EQ(HandIndex{1}, HandIndex{1});
  EXPECT_NE(HandIndex{1}, HandIndex{2});
  EXPECT_EQ(HandIndex::none(), HandIndex{-1});
}

TEST(HandIndex, InRange_Vector) {
  const std::vector<int> v = {5, 6};
  EXPECT_TRUE(HandIndex{0}.in_range(v));
  EXPECT_TRUE(HandIndex{1}.in_range(v));
  EXPECT_FALSE(HandIndex{2}.in_range(v));
  EXPECT_FALSE(HandIndex{-1}.in_range(v));
}

TEST(HandIndex, At_Vector) {
  std::vector<int> v = {5, 6};
  EXPECT_EQ(HandIndex{0}.at(v), 5);
  HandIndex{1}.at(v) = 42;
  EXPECT_EQ(v[1], 42);
}

// --- Type isolation: EnemySlot and HandIndex must not be interconvertible ---

static_assert(!std::is_convertible_v<EnemySlot, HandIndex>,
              "EnemySlot must not implicitly convert to HandIndex");
static_assert(!std::is_convertible_v<HandIndex, EnemySlot>,
              "HandIndex must not implicitly convert to EnemySlot");

}  // namespace

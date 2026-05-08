// tests/game/test_helpers.h
#pragma once

#include <array>
#include <algorithm>
#include <cstddef>
#include <vector>

#include <gtest/gtest.h>

#include "game/Power.h"
#include "game/Types.h"

namespace sts2::tests::helpers {

template <typename T, std::size_t N>
void ExpectShuffleMatchesPinned(const std::vector<T>& shuffled,
                                const std::array<T, N>& pinned,
                                const std::vector<T>& original) {
    ASSERT_EQ(shuffled.size(), pinned.size());
    for (std::size_t i = 0; i < shuffled.size(); ++i) {
        EXPECT_EQ(shuffled[i], pinned[i]) << "mismatch at index " << i;
    }
    EXPECT_TRUE(std::is_permutation(shuffled.begin(), shuffled.end(),
                                    original.begin(), original.end()));
}

// Compact constructor for Power test data; `just_applied` defaults to false
// because that matches every spec input except T-PWR-105.
inline Power MakePower(PowerKind kind, int amount, bool just_applied = false) {
    return Power{kind, amount, just_applied};
}

// Element-wise comparison of two Power vectors with diagnostic indexing.
// Implemented as a function (not a macro) so failures land on this line and
// the debugger can step into it.
inline void ExpectPowersEq(const std::vector<Power>& actual,
                           const std::vector<Power>& expected) {
    ASSERT_EQ(actual.size(), expected.size())
        << "powers vector size mismatch";
    for (std::size_t i = 0; i < expected.size(); ++i) {
        EXPECT_EQ(static_cast<int>(actual[i].kind),
                  static_cast<int>(expected[i].kind))
            << "kind mismatch at index " << i;
        EXPECT_EQ(actual[i].amount, expected[i].amount)
            << "amount mismatch at index " << i;
        EXPECT_EQ(actual[i].just_applied, expected[i].just_applied)
            << "just_applied mismatch at index " << i;
    }
}

}

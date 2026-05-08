// tests/game/test_helpers.h
#pragma once

#include <array>
#include <algorithm>
#include <vector>

#include <gtest/gtest.h>

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

}

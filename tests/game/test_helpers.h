// tests/game/test_helpers.h
#pragma once

#include <array>
#include <algorithm>
#include <cstddef>
#include <ostream>
#include <vector>

#include <gtest/gtest.h>

#include "game/Power.h"
#include "game/Types.h"

// gtest customization point: print PowerKind by name in failure messages.
// Test-only; kept out of production headers. `PrintTo` must live in the same
// namespace as the type for gtest's ADL lookup; `PowerKind` is at global
// scope (per src/game/Types.h), so this overload sits at global scope too.
inline void PrintTo(PowerKind k, std::ostream* os) {
    switch (k) {
        case PowerKind::Weak:     *os << "Weak";     break;
        case PowerKind::Strength: *os << "Strength"; break;
        case PowerKind::Ritual:   *os << "Ritual";   break;
    }
}

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
inline constexpr Power MakePower(PowerKind kind, int amount,
                                 bool just_applied = false) {
    return Power{kind, amount, just_applied};
}

// Element-wise comparison of two Power vectors with diagnostic indexing.
// Implemented as a function (not a macro) so failures land on this line and
// the debugger can step into it. SCOPED_TRACE attaches call-site context to
// failures so the gtest report includes vector sizes alongside the helper line.
inline void ExpectPowersEq(const std::vector<Power>& actual,
                           const std::vector<Power>& expected) {
    SCOPED_TRACE(::testing::Message() << "ExpectPowersEq actual.size()="
                                      << actual.size()
                                      << " expected.size()=" << expected.size());
    ASSERT_EQ(actual.size(), expected.size())
        << "powers vector size mismatch";
    for (std::size_t i = 0; i < expected.size(); ++i) {
        EXPECT_EQ(actual[i].kind, expected[i].kind)
            << "kind mismatch at index " << i;
        EXPECT_EQ(actual[i].amount, expected[i].amount)
            << "amount mismatch at index " << i;
        EXPECT_EQ(actual[i].just_applied, expected[i].just_applied)
            << "just_applied mismatch at index " << i;
    }
}

}  // namespace sts2::tests::helpers

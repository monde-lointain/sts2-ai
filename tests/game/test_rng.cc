// Tests for src/game/rng.{h,cc}.
// Spec: docs/test-plan/02-test-specifications.md §5 (T-RNG-005..070).
//
// Pinned-value tests (T-RNG-055, T-RNG-060) compare against
// tests::seeds::kShuffle_2 / kShuffle_10. The pinned values are toolchain-
// specific (clang-cl + MSVC STL); see tests/seeds/expected_values.h.

#include <array>
#include <climits>
#include <cstdint>
#include <limits>
#include <vector>

#include <gtest/gtest.h>

#include "sts2/game/rng.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/expected_values.h"

namespace {

using sts2::tests::helpers::ExpectShuffleMatchesPinned;
using sts2::tests::seeds::kRngFirstUniform_0_INTMAX;
using sts2::tests::seeds::kRngSecondUniform_0_INTMAX;
using sts2::tests::seeds::kRngSeq_0_9;
using sts2::tests::seeds::kRngTestSeed;
using sts2::tests::seeds::kShuffle_2;
using sts2::tests::seeds::kShuffle_10;

// -------------------------------------------------------------------------
// 5.1  Rng::Rng(uint64_t seed)
// -------------------------------------------------------------------------

// T-RNG-005 — BP, EP — Deterministic seeding.
TEST(RngConstructor, T_RNG_005_DeterministicSeeding) {
    constexpr std::uint64_t seed = 0xDEADBEEFCAFEULL;
    Rng a{seed};
    Rng b{seed};
    std::array<int, 10> sa{};
    std::array<int, 10> sb{};
    for (int i = 0; i < 10; ++i) {
        sa[static_cast<std::size_t>(i)] = a.uniform_int(0, 1'000'000);
        sb[static_cast<std::size_t>(i)] = b.uniform_int(0, 1'000'000);
    }
    EXPECT_EQ(sa, sb);
}

// T-RNG-007 — DF, EG — Pinned reference sequence locks toolchain output.
// Strategy: Data flow (seed → engine_ → uniform_int output); error guessing (toolchain regression).
TEST(RngConstructor, T_RNG_007_PinnedReferenceSequence) {
    Rng r{kRngTestSeed};
    for (std::size_t i = 0; i < kRngSeq_0_9.size(); ++i) {
        EXPECT_EQ(r.uniform_int(0, 9), kRngSeq_0_9[i]) << "mismatch at index " << i;
    }
}

// T-RNG-008 — DF, EG — Pinned wide-range determinism reference.
TEST(RngConstructor, T_RNG_008_PinnedWideRange) {
    Rng r{kRngTestSeed};
    EXPECT_EQ(r.uniform_int(0, std::numeric_limits<int>::max()), kRngFirstUniform_0_INTMAX);
    EXPECT_EQ(r.uniform_int(0, std::numeric_limits<int>::max()), kRngSecondUniform_0_INTMAX);
}

// T-RNG-010 — EG — Differentiated seeds yield different sequences.
TEST(RngConstructor, T_RNG_010_DifferentSeedsDifferSomewhere) {
    Rng a{0};
    Rng b{1};
    std::array<int, 50> sa{};
    std::array<int, 50> sb{};
    for (int i = 0; i < 50; ++i) {
        sa[static_cast<std::size_t>(i)] = a.uniform_int(0, 100);
        sb[static_cast<std::size_t>(i)] = b.uniform_int(0, 100);
    }
    EXPECT_NE(sa, sb);
}

// -------------------------------------------------------------------------
// 5.2  int Rng::uniform_int(int lo, int hi)
// -------------------------------------------------------------------------

// T-RNG-015 — BP, EP — Singleton range returns the value.
TEST(RngUniformInt, T_RNG_015_SingletonRange) {
    Rng r{kRngTestSeed};
    for (int i = 0; i < 100; ++i) {
        EXPECT_EQ(r.uniform_int(5, 5), 5);
    }
}

// T-RNG-020 — EP — Non-negative range. Both endpoints observed.
TEST(RngUniformInt, T_RNG_020_NonNegativeRangeEndpointsObserved) {
    Rng r{1};
    bool saw_lo = false;
    bool saw_hi = false;
    for (int i = 0; i < 1000; ++i) {
        const int x = r.uniform_int(0, 9);
        ASSERT_GE(x, 0);
        ASSERT_LE(x, 9);
        if (x == 0) saw_lo = true;
        if (x == 9) saw_hi = true;
    }
    EXPECT_TRUE(saw_lo);
    EXPECT_TRUE(saw_hi);
}

// T-RNG-025 — EP — Negative range. Both endpoints observed.
TEST(RngUniformInt, T_RNG_025_NegativeRangeEndpointsObserved) {
    Rng r{kRngTestSeed};
    bool saw_lo = false;
    bool saw_hi = false;
    for (int i = 0; i < 1000; ++i) {
        const int x = r.uniform_int(-10, -1);
        ASSERT_GE(x, -10);
        ASSERT_LE(x, -1);
        if (x == -10) saw_lo = true;
        if (x == -1)  saw_hi = true;
    }
    EXPECT_TRUE(saw_lo);
    EXPECT_TRUE(saw_hi);
}

// T-RNG-030 — EP — Mixed-sign range. 0 observed.
TEST(RngUniformInt, T_RNG_030_MixedSignRangeZeroObserved) {
    Rng r{kRngTestSeed};
    bool saw_zero = false;
    for (int i = 0; i < 1000; ++i) {
        const int x = r.uniform_int(-5, 5);
        ASSERT_GE(x, -5);
        ASSERT_LE(x, 5);
        if (x == 0) saw_zero = true;
    }
    EXPECT_TRUE(saw_zero);
}

// T-RNG-035 — BV — Maximum-width positive range.
// Statistical assertion: ≥ 1 sample > INT_MAX/2 across 100 calls.
// Probability of all samples ≤ INT_MAX/2 is ~2^-100; negligible flake.
TEST(RngUniformInt, T_RNG_035_MaxWidthPositiveRange) {
    Rng r{kRngTestSeed};
    bool saw_upper_half = false;
    for (int i = 0; i < 100; ++i) {
        const int x = r.uniform_int(0, INT_MAX);
        ASSERT_GE(x, 0);
        ASSERT_LE(x, INT_MAX);
        if (x > INT_MAX / 2) saw_upper_half = true;
    }
    EXPECT_TRUE(saw_upper_half);
}

// T-RNG-040 — BV — Maximum-width negative bound.
// Statistical assertion: ≥ 1 sample < INT_MIN/2 across 100 calls.
// Probability of all samples ≥ INT_MIN/2 is ~2^-100; negligible flake.
TEST(RngUniformInt, T_RNG_040_MaxWidthNegativeBound) {
    Rng r{kRngTestSeed};
    int samples_below_half = 0;
    for (int i = 0; i < 100; ++i) {
        const int x = r.uniform_int(INT_MIN, 0);
        ASSERT_LE(x, 0);
        ASSERT_GE(x, INT_MIN);
        if (x < INT_MIN / 2) ++samples_below_half;
    }
    EXPECT_GE(samples_below_half, 1) << "Probability of all samples >= INT_MIN/2 is ~2^-100; negligible flake.";
}

// -------------------------------------------------------------------------
// 5.3  template<T> void Rng::shuffle(std::vector<T>&)
// -------------------------------------------------------------------------

// T-RNG-045 — BP, BV — Empty vector. (D1 TRUE.)
TEST(RngShuffle, T_RNG_045_EmptyVector) {
    Rng r{kRngTestSeed};
    std::vector<int> v;
    r.shuffle(v);
    EXPECT_TRUE(v.empty());
}

// T-RNG-050 — BV — Single element. (D1 TRUE on boundary.)
TEST(RngShuffle, T_RNG_050_SingleElement) {
    Rng r{kRngTestSeed};
    std::vector<int> v{42};
    r.shuffle(v);
    ASSERT_EQ(v.size(), 1u);
    EXPECT_EQ(v[0], 42);
}

// T-RNG-055 — BP, BV — Two elements; pinned to kShuffle_2. (D1 FALSE; D2 single iter.)
TEST(RngShuffle, T_RNG_055_TwoElementsMatchesPinned) {
    Rng r{kRngTestSeed};
    const std::vector<int> original{1, 2};
    std::vector<int> v = original;
    r.shuffle(v);
    ExpectShuffleMatchesPinned(v, kShuffle_2, original);
}

// T-RNG-060 — DF — Ten elements; pinned to kShuffle_10.
TEST(RngShuffle, T_RNG_060_TenElementsMatchesPinned) {
    Rng r{kRngTestSeed};
    const std::vector<int> original{0, 1, 2, 3, 4, 5, 6, 7, 8, 9};
    std::vector<int> v = original;
    r.shuffle(v);
    ExpectShuffleMatchesPinned(v, kShuffle_10, original);
}

// T-RNG-065 — EG — Determinism across two Rngs with same seed.
TEST(RngShuffle, T_RNG_065_DeterminismAcrossSameSeed) {
    Rng a{kRngTestSeed};
    Rng b{kRngTestSeed};
    std::vector<int> va{0, 1, 2, 3, 4, 5, 6, 7, 8, 9};
    std::vector<int> vb{0, 1, 2, 3, 4, 5, 6, 7, 8, 9};
    a.shuffle(va);
    b.shuffle(vb);
    EXPECT_EQ(va, vb);
}

// T-RNG-070 — EG — Successive shuffles consume engine state.
TEST(RngShuffle, T_RNG_070_SuccessiveShufflesConsumeState) {
    Rng a{kRngTestSeed};
    std::vector<int> v1{0, 1, 2, 3, 4, 5, 6, 7, 8, 9};
    std::vector<int> v2{0, 1, 2, 3, 4, 5, 6, 7, 8, 9};
    a.shuffle(v1);
    a.shuffle(v2);
    EXPECT_NE(v1, v2);
}

}  // namespace

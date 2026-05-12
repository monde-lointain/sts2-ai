// Tests for src/app/args.{h,cc} — main.cc helpers hoisted per §2.1.
// Spec: docs/test-plan/02-test-specifications.md §13.1 (T-MAIN-005..045),
// §13.2 (T-MAIN-050..085), §13.3 (T-MAIN-090).
//
// `parse_args` was refactored to take `std::ostream& err` (per §13.2 addendum)
// so error-path tests assert on captured strings via std::ostringstream
// rather than freopen-ing stderr.

#include <gmock/gmock.h>
#include <gtest/gtest.h>

#include <array>
#include <cstdint>
#include <limits>
#include <sstream>
#include <string>
#include <vector>

#include "sts2/app/args.h"

namespace {

using sts2::app::parse_args;
using sts2::app::parse_uint64;
using sts2::app::random_seed;
using ::testing::HasSubstr;

// Build a char* vector from string literals; the strings live in `storage`
// (caller-owned) so the returned pointers stay valid for the test scope.
// Callers pass a pre-built std::vector<std::string>& as backing storage.
std::vector<char*> make_argv(std::vector<std::string>& storage) {
  std::vector<char*> argv;
  argv.reserve(storage.size());
  for (auto& s : storage) {
    argv.push_back(s.data());
  }
  return argv;
}

// -------------------------------------------------------------------------
// 13.1  parse_uint64  (T-MAIN-005..045)
// -------------------------------------------------------------------------

// T-MAIN-005 — BP, BV — Empty string → false. D1 TRUE.
TEST(AppParseUint64, T_MAIN_005_EmptyReturnsFalse) {
  std::uint64_t v = 999;  // sentinel: should not be written
  EXPECT_FALSE(parse_uint64("", v));
}

// T-MAIN-010 — BP — "0" → true, out=0.
TEST(AppParseUint64, T_MAIN_010_ZeroParses) {
  std::uint64_t v = 999;
  EXPECT_TRUE(parse_uint64("0", v));
  EXPECT_EQ(v, 0U);
}

// T-MAIN-015 — BP, EP — "42" → true, out=42.
TEST(AppParseUint64, T_MAIN_015_FortyTwoParses) {
  std::uint64_t v = 0;
  EXPECT_TRUE(parse_uint64("42", v));
  EXPECT_EQ(v, 42U);
}

// T-MAIN-020 — BV — UINT64_MAX as decimal string → true, out=UINT64_MAX.
TEST(AppParseUint64, T_MAIN_020_Uint64MaxParses) {
  std::uint64_t v = 0;
  EXPECT_TRUE(parse_uint64("18446744073709551615", v));
  EXPECT_EQ(v, std::numeric_limits<std::uint64_t>::max());
}

// T-MAIN-025 — BV — UINT64_MAX + 1 → false (overflow detected via D4).
TEST(AppParseUint64, T_MAIN_025_OverflowReturnsFalse) {
  std::uint64_t v = 0;
  EXPECT_FALSE(parse_uint64("18446744073709551616", v));
}

// T-MAIN-030 — BP — "12a" → false (D3 right-op TRUE on trailing char).
TEST(AppParseUint64, T_MAIN_030_TrailingLetterReturnsFalse) {
  std::uint64_t v = 0;
  EXPECT_FALSE(parse_uint64("12a", v));
}

// T-MAIN-035 — EG — "a12" → false (D3 left-op TRUE on first char).
TEST(AppParseUint64, T_MAIN_035_LeadingLetterReturnsFalse) {
  std::uint64_t v = 0;
  EXPECT_FALSE(parse_uint64("a12", v));
}

// T-MAIN-040 — EG — "-1" → false ('-' is not in '0'..'9').
TEST(AppParseUint64, T_MAIN_040_NegativeSignReturnsFalse) {
  std::uint64_t v = 0;
  EXPECT_FALSE(parse_uint64("-1", v));
}

// T-MAIN-045 — EG — Leading whitespace " 5" → false (caller is expected to
// trim).
TEST(AppParseUint64, T_MAIN_045_LeadingWhitespaceReturnsFalse) {
  std::uint64_t v = 0;
  EXPECT_FALSE(parse_uint64(" 5", v));
}

// -------------------------------------------------------------------------
// 13.2  parse_args  (T-MAIN-050..085)
// -------------------------------------------------------------------------

// T-MAIN-050 — BP — No args → returns true, seed_provided=false.
TEST(AppParseArgs, T_MAIN_050_NoArgsOk) {
  std::vector<std::string> storage{"prog"};
  auto argv = make_argv(storage);
  std::uint64_t seed = 12345;  // sentinel
  bool seed_provided = true;
  std::ostringstream err;

  EXPECT_TRUE(parse_args(static_cast<int>(argv.size()), argv.data(), seed,
                         seed_provided, err));
  EXPECT_FALSE(seed_provided);
  EXPECT_TRUE(err.str().empty());
}

// T-MAIN-055 — BP — `[prog, --seed, 42]` → ok, seed_provided, seed=42.
TEST(AppParseArgs, T_MAIN_055_SeedFortyTwo) {
  std::vector<std::string> storage{"prog", "--seed", "42"};
  auto argv = make_argv(storage);
  std::uint64_t seed = 0;
  bool seed_provided = false;
  std::ostringstream err;

  EXPECT_TRUE(parse_args(static_cast<int>(argv.size()), argv.data(), seed,
                         seed_provided, err));
  EXPECT_TRUE(seed_provided);
  EXPECT_EQ(seed, 42U);
  EXPECT_TRUE(err.str().empty());
}

// T-MAIN-060 — BP, EG — `[prog, --seed]` (missing value) → false; err mentions
// "--seed requires a value".
TEST(AppParseArgs, T_MAIN_060_MissingValueErrors) {
  std::vector<std::string> storage{"prog", "--seed"};
  auto argv = make_argv(storage);
  std::uint64_t seed = 0;
  bool seed_provided = false;
  std::ostringstream err;

  EXPECT_FALSE(parse_args(static_cast<int>(argv.size()), argv.data(), seed,
                          seed_provided, err));
  EXPECT_THAT(err.str(), HasSubstr("--seed requires a value"));
}

// T-MAIN-065 — BP, EG — `[prog, --seed, abc]` → false; err mentions
// "is not a valid uint64".
TEST(AppParseArgs, T_MAIN_065_BadValueErrors) {
  std::vector<std::string> storage{"prog", "--seed", "abc"};
  auto argv = make_argv(storage);
  std::uint64_t seed = 0;
  bool seed_provided = false;
  std::ostringstream err;

  EXPECT_FALSE(parse_args(static_cast<int>(argv.size()), argv.data(), seed,
                          seed_provided, err));
  EXPECT_THAT(err.str(), HasSubstr("is not a valid uint64"));
}

// T-MAIN-070 — BP — `[prog, --foo]` (unknown) → false; err mentions
// "unknown argument".
TEST(AppParseArgs, T_MAIN_070_UnknownArgErrors) {
  std::vector<std::string> storage{"prog", "--foo"};
  auto argv = make_argv(storage);
  std::uint64_t seed = 0;
  bool seed_provided = false;
  std::ostringstream err;

  EXPECT_FALSE(parse_args(static_cast<int>(argv.size()), argv.data(), seed,
                          seed_provided, err));
  EXPECT_THAT(err.str(), HasSubstr("unknown argument"));
}

// T-MAIN-075 — BV — `[prog, --foo, --seed, 1]` → false; loop short-circuits on
// the first unknown arg before reaching --seed. Documents left-to-right scan.
TEST(AppParseArgs, T_MAIN_075_UnknownBeforeSeedShortCircuits) {
  std::vector<std::string> storage{"prog", "--foo", "--seed", "1"};
  auto argv = make_argv(storage);
  std::uint64_t seed = 999;   // sentinel
  bool seed_provided = true;  // sentinel
  std::ostringstream err;

  EXPECT_FALSE(parse_args(static_cast<int>(argv.size()), argv.data(), seed,
                          seed_provided, err));
  EXPECT_THAT(err.str(), HasSubstr("unknown argument"));
  // Function returns at the first unknown arg before assigning seed.
}

// T-MAIN-080 — BV — `[prog, --seed, 0]` → seed=0, ok.
TEST(AppParseArgs, T_MAIN_080_SeedZero) {
  std::vector<std::string> storage{"prog", "--seed", "0"};
  auto argv = make_argv(storage);
  std::uint64_t seed = 999;
  bool seed_provided = false;
  std::ostringstream err;

  EXPECT_TRUE(parse_args(static_cast<int>(argv.size()), argv.data(), seed,
                         seed_provided, err));
  EXPECT_TRUE(seed_provided);
  EXPECT_EQ(seed, 0U);
  EXPECT_TRUE(err.str().empty());
}

// T-MAIN-085 — BV — `[prog, --seed, 18446744073709551615]` → seed=UINT64_MAX.
TEST(AppParseArgs, T_MAIN_085_SeedUint64Max) {
  std::vector<std::string> storage{"prog", "--seed", "18446744073709551615"};
  auto argv = make_argv(storage);
  std::uint64_t seed = 0;
  bool seed_provided = false;
  std::ostringstream err;

  EXPECT_TRUE(parse_args(static_cast<int>(argv.size()), argv.data(), seed,
                         seed_provided, err));
  EXPECT_TRUE(seed_provided);
  EXPECT_EQ(seed, std::numeric_limits<std::uint64_t>::max());
  EXPECT_TRUE(err.str().empty());
}

// -------------------------------------------------------------------------
// 13.3  random_seed  (T-MAIN-090)
// -------------------------------------------------------------------------

// T-MAIN-090 — BP — Returns a 64-bit value; loose lock for non-determinism.
// Across 10 invocations, not every result is zero (probability of all-zero
// from std::random_device is vanishingly small on any sane platform).
TEST(AppRandomSeed, T_MAIN_090_NotAllZero) {
  std::array<std::uint64_t, 10> samples{};
  for (auto& s : samples) {
    s = random_seed();
  }
  bool any_nonzero = false;
  for (auto s : samples) {
    if (s != 0) {
      any_nonzero = true;
      break;
    }
  }
  EXPECT_TRUE(any_nonzero) << "10 calls to random_seed() all returned 0";
}

}  // namespace

// Tests for hoisted Input anon-namespace helpers in src/input/input_internal.h.
// Spec: docs/test-plan/02-test-specifications.md §12.3
// (T-INP-105..140 trim, T-INP-145..185 parse_nonneg_int).
//
// Pinning these primitives turns later read_action/read_index assertions
// into local-failure diagnostics rather than whole-pipeline forensics.

#include <string>

#include <gtest/gtest.h>

#include "input/input_internal.h"

namespace {

using sts2::input::detail::parse_nonneg_int;
using sts2::input::detail::trim;

// -------------------------------------------------------------------------
// 12.3.1  trim  (T-INP-105..140)
// -------------------------------------------------------------------------

// T-INP-105 — BP, BV — Empty string → empty.
TEST(InputTrim, T_INP_105_Empty) {
    EXPECT_EQ(trim(""), "");
}

// T-INP-110 — BP, BV — All whitespace → empty.
TEST(InputTrim, T_INP_110_AllWhitespace) {
    EXPECT_EQ(trim("    "), "");
}

// T-INP-115 — BP — Leading whitespace only.
TEST(InputTrim, T_INP_115_LeadingWhitespace) {
    EXPECT_EQ(trim("   abc"), "abc");
}

// T-INP-120 — BP — Trailing whitespace only.
TEST(InputTrim, T_INP_120_TrailingWhitespace) {
    EXPECT_EQ(trim("abc   "), "abc");
}

// T-INP-125 — BP — Both ends.
TEST(InputTrim, T_INP_125_BothEnds) {
    EXPECT_EQ(trim("   abc   "), "abc");
}

// T-INP-130 — BP — No whitespace → unchanged.
TEST(InputTrim, T_INP_130_NoWhitespace) {
    EXPECT_EQ(trim("abc"), "abc");
}

// T-INP-135 — EG — Embedded whitespace preserved ("  a b  " → "a b").
TEST(InputTrim, T_INP_135_EmbeddedWhitespacePreserved) {
    EXPECT_EQ(trim("  a b  "), "a b");
}

// T-INP-140 — EG — Tab and CR also stripped (std::isspace-true).
TEST(InputTrim, T_INP_140_TabAndCarriageReturnStripped) {
    EXPECT_EQ(trim("\t\rabc\r\t"), "abc");
}

// -------------------------------------------------------------------------
// 12.3.2  parse_nonneg_int  (T-INP-145..185)
// -------------------------------------------------------------------------

// T-INP-145 — BP, BV — Empty → false; out unchanged.
TEST(InputParseNonnegInt, T_INP_145_EmptyReturnsFalse) {
    int out = 999;
    EXPECT_FALSE(parse_nonneg_int("", out));
    EXPECT_EQ(out, 999);
}

// T-INP-150 — BP — "0" → true, out=0.
TEST(InputParseNonnegInt, T_INP_150_Zero) {
    int out = -1;
    EXPECT_TRUE(parse_nonneg_int("0", out));
    EXPECT_EQ(out, 0);
}

// T-INP-155 — BP — "42" → true, out=42.
TEST(InputParseNonnegInt, T_INP_155_FortyTwo) {
    int out = -1;
    EXPECT_TRUE(parse_nonneg_int("42", out));
    EXPECT_EQ(out, 42);
}

// T-INP-160 — BV — "1000000" → true, out=1_000_000 (boundary, NOT > cap).
TEST(InputParseNonnegInt, T_INP_160_OneMillionBoundary) {
    int out = -1;
    EXPECT_TRUE(parse_nonneg_int("1000000", out));
    EXPECT_EQ(out, 1000000);
}

// T-INP-165 — BV — "1000001" → false (> cap).
TEST(InputParseNonnegInt, T_INP_165_OverCapRejected) {
    int out = 999;
    EXPECT_FALSE(parse_nonneg_int("1000001", out));
}

// T-INP-170 — BP — "1a" → false (non-digit).
TEST(InputParseNonnegInt, T_INP_170_TrailingLetterRejected) {
    int out = 999;
    EXPECT_FALSE(parse_nonneg_int("1a", out));
}

// T-INP-175 — EG — "+5" → false (sign char fails isdigit).
TEST(InputParseNonnegInt, T_INP_175_LeadingPlusRejected) {
    int out = 999;
    EXPECT_FALSE(parse_nonneg_int("+5", out));
}

// T-INP-180 — EG — "-1" → false.
TEST(InputParseNonnegInt, T_INP_180_NegativeRejected) {
    int out = 999;
    EXPECT_FALSE(parse_nonneg_int("-1", out));
}

// T-INP-185 — EG — Leading zeros "007" → true, out=7.
TEST(InputParseNonnegInt, T_INP_185_LeadingZeros) {
    int out = -1;
    EXPECT_TRUE(parse_nonneg_int("007", out));
    EXPECT_EQ(out, 7);
}

}

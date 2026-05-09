// Tests for src/input/input.{h,cc} public surface.
// Spec: docs/test-plan/02-test-specifications.md §12.1 (T-INP-005..065)
// and §12.2 (T-INP-070..100).
//
// read_action covers D1-D5 plus the trim/whitespace path; read_index
// covers D1-D3 with boundary cases at v == max_inclusive and the
// degenerate max_inclusive < 0 case where every input rejects.

#include <gtest/gtest.h>

#include <sstream>

#include "sts2/input/input.h"

namespace {

using sts2::input::Action;
using sts2::input::read_action;
using sts2::input::read_index;

// -------------------------------------------------------------------------
// 12.1  read_action  (T-INP-005..065)
// -------------------------------------------------------------------------

// T-INP-005 — BP — EOF → Quit. D1 TRUE.
TEST(InputReadAction, T_INP_005_EofReturnsQuit) {
  std::istringstream in("");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kQuit);
}

// T-INP-010 — BP, BV — Empty line → Invalid. D2 TRUE (post-trim).
TEST(InputReadAction, T_INP_010_EmptyLineReturnsInvalid) {
  std::istringstream in("\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kInvalid);
}

// T-INP-015 — BP, EP — "e" → EndTurn. D3 left-op TRUE.
TEST(InputReadAction, T_INP_015_LowercaseEReturnsEndTurn) {
  std::istringstream in("e\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kEndTurn);
}

// T-INP-020 — BP, EP — "E" → EndTurn. D3 right-op TRUE.
TEST(InputReadAction, T_INP_020_UppercaseEReturnsEndTurn) {
  std::istringstream in("E\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kEndTurn);
}

// T-INP-025 — BP, EP — "q" → Quit. D4 left-op TRUE.
TEST(InputReadAction, T_INP_025_LowercaseQReturnsQuit) {
  std::istringstream in("q\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kQuit);
}

// T-INP-030 — BP, EP — "Q" → Quit. D4 right-op TRUE.
TEST(InputReadAction, T_INP_030_UppercaseQReturnsQuit) {
  std::istringstream in("Q\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kQuit);
}

// T-INP-035 — BP, EP — "3" → PlayCard idx=3. D5 TRUE.
TEST(InputReadAction, T_INP_035_NumericReturnsPlayCard) {
  std::istringstream in("3\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kPlayCard);
  EXPECT_EQ(a.card_idx, 3);
}

// T-INP-040 — BP, EG — "3a" → Invalid (parse fails on letter). D5 FALSE.
TEST(InputReadAction, T_INP_040_TrailingLetterReturnsInvalid) {
  std::istringstream in("3a\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kInvalid);
}

// T-INP-045 — EP — Whitespace tolerant: "  e  " → EndTurn after trim.
TEST(InputReadAction, T_INP_045_WhitespaceTolerantEndTurn) {
  std::istringstream in("  e  \n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kEndTurn);
}

// T-INP-050 — EG — Trailing CR/LF (Windows line endings) trimmed.
// "q\r\n": getline strips '\n', leaving "q\r"; trim removes the '\r'
// since std::isspace is true for it.
TEST(InputReadAction, T_INP_050_WindowsLineEndingsTrimmed) {
  std::istringstream in("q\r\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kQuit);
}

// T-INP-055 — EG — Multi-line input: read_action consumes only first line.
// After reading "q", peek() should return 'e' (start of the next line).
TEST(InputReadAction, T_INP_055_ConsumesOnlyFirstLine) {
  std::istringstream in("q\ne\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kQuit);
  EXPECT_EQ(in.peek(), 'e');
}

// T-INP-060 — EG — Multi-digit numeric → PlayCard with parsed value.
// (No upper-bound bounds-check at this layer — caller validates against hand
// size.)
TEST(InputReadAction, T_INP_060_MultiDigitParsesValue) {
  std::istringstream in("42\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kPlayCard);
  EXPECT_EQ(a.card_idx, 42);
}

// T-INP-065 — EG — Overflow guard: 9999999 > 1_000_000 cap → Invalid.
// parse_nonneg_int returns false, so read_action falls to D5 FALSE branch.
TEST(InputReadAction, T_INP_065_OverflowGuardReturnsInvalid) {
  std::istringstream in("9999999\n");
  const Action a = read_action(in);
  EXPECT_EQ(a.kind, Action::kInvalid);
}

// -------------------------------------------------------------------------
// 12.2  read_index  (T-INP-070..100)
// -------------------------------------------------------------------------

// T-INP-070 — BP — EOF → -1. D1 TRUE.
TEST(InputReadIndex, T_INP_070_EofReturnsNegativeOne) {
  std::istringstream in("");
  EXPECT_EQ(read_index(in, 5), -1);
}

// T-INP-075 — BP — Non-digit → -1. D2 TRUE.
TEST(InputReadIndex, T_INP_075_NonDigitReturnsNegativeOne) {
  std::istringstream in("abc\n");
  EXPECT_EQ(read_index(in, 5), -1);
}

// T-INP-080 — BP — In-range value → that value.
TEST(InputReadIndex, T_INP_080_InRangeReturnsValue) {
  std::istringstream in("3\n");
  EXPECT_EQ(read_index(in, 5), 3);
}

// T-INP-085 — BV — v == max_inclusive → returns max_inclusive (boundary).
TEST(InputReadIndex, T_INP_085_AtMaxInclusiveReturnsValue) {
  std::istringstream in("5\n");
  EXPECT_EQ(read_index(in, 5), 5);
}

// T-INP-090 — BV — v == max_inclusive + 1 → -1. D3 TRUE.
TEST(InputReadIndex, T_INP_090_OneAboveMaxReturnsNegativeOne) {
  std::istringstream in("6\n");
  EXPECT_EQ(read_index(in, 5), -1);
}

// T-INP-095 — EG — max_inclusive == 0: v=0 → 0; v=1 → -1.
TEST(InputReadIndex, T_INP_095_ZeroMaxBoundary) {
  {
    std::istringstream in("0\n");
    EXPECT_EQ(read_index(in, 0), 0);
  }
  {
    std::istringstream in("1\n");
    EXPECT_EQ(read_index(in, 0), -1);
  }
}

// T-INP-100 — EG — Negative max_inclusive: every non-negative input rejects.
// parse_nonneg_int yields v >= 0, so v > -1 → D3 TRUE → -1.
TEST(InputReadIndex, T_INP_100_NegativeMaxAlwaysRejects) {
  {
    std::istringstream in("0\n");
    EXPECT_EQ(read_index(in, -1), -1);
  }
  {
    std::istringstream in("7\n");
    EXPECT_EQ(read_index(in, -1), -1);
  }
}

}  // namespace

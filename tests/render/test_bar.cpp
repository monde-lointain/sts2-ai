// Tests for src/render/Bar.{h,cpp}.
// Spec: docs/test-plan/02-test-specifications.md §11.1 (T-RND-005..060).
//
// hp_bar covers six decisions: width<=0 guard, maximum<=0 coercion,
// clamp(current), the visibility-floor short-circuit (clamped>0 &&
// filled_chars==0), and the two fill/empty loops. UTF-8 expectations are
// built from glyphs::kFullBlock/kEmptyBlock so the assertion strings
// reflect the exact byte sequences emitted by the renderer.

#include <string>

#include <gtest/gtest.h>

#include "render/Bar.h"
#include "render/Glyphs.h"

namespace {

using glyphs::kEmptyBlock;
using glyphs::kFullBlock;

// Helpers: build expected UTF-8 strings without sprinkling raw byte sequences
// at every call site. Keeps tests readable and resilient if the glyph bytes
// ever change (the constants in Glyphs.h move with them).
std::string Repeat(const char* glyph, int n) {
    std::string s;
    for (int i = 0; i < n; ++i) s += glyph;
    return s;
}

// -------------------------------------------------------------------------
// 11.1  hp_bar  (T-RND-005..060)
// -------------------------------------------------------------------------

// T-RND-005 — BP, BV — Width zero — D1 TRUE.
TEST(RenderHpBar, T_RND_005_WidthZeroReturnsEmpty) {
    const std::string s = render::hp_bar(5, 10, 0);
    EXPECT_TRUE(s.empty());
}

// T-RND-010 — BV, EG — Negative width — D1 TRUE.
TEST(RenderHpBar, T_RND_010_NegativeWidthReturnsEmpty) {
    EXPECT_EQ(render::hp_bar(5, 10, -3), "");
}

// T-RND-015 — BP, EG — Maximum zero coerced to 1 — D2 TRUE.
// (current=5 > 1 → clamped to 1 → 100% filled across width=4.)
TEST(RenderHpBar, T_RND_015_MaximumZeroCoercedToOne) {
    EXPECT_EQ(render::hp_bar(5, 0, 4), Repeat(kFullBlock, 4));
}

// T-RND-020 — EG — Negative maximum coerced — D2 TRUE.
TEST(RenderHpBar, T_RND_020_NegativeMaximumCoerced) {
    EXPECT_EQ(render::hp_bar(5, -1, 4), Repeat(kFullBlock, 4));
}

// T-RND-025 — BP, BV — Current zero clamps to 0 → all empty.
TEST(RenderHpBar, T_RND_025_CurrentZeroAllEmpty) {
    EXPECT_EQ(render::hp_bar(0, 10, 4), Repeat(kEmptyBlock, 4));
}

// T-RND-030 — BV — Current negative clamps to 0 → all empty.
TEST(RenderHpBar, T_RND_030_NegativeCurrentAllEmpty) {
    EXPECT_EQ(render::hp_bar(-3, 10, 4), Repeat(kEmptyBlock, 4));
}

// T-RND-035 — BV — Current above max clamps to max → all filled.
TEST(RenderHpBar, T_RND_035_CurrentAboveMaxAllFilled) {
    EXPECT_EQ(render::hp_bar(15, 10, 4), Repeat(kFullBlock, 4));
}

// T-RND-040 — BP, BV — Visibility floor: tiny positive raises filled to 1.
// (1*4)/100 == 0, but D4 (clamped>0 && filled_chars==0) raises to 1.
TEST(RenderHpBar, T_RND_040_VisibilityFloorRaisesToOne) {
    const std::string expected = std::string(kFullBlock) + Repeat(kEmptyBlock, 3);
    EXPECT_EQ(render::hp_bar(1, 100, 4), expected);
}

// T-RND-045 — BP, EP — Half-fill normal case: 5/10 of 4 → 2 filled.
TEST(RenderHpBar, T_RND_045_HalfFill) {
    const std::string expected = Repeat(kFullBlock, 2) + Repeat(kEmptyBlock, 2);
    EXPECT_EQ(render::hp_bar(5, 10, 4), expected);
}

// T-RND-050 — BV — Full bar.
TEST(RenderHpBar, T_RND_050_FullBar) {
    EXPECT_EQ(render::hp_bar(10, 10, 4), Repeat(kFullBlock, 4));
}

// T-RND-055 — EG — Width 1 with clamped > 0 — D4 raises filled to 1.
// (5*1)/10 == 0; clamped>0 && filled_chars==0 → filled_chars=1.
TEST(RenderHpBar, T_RND_055_WidthOneVisibilityFloor) {
    EXPECT_EQ(render::hp_bar(5, 10, 1), std::string(kFullBlock));
}

// T-RND-060 — EG, BV — clamped == 0 short-circuits D4's left operand.
// Equivalent inputs to T-RND-025 but the test name pins the branch intent:
// the && at D4 must NOT evaluate filled_chars==0 when clamped is 0.
TEST(RenderHpBar, T_RND_060_ClampedZeroShortCircuitsD4) {
    EXPECT_EQ(render::hp_bar(0, 10, 4), Repeat(kEmptyBlock, 4));
}

}  // namespace

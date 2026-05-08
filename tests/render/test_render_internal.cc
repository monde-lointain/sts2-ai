// Tests for hoisted Render anon-namespace helpers in
// src/render/render_internal.h. Spec: docs/test-plan/02-test-specifications.md
// §11.2 (T-RND-065..150).
//
// The helpers are pure formatters/predicates that the integration target
// (render_combat) composes; pinning their behaviour here turns later
// substring assertions into local-failure diagnostics rather than
// whole-output forensics.

#include <gmock/gmock.h>
#include <gtest/gtest.h>

#include <cstddef>
#include <string>
#include <vector>

#include "render/render_internal.h"
#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/enemy.h"
#include "sts2/game/player.h"
#include "sts2/game/power.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"
#include "sts2/render/ansi.h"
#include "sts2/render/glyphs.h"
#include "tests/game/test_helpers.h"

namespace {

using ::testing::HasSubstr;

using sts2::render::detail::format_intent;
using sts2::render::detail::format_powers;
using sts2::render::detail::max_enemy_name_len;
using sts2::render::detail::power_color;
using sts2::render::detail::power_name;
using sts2::render::detail::repeat_utf8;
using sts2::render::detail::spaces;
using sts2::render::detail::total_deck_size;

using sts2::tests::helpers::MakePower;

namespace ansi = sts2::ansi;
namespace glyphs = sts2::glyphs;

using Enemy = sts2::game::Enemy;
using MoveId = sts2::game::MoveId;
using Player = sts2::game::Player;
using PowerKind = sts2::game::PowerKind;
using Vitals = sts2::game::Vitals;

constexpr PowerKind Weak = PowerKind::Weak;
constexpr PowerKind Strength = PowerKind::Strength;
constexpr PowerKind Ritual = PowerKind::Ritual;

// -------------------------------------------------------------------------
// 11.2.1  repeat_utf8  (T-RND-065..075)
// -------------------------------------------------------------------------

// T-RND-065 — BP, BV — Count 0 → empty (loop predicate FALSE on entry).
TEST(RenderInternalRepeat, T_RND_065_CountZeroEmpty) {
  EXPECT_EQ(repeat_utf8(glyphs::kFullBlock, 0), "");
}

// T-RND-070 — BP — Count 3 → glyph repeated 3 times (UTF-8 byte count = 3*3).
TEST(RenderInternalRepeat, T_RND_070_CountThreeRepeats) {
  const std::string s = repeat_utf8(glyphs::kFullBlock, 3);
  EXPECT_EQ(s, std::string(glyphs::kFullBlock) + glyphs::kFullBlock +
                   glyphs::kFullBlock);
}

// T-RND-075 — EG — Negative count → empty (loop predicate FALSE on entry).
TEST(RenderInternalRepeat, T_RND_075_NegativeCountEmpty) {
  EXPECT_EQ(repeat_utf8(glyphs::kFullBlock, -2), "");
}

// -------------------------------------------------------------------------
// 11.2.2  spaces  (T-RND-080)
// -------------------------------------------------------------------------

// T-RND-080 — BP, BV — spaces(0)/spaces(5) — boundary-and-typical pair.
TEST(RenderInternalSpaces, T_RND_080_ZeroAndFive) {
  EXPECT_EQ(spaces(0), "");
  EXPECT_EQ(spaces(5), "     ");
}

// -------------------------------------------------------------------------
// 11.2.3  power_color  (T-RND-085)
// -------------------------------------------------------------------------

// T-RND-085 — BP — Returns ansi::kReset for any PowerKind. Locks the current
// "no per-kind colour" behaviour so later refactors that introduce per-power
// colouring trip this assertion intentionally.
TEST(RenderInternalPowerColor, T_RND_085_AlwaysReset) {
  EXPECT_STREQ(power_color(Weak), ansi::kReset);
  EXPECT_STREQ(power_color(Strength), ansi::kReset);
  EXPECT_STREQ(power_color(Ritual), ansi::kReset);
}

// -------------------------------------------------------------------------
// 11.2.4  power_name  (T-RND-090..095)
// -------------------------------------------------------------------------

// T-RND-090 — BP — Each enum value maps to its expected display string.
TEST(RenderInternalPowerName, T_RND_090_EachKindMapsCorrectly) {
  EXPECT_STREQ(power_name(Weak), "Weak");
  EXPECT_STREQ(power_name(Strength), "Str");
  EXPECT_STREQ(power_name(Ritual), "Ritual");
}

// T-RND-095 — EG — Out-of-enum value → "" (post-switch fall-through return).
// PowerKind has explicit underlying type `int`, so static_cast<PowerKind>(99)
// is well-defined per [expr.static.cast]. Locks the post-switch return path
// of power_name() against regressions that drop the fall-through return.
TEST(RenderInternalPowerName, T_RND_095_OutOfEnumReturnsEmpty) {
  const PowerKind bogus = static_cast<PowerKind>(99);
  EXPECT_STREQ(power_name(bogus), "");
}

// -------------------------------------------------------------------------
// 11.2.5  format_powers  (T-RND-100..115)
// -------------------------------------------------------------------------

// T-RND-100 — BP, BV — Empty vector → empty string (D1 TRUE early return).
TEST(RenderInternalFormatPowers, T_RND_100_EmptyVectorEmpty) {
  EXPECT_EQ(format_powers({}), "");
}

// T-RND-105 — BP — Single power: no leading separator (D3 short-circuits on
// first iter via the `first` flag).
TEST(RenderInternalFormatPowers, T_RND_105_SingleNoSeparator) {
  const std::string s = format_powers({MakePower(Weak, 2)});
  const std::string expected =
      std::string(ansi::kReset) + "Weak 2" + ansi::kReset;
  EXPECT_EQ(s, expected);
}

// T-RND-110 — BP — Two powers: exactly one ", " separator between them.
TEST(RenderInternalFormatPowers, T_RND_110_TwoWithSeparator) {
  const std::string s =
      format_powers({MakePower(Weak, 2), MakePower(Strength, 3)});
  const std::string expected = std::string(ansi::kReset) + "Weak 2" +
                               ansi::kReset + ", " + std::string(ansi::kReset) +
                               "Str 3" + ansi::kReset;
  EXPECT_EQ(s, expected);
}

// T-RND-115 — EG — Three powers: separator count = n-1 = 2.
// Counts ", " occurrences explicitly so a regression that drops or doubles
// separators is caught even if individual tokens still match.
TEST(RenderInternalFormatPowers, T_RND_115_ThreeSeparatorsAreNMinusOne) {
  const std::string s = format_powers(
      {MakePower(Weak, 1), MakePower(Strength, 2), MakePower(Ritual, 3)});

  std::size_t count = 0;
  for (std::size_t pos = 0; (pos = s.find(", ", pos)) != std::string::npos;
       ++pos) {
    ++count;
  }
  EXPECT_EQ(count, 2u);
  EXPECT_THAT(s, HasSubstr("Weak 1"));
  EXPECT_THAT(s, HasSubstr("Str 2"));
  EXPECT_THAT(s, HasSubstr("Ritual 3"));
}

// -------------------------------------------------------------------------
// 11.2.6  format_intent  (T-RND-120..130)
// -------------------------------------------------------------------------

// T-RND-120 — BP — Incantation: contains kArrowUp glyph and "Buff".
TEST(RenderInternalFormatIntent, T_RND_120_IncantationBuffArrowUp) {
  Enemy e{};
  e.current_move = MoveId::Incantation;

  const std::string s = format_intent(e);

  EXPECT_THAT(s, HasSubstr(glyphs::kArrowUp));
  EXPECT_THAT(s, HasSubstr("Buff"));
}

// T-RND-125 — BP — DarkStrike, no powers: contains kSwords and the base
// damage as a plain integer (compute_outgoing returns base when both
// Strength and Weak are absent).
TEST(RenderInternalFormatIntent, T_RND_125_DarkStrikeShowsBaseDamage) {
  Enemy e{};
  e.current_move = MoveId::DarkStrike;
  e.dark_strike_base = 9;

  const std::string s = format_intent(e);

  EXPECT_THAT(s, HasSubstr(glyphs::kSwords));
  EXPECT_THAT(s, HasSubstr("9"));
}

// T-RND-130 — DF — DarkStrike with Strength on enemy reflects boosted damage.
// Exercises the (def, use) chain enemy.vitals.powers → compute_outgoing →
// intent string.
TEST(RenderInternalFormatIntent, T_RND_130_DarkStrikeBoostedByStrength) {
  Enemy e{};
  e.current_move = MoveId::DarkStrike;
  e.dark_strike_base = 9;
  e.vitals.powers = {MakePower(Strength, 2)};

  const std::string s = format_intent(e);

  EXPECT_THAT(s, HasSubstr("11"));
}

// -------------------------------------------------------------------------
// 11.2.7  max_enemy_name_len  (T-RND-135..145)
// -------------------------------------------------------------------------

// T-RND-135 — BP, BV — Empty container → 0 (loop body never enters).
TEST(RenderInternalMaxEnemyName, T_RND_135_EmptyZero) {
  const std::vector<Enemy> es;
  EXPECT_EQ(max_enemy_name_len(es), 0u);
}

// T-RND-140 — BP — All alive: returns max name length across the vector.
TEST(RenderInternalMaxEnemyName, T_RND_140_AllAliveMaxLen) {
  Enemy a{};
  a.name = "ab";
  a.vitals = Vitals{10, 10, 0, {}};
  Enemy b{};
  b.name = "abcde";
  b.vitals = Vitals{10, 10, 0, {}};
  Enemy c{};
  c.name = "abcd";
  c.vitals = Vitals{10, 10, 0, {}};
  const std::vector<Enemy> es = {a, b, c};

  EXPECT_EQ(max_enemy_name_len(es), 5u);
}

// T-RND-145 — EG — Dead enemies are excluded; only alive names contribute.
// Locks the "hp > 0" guard inside the loop body.
TEST(RenderInternalMaxEnemyName, T_RND_145_DeadExcluded) {
  Enemy a{};
  a.name = "longer";
  a.vitals = Vitals{0, 10, 0, {}};
  Enemy b{};
  b.name = "x";
  b.vitals = Vitals{10, 10, 0, {}};
  const std::vector<Enemy> es = {a, b};

  EXPECT_EQ(max_enemy_name_len(es), 1u);
}

// -------------------------------------------------------------------------
// 11.2.8  total_deck_size  (T-RND-150)
// -------------------------------------------------------------------------

// T-RND-150 — BP — Sum across draw + hand + discard + exhaust piles.
TEST(RenderInternalTotalDeckSize, T_RND_150_SumsAllFourPiles) {
  Player p;
  p.draw_pile.push_back(sts2::cards::make_strike());
  p.draw_pile.push_back(sts2::cards::make_strike());
  p.hand.push_back(sts2::cards::make_defend());
  p.discard_pile.push_back(sts2::cards::make_neutralize());
  p.discard_pile.push_back(sts2::cards::make_neutralize());
  p.discard_pile.push_back(sts2::cards::make_neutralize());
  p.exhaust_pile.push_back(sts2::cards::make_survivor());

  EXPECT_EQ(total_deck_size(p), 7);
}

}  // namespace

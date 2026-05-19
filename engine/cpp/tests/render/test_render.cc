// Tests for src/render/render.{h,cc} render_combat integration.
// Spec: docs/test-plan/02-test-specifications.md §11.3 (T-RND-155..210).
//
// render_combat is the integration target; these tests capture into a
// std::ostringstream and use HasSubstr/Not(HasSubstr) to assert visible
// tokens. ANSI colour sequences appear via the ansi:: constants — we never
// hand-write escape literals at the assertion site.
//
// Setup-API caveat: Combat exposes no public method to apply a power to
// the player's vitals (see test plan §14.3 U-2). T-RND-165 documents this
// uncovered branch and skips at runtime.

#include <gmock/gmock.h>
#include <gtest/gtest.h>

#include <cstddef>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

#include "render/render_internal.h"
#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/damage.h"
#include "sts2/game/enemy.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"
#include "sts2/render/ansi.h"
#include "sts2/render/glyphs.h"
#include "sts2/render/render.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/expected_values.h"

namespace {

using ::testing::HasSubstr;
using ::testing::Not;

using sts2::tests::helpers::drain_player_energy;
using sts2::tests::helpers::kill_enemy;
using sts2::tests::helpers::make_starter_combat;
using sts2::tests::seeds::kCombatTestSeed;

using Card = sts2::game::Card;
using Combat = sts2::game::Combat;
using Enemy = sts2::game::Enemy;
using MoveId = sts2::game::MoveId;
using Stat = sts2::game::Stat;
using Vitals = sts2::game::Vitals;

namespace ansi = sts2::ansi;
namespace glyphs = sts2::glyphs;
namespace render = sts2::render;

// Capture the renderer's output for substring assertions.
std::string render_to_string(const Combat& c) {
  std::ostringstream os;
  render::render_combat(c, os);
  return os.str();
}

// -------------------------------------------------------------------------
// 11.3  render_combat  (T-RND-155..210)
// -------------------------------------------------------------------------

// T-RND-155 — BP, EP — Initial state covers the bulk of the always-on
// branches: round/energy/HP header, both enemies alive, hand of 7 with the
// "playable" path on every card (energy 3, all costs <= 1).
TEST(RenderCombat, T_RND_155_InitialStateRendersAllBaselineFields) {
  Combat c = make_starter_combat(kCombatTestSeed);
  const std::string s = render_to_string(c);

  EXPECT_THAT(s, HasSubstr("Round 1"));
  EXPECT_THAT(s, HasSubstr("Energy 3/3"));
  EXPECT_THAT(s, HasSubstr("70/70"));
  EXPECT_THAT(s, HasSubstr("Calcified Cultist"));
  EXPECT_THAT(s, HasSubstr("Damp Cultist"));
  for (std::size_t i = 0; i < c.player().hand.size(); ++i) {
    EXPECT_THAT(s, HasSubstr("[" + std::to_string(i) + "]"));
  }
  EXPECT_THAT(s, Not(HasSubstr(" blk")));  // no block to display
  EXPECT_THAT(s, Not(HasSubstr("Weak")));  // no powers visible yet
  EXPECT_THAT(s, Not(HasSubstr("Ritual")));
  EXPECT_THAT(s, HasSubstr(glyphs::kRelicDiamond));  // Ring of the Snake row
}

// T-RND-160 — BP — Block visible on player after gain_player_block(5).
// D99 (player block > 0) TRUE.
TEST(RenderCombat, T_RND_160_PlayerBlockVisible) {
  Combat c = make_starter_combat(kCombatTestSeed);
  c.gain_player_block(5);

  const std::string s = render_to_string(c);

  // Tied form: blue "5", reset, then " blk".
  const std::string token =
      std::string(ansi::kBlue) + "5" + ansi::kReset + " blk";
  EXPECT_THAT(s, HasSubstr(token));
}

// T-RND-165 — BP — Player powers visible. Documented as unreachable: no
// public Combat API applies a power to the player. Tracked in §14.3 U-2.
TEST(RenderCombat, T_RND_165_PlayerPowersVisible_Skipped) {
  GTEST_SKIP() << "Unreachable: no public API to apply power to player; see "
                  "test plan §14.3 U-2";
}

// T-RND-170 — BP — Block visible on enemy. Built directly: add_enemy with
// vitals.block=3 on the prebuilt struct exercises D121 TRUE without any
// dependency on enemy_phase block-zeroing.
TEST(RenderCombat, T_RND_170_EnemyBlockVisible) {
  Combat c{kCombatTestSeed};
  Enemy e{};
  e.name = "Block Goblin";
  e.vitals = Vitals{Stat{20}, Stat{20}, Stat{3}, {}};
  c.add_enemy(std::move(e));
  c.start(sts2::cards::make_silent_starter_deck());

  const std::string s = render_to_string(c);

  const std::string token =
      std::string(ansi::kBlue) + "3" + ansi::kReset + " blk";
  EXPECT_THAT(s, HasSubstr(token));
}

// T-RND-175 — BP, DF — Enemy with Ritual visible after R1 enemy_phase.
// end_turn drives R1 enemy_phase → both enemies' Incantation applies Ritual.
// Covers D125 TRUE plus the format_powers integration.
TEST(RenderCombat, T_RND_175_EnemyRitualVisibleAfterEndTurn) {
  Combat c = make_starter_combat(kCombatTestSeed);
  c.end_turn();

  const std::string s = render_to_string(c);

  EXPECT_THAT(s, HasSubstr("Ritual 2"));  // Calcified
  EXPECT_THAT(s, HasSubstr("Ritual 5"));  // Damp
}

// T-RND-180 — BP — Dead enemy hidden: D116 TRUE (continue) drops the row.
TEST(RenderCombat, T_RND_180_DeadEnemyHidden) {
  Combat c = make_starter_combat(kCombatTestSeed);
  kill_enemy(c, 0);

  const std::string s = render_to_string(c);

  EXPECT_THAT(s, Not(HasSubstr("Calcified Cultist")));
  EXPECT_THAT(s, HasSubstr("Damp Cultist"));
}

// T-RND-185 — BP — Unplayable card displayed in dim. After draining energy
// to 0 via drain_player_energy, every cost-1 card in hand renders with
// kBulletHollow + kDim, never kBulletFilled + kGreen. Covers D135/D136 FALSE.
TEST(RenderCombat, T_RND_185_UnplayableCardsRenderDim) {
  Combat c = make_starter_combat(kCombatTestSeed);
  drain_player_energy(c);
  ASSERT_EQ(c.player().energy, 0);
  ASSERT_FALSE(c.player().hand.empty());

  const std::string s = render_to_string(c);

  const std::string hollow_dim =
      std::string(ansi::kDim) + glyphs::kBulletHollow + ansi::kReset;
  const std::string filled_green =
      std::string(ansi::kGreen) + glyphs::kBulletFilled + ansi::kReset;
  EXPECT_THAT(s, HasSubstr(hollow_dim));
  EXPECT_THAT(s, Not(HasSubstr(filled_green)));
}

// T-RND-190 — BP — Attack vs Skill colour: Strike (Attack) carries kRed on
// its name; Defend (Skill) carries kBlue. The starter hand under
// kCombatTestSeed contains both card types.
TEST(RenderCombat, T_RND_190_AttackVsSkillColour) {
  Combat c = make_starter_combat(kCombatTestSeed);
  const std::string s = render_to_string(c);

  const std::string strike_red =
      std::string(ansi::kRed) + "Strike" + ansi::kReset;
  const std::string defend_blue =
      std::string(ansi::kBlue) + "Defend" + ansi::kReset;
  EXPECT_THAT(s, HasSubstr(strike_red));
  EXPECT_THAT(s, HasSubstr(defend_blue));
}

// T-RND-195 — BP — Target arrow: AnyEnemy cards (Strike) render kArrowRight;
// Self cards (Defend) do not get one tied to their line. We check that a
// "Strike line ending with arrow" pattern is present and that no
// "Defend ... arrow" pattern is. Defend-arrow absence is checked via the
// unique stat token "5blk" followed (eventually) by a newline before any arrow.
TEST(RenderCombat, T_RND_195_TargetArrowOnAnyEnemyOnly) {
  Combat c = make_starter_combat(kCombatTestSeed);
  const std::string s = render_to_string(c);

  // Strike line: "...Strike\x1b[0m (...) 6dmg  \x1b[93m→\x1b[0m\n"
  // Robust substring: short_stats "6dmg" then the arrow run on the same line.
  const std::string strike_arrow_tail = "6dmg  " + std::string(ansi::kYellow) +
                                        glyphs::kArrowRight + ansi::kReset +
                                        "\n";
  EXPECT_THAT(s, HasSubstr(strike_arrow_tail));

  // Defend's line ends right after "5blk" (no arrow before the newline).
  EXPECT_THAT(s, HasSubstr("5blk\n"));

  // Belt-and-braces: the literal "Defend has an arrow" byte sequence must
  // never appear. Mirrors render.cc's emission: short_stats then two
  // spaces then the yellow arrow run. If a regression ever attaches an
  // arrow to a Self-target card, this assertion fires.
  const std::string defend_arrow_pattern =
      "5blk  " + std::string(ansi::kYellow) + glyphs::kArrowRight;
  EXPECT_THAT(s, Not(HasSubstr(defend_arrow_pattern)))
      << "Defend (Self target) must not have target arrow";
}

// T-RND-200 — BP — Description rendering: each line of card.description
// emerges on its own indented row (D147 multi-iter). Use Neutralize whose
// description has two lines; build a controlled deck so it lands in hand.
TEST(RenderCombat, T_RND_200_NeutralizeDescriptionMultiLine) {
  Combat c{kCombatTestSeed};
  // Three Neutralize cards; with kBaseHandDraw+RingOfTheSnake=7 and only 3
  // in deck, draw fills hand with all three.
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  deck.push_back(sts2::cards::make_card(sts2::game::CardId::kNeutralize));
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 3U);

  const std::string s = render_to_string(c);

  EXPECT_THAT(s, HasSubstr("Deal 3 damage."));
  EXPECT_THAT(s, HasSubstr("Apply 1 Weak."));
}

// T-RND-205 — DF — Intent rendering toggles per move.
// Setup A (R1 just-started): both enemies on Incantation → "Buff" appears.
// Setup B (after end_turn → R2 just-started): both on DarkStrike → the
// computed damage value (read off the actual enemy via format_intent) is
// in the output.
TEST(RenderCombat, T_RND_205_IntentRendersIncantationThenDarkStrike) {
  Combat c = make_starter_combat(kCombatTestSeed);

  // Setup A: R1, both enemies on Incantation.
  ASSERT_EQ(c.round(), 1);
  ASSERT_EQ(c.enemies()[0].current_move, MoveId::kIncantation);
  ASSERT_EQ(c.enemies()[1].current_move, MoveId::kIncantation);
  {
    const std::string s = render_to_string(c);
    EXPECT_THAT(s, HasSubstr("Buff"));
  }

  // Advance to R2; enemies roll Incantation → DarkStrike.
  c.end_turn();
  ASSERT_EQ(c.round(), 2);
  ASSERT_EQ(c.enemies()[0].current_move, MoveId::kDarkStrike);
  ASSERT_EQ(c.enemies()[1].current_move, MoveId::kDarkStrike);
  {
    const std::string s = render_to_string(c);
    // Read the computed intent strings off the live enemies; this binds
    // the assertion to render::detail::format_intent's exact output and
    // sidesteps any dependence on the seed's HP rolls or Ritual ticks.
    const std::string intent0 = render::detail::format_intent(c.enemies()[0]);
    const std::string intent1 = render::detail::format_intent(c.enemies()[1]);
    EXPECT_THAT(s, HasSubstr(intent0));
    EXPECT_THAT(s, HasSubstr(intent1));
    EXPECT_THAT(s, Not(HasSubstr("Buff")));
  }
}

// T-RND-210 — EG — All enemies dead: no enemy rows render; display_idx
// stays at 0 throughout the loop (D116 TRUE for every entry).
TEST(RenderCombat, T_RND_210_AllEnemiesDeadNoEnemyRows) {
  Combat c = make_starter_combat(kCombatTestSeed);
  kill_enemy(c, 0);
  kill_enemy(c, 1);

  const std::string s = render_to_string(c);

  EXPECT_THAT(s, Not(HasSubstr("Calcified Cultist")));
  EXPECT_THAT(s, Not(HasSubstr("Damp Cultist")));
  // No "[N]" enemy index brackets — but the player hand uses "[N]" too,
  // so check that the enemy-block prefix "  [0] \x1b[1m" (bold name run)
  // does NOT appear. The hand bullet pattern starts with a colour code and
  // the bullet glyph, never bold immediately after "[N] ".
  const std::string enemy_prefix = "  [0] " + std::string(ansi::kBold);
  EXPECT_THAT(s, Not(HasSubstr(enemy_prefix)));
}

// T-RND-215 — LouseProgenitor 3-move intent (sts2-cli parity).
// WebCannon:  ⚔9 ⬇Debuff.  CurlAndGrow: 🛡DEF ⬆Buff.  Pounce: ⚔14.
TEST(RenderCombat, T_RND_215_LouseProgenitorIntents) {
  using sts2::game::MonsterKind;

  auto make_lp = [](MoveId m) {
    Enemy e{};
    e.name = "Louse";
    e.kind = MonsterKind::kLouseProgenitor;
    e.current_move = m;
    e.vitals = Vitals{Stat{134}, Stat{134}, Stat{0}, {}};
    return e;
  };

  {  // WebCannon: attack 9 + Frail debuff
    Combat c{kCombatTestSeed};
    c.add_enemy(make_lp(MoveId::kWebCannon));
    c.start(sts2::cards::make_silent_starter_deck());
    const std::string s = render_to_string(c);
    EXPECT_THAT(s, HasSubstr(std::string(glyphs::kSwords) + "9"));
    EXPECT_THAT(s, HasSubstr(std::string(glyphs::kArrowDown) + "Debuff"));
  }
  {  // CurlAndGrow: 14 block + Strength buff
    Combat c{kCombatTestSeed};
    c.add_enemy(make_lp(MoveId::kCurlAndGrow));
    c.start(sts2::cards::make_silent_starter_deck());
    const std::string s = render_to_string(c);
    EXPECT_THAT(s, HasSubstr(std::string(glyphs::kShield) + "DEF"));
    EXPECT_THAT(s, HasSubstr(std::string(glyphs::kArrowUp) + "Buff"));
  }
  {  // Pounce: attack 14
    Combat c{kCombatTestSeed};
    c.add_enemy(make_lp(MoveId::kPounce));
    c.start(sts2::cards::make_silent_starter_deck());
    const std::string s = render_to_string(c);
    EXPECT_THAT(s, HasSubstr(std::string(glyphs::kSwords) + "14"));
  }
}

// T-RND-220 — Nibbit 3-move intent (sts2-cli parity).
// ButtMove: ⚔12.  SliceMove: ⚔6 🛡DEF.  HissMove: ⬆Buff.
TEST(RenderCombat, T_RND_220_NibbitIntents) {
  using sts2::game::MonsterKind;

  auto make_nb = [](MoveId m) {
    Enemy e{};
    e.name = "Nibbit";
    e.kind = MonsterKind::kNibbit;
    e.current_move = m;
    e.vitals = Vitals{Stat{42}, Stat{42}, Stat{0}, {}};
    return e;
  };

  {  // ButtMove: attack 12
    Combat c{kCombatTestSeed};
    c.add_enemy(make_nb(MoveId::kButtMove));
    c.start(sts2::cards::make_silent_starter_deck());
    const std::string s = render_to_string(c);
    EXPECT_THAT(s, HasSubstr(std::string(glyphs::kSwords) + "12"));
  }
  {  // SliceMove: attack 6 + 5 block-self
    Combat c{kCombatTestSeed};
    c.add_enemy(make_nb(MoveId::kSliceMove));
    c.start(sts2::cards::make_silent_starter_deck());
    const std::string s = render_to_string(c);
    EXPECT_THAT(s, HasSubstr(std::string(glyphs::kSwords) + "6"));
    EXPECT_THAT(s, HasSubstr(std::string(glyphs::kShield) + "DEF"));
  }
  {  // HissMove: Strength self-buff
    Combat c{kCombatTestSeed};
    c.add_enemy(make_nb(MoveId::kHissMove));
    c.start(sts2::cards::make_silent_starter_deck());
    const std::string s = render_to_string(c);
    EXPECT_THAT(s, HasSubstr(std::string(glyphs::kArrowUp) + "Buff"));
  }
}

// T-RND-225 — kAttack token applies attacker Strength via compute_outgoing.
// LP + Strength(3) on WebCannon: 9 + 3 = 12.
TEST(RenderCombat, T_RND_225_AttackIntentAppliesStrength) {
  using sts2::game::MonsterKind;
  using sts2::game::Power;
  using sts2::game::PowerKind;

  Combat c{kCombatTestSeed};
  Enemy e{};
  e.name = "Louse";
  e.kind = MonsterKind::kLouseProgenitor;
  e.current_move = MoveId::kWebCannon;
  e.vitals =
      Vitals{Stat{134}, Stat{134}, Stat{0}, {Power{PowerKind::kStrength, 3}}};
  c.add_enemy(std::move(e));
  c.start(sts2::cards::make_silent_starter_deck());

  const std::string s = render_to_string(c);
  EXPECT_THAT(s, HasSubstr(std::string(glyphs::kSwords) + "12"));
}

// T-RND-230 — Cultist DarkStrike attack token renders ⚔{n} with NO space
// between glyph and value (sts2-cli parity: play.py:721 emits f"⚔{dmg}").
// Self-bound T_RND_205 accepts either form, so we pin the no-space format here.
TEST(RenderCombat, T_RND_230_CultistDarkStrikeNoSpace) {
  Combat c = make_starter_combat(kCombatTestSeed);
  c.end_turn();  // Advance to R2; both cultists roll to DarkStrike.
  ASSERT_EQ(c.enemies()[0].current_move, MoveId::kDarkStrike);
  ASSERT_EQ(c.enemies()[1].current_move, MoveId::kDarkStrike);

  const std::string s = render_to_string(c);

  // Derive expected damage from live enemy state (Strength accrual from R1
  // Ritual is applied via compute_outgoing).
  const int dmg0 = sts2::damage::compute_outgoing(
      c.enemies()[0].vitals.powers, c.enemies()[0].dark_strike_base.value());
  const int dmg1 = sts2::damage::compute_outgoing(
      c.enemies()[1].vitals.powers, c.enemies()[1].dark_strike_base.value());

  EXPECT_THAT(s,
              HasSubstr(std::string(glyphs::kSwords) + std::to_string(dmg0)));
  EXPECT_THAT(s,
              HasSubstr(std::string(glyphs::kSwords) + std::to_string(dmg1)));
  // Belt: explicit no-space check — glyph followed immediately by digit, never
  // " ".
  EXPECT_THAT(s, Not(HasSubstr(std::string(glyphs::kSwords) + " ")));
}

}  // namespace

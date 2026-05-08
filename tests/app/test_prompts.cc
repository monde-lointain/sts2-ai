// Tests for src/app/prompts.{h,cc} — main.cc helpers hoisted per §2.1.
// Spec: docs/test-plan/02-test-specifications.md §13.4 (T-MAIN-095..105),
// §13.5 (T-MAIN-110..125), §13.6 (T-MAIN-130..135).
//
// Stream-injected prompt helpers consume an std::istream and write to an
// std::ostream, so all tests use std::istringstream / std::ostringstream
// rather than mocking a TTY.

#include <gmock/gmock.h>
#include <gtest/gtest.h>

#include <cstdint>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

#include "sts2/app/prompts.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/enemy.h"
#include "sts2/game/rng.h"
#include "sts2/game/vitals.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/expected_values.h"

namespace {

using sts2::app::prompt_discard;
using sts2::app::prompt_index;
using sts2::app::prompt_target;
using sts2::tests::helpers::KillEnemy;
using sts2::tests::helpers::MakeStarterCombat;
using sts2::tests::seeds::kCombatTestSeed;
using ::testing::HasSubstr;

using Card = sts2::game::Card;
using Combat = sts2::game::Combat;
using Rng = sts2::game::Rng;

// -------------------------------------------------------------------------
// 13.4  prompt_index  (T-MAIN-095..105)
// -------------------------------------------------------------------------

// T-MAIN-095 — BP, EP — Valid first input. D2 TRUE on first iter.
TEST(AppPromptIndex, T_MAIN_095_ValidFirstInput) {
  std::istringstream in("3\n");
  std::ostringstream out;
  const int idx = prompt_index(out, in, "pick> ", /*max_inclusive=*/5);
  EXPECT_EQ(idx, 3);
  EXPECT_THAT(out.str(), HasSubstr("pick>"));
}

// T-MAIN-100 — BP, EG — Invalid then valid: "abc\n2\n". D2 FALSE → loop.
// out should contain the label twice and the invalid-index notice once.
TEST(AppPromptIndex, T_MAIN_100_InvalidThenValid) {
  std::istringstream in("abc\n2\n");
  std::ostringstream out;
  const int idx = prompt_index(out, in, "pick> ", /*max_inclusive=*/5);
  EXPECT_EQ(idx, 2);

  const std::string s = out.str();
  // Label appears once per prompt cycle; first "abc" rejects, then "2".
  std::size_t first = s.find("pick>");
  ASSERT_NE(first, std::string::npos);
  std::size_t second = s.find("pick>", first + 1);
  EXPECT_NE(second, std::string::npos) << "label not printed twice";
  EXPECT_THAT(s, HasSubstr("invalid index"));
}

// T-MAIN-105 — EG — All-invalid stream EOFs and re-prompts forever.
// Documented livelock hazard per test plan §14.3 U-4: prompt_index has no
// EOF bail-out, so a finite invalid stream loops indefinitely. Skipped to
// avoid hanging the suite; tracked as a refactor item.
TEST(AppPromptIndex, T_MAIN_105_EofLivelockSkipped) {
  GTEST_SKIP() << "EOF-livelock hazard; see test plan §14.3 U-4";
}

// -------------------------------------------------------------------------
// 13.5  prompt_target  (T-MAIN-110..125)
// -------------------------------------------------------------------------

// T-MAIN-110 — BP — No alive enemies → -1. D2 TRUE.
TEST(AppPromptTarget, T_MAIN_110_NoEnemiesReturnsMinusOne) {
  Combat c{kCombatTestSeed};  // no add_enemy calls
  std::istringstream in;      // never read
  std::ostringstream out;     // never written

  EXPECT_EQ(prompt_target(c, in, out), -1);
  EXPECT_TRUE(out.str().empty());
}

// T-MAIN-115 — BP — Exactly one alive enemy → returns its index without
// consulting the input stream. D3 TRUE.
TEST(AppPromptTarget, T_MAIN_115_SingleAliveReturnsIndexNoStream) {
  Combat c{kCombatTestSeed};
  Rng enemy_rng{kCombatTestSeed};
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));

  // If prompt_target wrongly consults the stream, "garbage" would propagate.
  std::istringstream in("garbage\n");
  std::ostringstream out;

  EXPECT_EQ(prompt_target(c, in, out), 0);
  EXPECT_TRUE(out.str().empty());
  // Stream was untouched; "garbage\n" is still pending.
  std::string remaining;
  std::getline(in, remaining);
  EXPECT_EQ(remaining, "garbage");
}

// T-MAIN-120 — BP — Two alive enemies, user picks 1 → returns the second
// alive index. Setup kills enemy 1 so alive_indices = [0, 2]; input "1\n"
// maps to alive_indices[1] = 2. Validates the alive-index → real-index map.
TEST(AppPromptTarget, T_MAIN_120_TwoAliveUserPicksSecond) {
  // Build a 3-enemy combat: starter (cultist 0, cultist 1) + a third.
  Combat c{kCombatTestSeed};
  Rng enemy_rng{kCombatTestSeed};
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));  // idx 0
  c.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));  // idx 1 (will die)
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));  // idx 2

  KillEnemy(c, 1);  // alive_indices becomes [0, 2]

  std::istringstream in("1\n");
  std::ostringstream out;
  EXPECT_EQ(prompt_target(c, in, out), 2);
}

// T-MAIN-125 — EG — Invalid then valid input — verifies the prompt_index
// retry path through prompt_target. Same setup as T-MAIN-120; first "abc"
// rejects, then "0" picks alive_indices[0] = 0.
TEST(AppPromptTarget, T_MAIN_125_InvalidThenValid) {
  Combat c{kCombatTestSeed};
  Rng enemy_rng{kCombatTestSeed};
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));  // idx 0
  c.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));       // idx 1 (die)
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));  // idx 2

  KillEnemy(c, 1);

  std::istringstream in("abc\n0\n");
  std::ostringstream out;
  EXPECT_EQ(prompt_target(c, in, out), 0);
  EXPECT_THAT(out.str(), HasSubstr("invalid index"));
}

// -------------------------------------------------------------------------
// 13.6  prompt_discard  (T-MAIN-130..135)
// -------------------------------------------------------------------------

// T-MAIN-130 — BP — Single-card hand → returns 0 without consulting stream.
TEST(AppPromptDiscard, T_MAIN_130_SingleCardReturnsZeroNoStream) {
  Combat c{kCombatTestSeed};
  Rng enemy_rng{kCombatTestSeed};
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));

  // 1-card deck → start() draws into hand. Ring of the Snake adds 2 to the
  // base draw, but with only 1 card available, hand size becomes 1.
  std::vector<Card> deck;
  deck.push_back(sts2::cards::make_strike());
  c.start(std::move(deck));
  ASSERT_EQ(c.player().hand.size(), 1u);

  std::istringstream in("garbage\n");
  std::ostringstream out;
  EXPECT_EQ(prompt_discard(c, in, out), 0);
  EXPECT_TRUE(out.str().empty());
  // Stream untouched.
  std::string remaining;
  std::getline(in, remaining);
  EXPECT_EQ(remaining, "garbage");
}

// T-MAIN-135 — BP — Multi-card hand: calls render_combat then prompt_index.
// out should contain rendered combat output AND the discard prompt label.
// Returns the user's pick.
TEST(AppPromptDiscard, T_MAIN_135_MultiCardRendersAndPrompts) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  ASSERT_GT(c.player().hand.size(), 1u);  // starter draws 7

  const int max_idx = static_cast<int>(c.player().hand.size()) - 1;
  std::istringstream in("0\n");
  std::ostringstream out;
  EXPECT_EQ(prompt_discard(c, in, out), 0);

  const std::string s = out.str();
  // render_combat output: HP bar / hand list contains "HP" or card names.
  // Use "HP" as a stable substring of the rendered combat header.
  EXPECT_THAT(s, HasSubstr("HP"));
  // Discard prompt label embeds the max index "[0-N]".
  const std::string expected_label =
      "Discard which? [0-" + std::to_string(max_idx) + "]:";
  EXPECT_THAT(s, HasSubstr(expected_label));
}

}  // namespace

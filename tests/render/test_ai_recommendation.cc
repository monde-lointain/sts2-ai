// Tests for src/render/ai_recommendation.{h,cc}.
//
// render_ai_recommendation writes a colored block beneath the battle UI.
// We capture into std::ostringstream and use HasSubstr on non-color
// substrings — color codes are deliberately excluded so reformatting the
// palette never breaks these tests.

#include <gmock/gmock.h>
#include <gtest/gtest.h>

#include <sstream>
#include <string>

#include "sts2/ai/recommend.h"
#include "sts2/game/combat.h"
#include "sts2/game/index_types.h"
#include "sts2/game/types.h"
#include "sts2/input/input.h"
#include "sts2/render/ai_recommendation.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/expected_values.h"

namespace {

using ::testing::HasSubstr;
using ::testing::IsEmpty;
using ::testing::Not;

using sts2::tests::helpers::KillEnemy;
using sts2::tests::helpers::MakeStarterCombat;
using sts2::tests::seeds::kCombatTestSeed;

using sts2::ai::PvStep;
using sts2::ai::Recommendation;
using sts2::game::CardId;
using sts2::game::Combat;
using sts2::input::Action;

std::string Render(const Recommendation& rec, const Combat& c) {
  std::ostringstream os;
  sts2::render::render_ai_recommendation(rec, c, os);
  return os.str();
}

TEST(RenderAiRecommendation, PlayCardWithTargetIncludesEnemyNameAndIndex) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  // Find a Strike in hand for a realistic kPlayCard hand_idx.
  int strike_idx = -1;
  for (std::size_t i = 0; i < c.player().hand.size(); ++i) {
    if (c.player().hand[i].id == CardId::kStrike) {
      strike_idx = static_cast<int>(i);
      break;
    }
  }
  ASSERT_GE(strike_idx, 0);

  Recommendation rec;
  rec.action = Action{Action::kPlayCard, sts2::game::HandIndex{strike_idx}};
  rec.target_idx = sts2::game::EnemySlot{0};
  rec.expected_hp = 42.7;
  rec.expected_rounds = 8.2;
  rec.principal_variation.push_back(
      PvStep{PvStep::kPlayCard, CardId::kStrike, sts2::game::EnemySlot{0},
             CardId::kNone});
  rec.principal_variation.push_back(PvStep{PvStep::kEndTurn, CardId::kNone,
                                           sts2::game::EnemySlot::none(),
                                           CardId::kNone});

  const std::string s = Render(rec, c);

  EXPECT_THAT(s, HasSubstr("AI:"));
  EXPECT_THAT(s, HasSubstr("Play Strike"));
  EXPECT_THAT(s, HasSubstr("Calcified Cultist"));
  EXPECT_THAT(s, HasSubstr("[0]"));
  EXPECT_THAT(s, HasSubstr("E[HP]=42.7"));
  EXPECT_THAT(s, HasSubstr("E[turns]=8.2"));
  EXPECT_THAT(s, HasSubstr("PV:"));
  EXPECT_THAT(s, HasSubstr("Strike -> [0]"));
  EXPECT_THAT(s, HasSubstr("EndTurn"));
  EXPECT_THAT(s, HasSubstr("(then chance)"));
}

TEST(RenderAiRecommendation, PostDeath_RenumberAliveEnemies) {
  Combat c = MakeStarterCombat(kCombatTestSeed);
  // Kill Calcified Cultist (slot 0); Damp Cultist (slot 1) is now display [0].
  KillEnemy(c, 0);
  ASSERT_LE(c.enemies()[0].vitals.hp, 0);
  ASSERT_GT(c.enemies()[1].vitals.hp, 0);

  Recommendation rec;
  rec.action = Action{Action::kPlayCard, sts2::game::HandIndex{0}};
  rec.target_idx = sts2::game::EnemySlot{1};  // engine slot for Damp
  rec.expected_hp = 30.0;
  rec.expected_rounds = 4.0;
  rec.principal_variation.push_back(
      PvStep{PvStep::kPlayCard, CardId::kStrike, sts2::game::EnemySlot{1},
             CardId::kNone});

  const std::string s = Render(rec, c);

  EXPECT_THAT(s, HasSubstr("[0] Damp Cultist"));
  EXPECT_THAT(s, Not(HasSubstr("[1] Damp Cultist")));
}

TEST(RenderAiRecommendation, EndTurnRendersEndTurnText) {
  Combat c = MakeStarterCombat(kCombatTestSeed);

  Recommendation rec;
  rec.action = Action{Action::kEndTurn, sts2::game::HandIndex::none()};
  rec.expected_hp = 50.0;
  rec.expected_rounds = 3.0;
  rec.principal_variation.push_back(
      PvStep{PvStep::kEndTurn, CardId::kNone, sts2::game::EnemySlot::none(),
             CardId::kNone});

  const std::string s = Render(rec, c);

  EXPECT_THAT(s, HasSubstr("End turn"));
  EXPECT_THAT(s, HasSubstr("E[HP]=50.0"));
  EXPECT_THAT(s, HasSubstr("(then chance)"));
}

TEST(RenderAiRecommendation, SurvivorIncludesDiscardSuggestion) {
  Combat c = MakeStarterCombat(kCombatTestSeed);

  Recommendation rec;
  // Use any in-bounds index; the renderer reads the card id from the hand
  // for the action line. Since starter hand may not contain Survivor at a
  // known index, we exercise the survivor_discard_id branch directly and
  // accept whatever the action card name resolves to (the discard hint is
  // what the test checks).
  rec.action = Action{Action::kPlayCard, sts2::game::HandIndex{0}};
  rec.target_idx = sts2::game::EnemySlot::none();
  rec.survivor_discard_id = CardId::kDefend;
  rec.expected_hp = 60.0;
  rec.expected_rounds = 5.5;

  const std::string s = Render(rec, c);

  EXPECT_THAT(s, HasSubstr("discarding"));
  EXPECT_THAT(s, HasSubstr("Defend"));
}

TEST(RenderAiRecommendation, PvSurvivorStepIncludesDropAnnotation) {
  Combat c = MakeStarterCombat(kCombatTestSeed);

  Recommendation rec;
  rec.action = Action{Action::kEndTurn, sts2::game::HandIndex::none()};
  rec.expected_hp = 1.0;
  rec.expected_rounds = 1.0;
  rec.principal_variation.push_back(
      PvStep{PvStep::kPlayCard, CardId::kSurvivor,
             sts2::game::EnemySlot::none(), CardId::kStrike});

  const std::string s = Render(rec, c);

  EXPECT_THAT(s, HasSubstr("Survivor"));
  EXPECT_THAT(s, HasSubstr("(drop Strike)"));
  // No EndTurn at the end → no "(then chance)" suffix.
  EXPECT_THAT(s, Not(HasSubstr("(then chance)")));
}

TEST(RenderAiRecommendation, CombatOverRendersShortLineNoPv) {
  Combat c = MakeStarterCombat(kCombatTestSeed);

  Recommendation rec;
  rec.combat_over = true;
  rec.action = Action{Action::kEndTurn, sts2::game::HandIndex::none()};
  rec.expected_hp = 70.0;

  const std::string s = Render(rec, c);

  EXPECT_THAT(s, HasSubstr("combat over"));
  EXPECT_THAT(s, Not(HasSubstr("E[HP]=")));
  EXPECT_THAT(s, Not(HasSubstr("PV:")));
}

}  // namespace

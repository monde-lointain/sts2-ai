#include <gtest/gtest.h>

#include <cstddef>
#include <cstdint>
#include <vector>

#include "sts2/ai/recommend.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemy.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"
#include "sts2/input/input.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::ai::PvStep;
using sts2::ai::Recommendation;
using sts2::ai::Recommender;
using sts2::game::Card;
using sts2::game::CardId;
using sts2::game::Combat;
using sts2::game::Enemy;
using sts2::game::MoveId;
using sts2::game::Vitals;
using sts2::input::Action;
using sts2::tests::helpers::KillEnemy;
using sts2::tests::helpers::MakeCombatWithEnemy;

// Tiny deck: 5 Strikes only. Keeps the chance branching factor at the round-1
// draw small so default ctest stays fast (full silent starter is multi-minute
// per first solve). Round-1 draws 7 from a 5-card deck — 5 cards land in hand,
// piles are empty afterwards.
std::vector<Card> MakeTinyStrikeDeck() {
  std::vector<Card> deck;
  for (int i = 0; i < 5; ++i) deck.push_back(sts2::cards::make_card(sts2::game::CardId::kStrike));
  return deck;
}

// Small-fixture combat: one enemy with controllable hp, real DarkStrike move
// so the search bottoms out via player death on bad lines, plus the tiny
// Strike-only deck above.
Combat MakeTinyCombat(uint64_t seed, int enemy_hp) {
  Combat c{seed};
  Enemy e{};
  e.vitals = Vitals{enemy_hp, enemy_hp, 0, {}};
  e.dark_strike_base = 9;
  e.current_move = MoveId::kDarkStrike;
  e.performed_first_move = true;
  c.add_enemy(std::move(e));
  c.set_pick_discard_callback(
      [](const Combat&) { return sts2::game::HandIndex{0}; });
  c.start(MakeTinyStrikeDeck());
  return c;
}

TEST(Recommend, FreshStarter_ReturnsLegalAction) {
  // Five seeds. Tiny fixture so each first-solve is ~ms, not minutes.
  for (uint64_t seed : {1ULL, 2ULL, 3ULL, 4ULL, 5ULL}) {
    Combat combat = MakeTinyCombat(seed, /*enemy_hp=*/30);
    Recommender rec;
    const Recommendation r = rec.recommend(combat);

    ASSERT_FALSE(r.combat_over) << "seed=" << seed;
    if (r.action.kind == Action::kPlayCard) {
      EXPECT_TRUE(combat.can_play(r.action.card_idx))
          << "seed=" << seed << " card_idx=" << r.action.card_idx.raw();
    } else {
      EXPECT_EQ(r.action.kind, Action::kEndTurn) << "seed=" << seed;
    }
  }
}

TEST(Recommend, TerminalState_FlagsCombatOver) {
  Combat combat = MakeCombatWithEnemy(0xABCDEFULL, /*hp=*/40);
  KillEnemy(combat, 0);
  combat.check_win_or_lose();
  ASSERT_TRUE(combat.combat_over());

  Recommender rec;
  const Recommendation r = rec.recommend(combat);
  EXPECT_TRUE(r.combat_over);
  EXPECT_TRUE(r.principal_variation.empty());
}

TEST(Recommend, PvAdvancesCorrectly_LethalPosition) {
  // 1 enemy at 6 hp + 5-Strike deck. Round-1 draws 7 from 5 cards: hand has
  // all 5 Strikes (draw stops when pile empty per Hand::draw_from). Energy=3
  // (max_energy default). One Strike at idx 0 lethals the enemy.
  Combat combat = MakeTinyCombat(0x12345ULL, /*enemy_hp=*/6);
  ASSERT_FALSE(combat.combat_over());
  ASSERT_EQ(combat.player().hand.size(), 5u);

  Recommender rec;
  const Recommendation r = rec.recommend(combat);
  ASSERT_FALSE(r.combat_over);
  ASSERT_FALSE(r.principal_variation.empty());

  // First step is the killing Strike at target 0.
  EXPECT_EQ(r.action.kind, Action::kPlayCard);
  EXPECT_EQ(r.target_idx, sts2::game::EnemySlot{0});
  const PvStep& first = r.principal_variation.front();
  EXPECT_EQ(first.kind, PvStep::kPlayCard);
  EXPECT_EQ(first.card_id, CardId::kStrike);
  EXPECT_EQ(first.target_idx, sts2::game::EnemySlot{0});

  // PV truncates at first chance event (EndTurn must be last if present).
  for (std::size_t i = 0; i + 1 < r.principal_variation.size(); ++i) {
    EXPECT_NE(r.principal_variation[i].kind, PvStep::kEndTurn)
        << "PV step " << i << " is EndTurn but not last";
  }
}

TEST(Recommend, RecommenderTtPersistsAcrossCalls) {
  Combat combat = MakeTinyCombat(0xDEADBEEFULL, /*enemy_hp=*/6);

  Recommender rec;
  (void)rec.recommend(combat);
  const std::size_t after_first = rec.tt_size();
  (void)rec.recommend(combat);
  const std::size_t after_second = rec.tt_size();

  EXPECT_GT(after_first, 0u);
  EXPECT_GE(after_second, after_first)
      << "TT shrank between recommend calls: " << after_first << " -> "
      << after_second;
}

}  // namespace

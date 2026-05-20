#include <gtest/gtest.h>

#include <array>
#include <cstdint>
#include <numeric>
#include <vector>

#include "sts2/ai/chance.h"
#include "sts2/ai/state.h"
#include "sts2/ai/state_builders.h"
#include "sts2/ai/transition.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/types.h"
#include "tests/ai/test_helpers.h"

// ============================================================================
// test_chance.cc (wave-22.α — new)
// ============================================================================
// Exercises the chance-node enumeration helpers in chance.h:
//
//   - enumerate_branch_outcomes(MonsterMove, current_move_idx): the
//     CannotRepeat re-normalization rule. Pure over its inputs → directly
//     testable with synthetic MonsterMove instances (no dependency on
//     populated kMonsterMoveTables data).
//
//   - enumerate_chance_outcomes(state) — kAtChanceDraw passthrough: covers
//     regression-protection of the wave-19 B.2-β draw enumeration.
//
// Data-dependency note (per dispatch prompt §22.α Option 1):
//   The full-stack kAtEnemyMoveRng → kAtChanceDraw → kPlayerActing path
//   requires populated slime move tables. C.2-α merges with slime tables
//   ZERO-FILLED (MonsterMoveTable{}); C.3-β populates the real LeafSlimeS
//   / TwigSlimeM data. Full-stack tests at the chance-helper level (e.g.,
//   "TwigSlimeM after POKEY → 2 outcomes summing to 1.0") will land in the
//   C.3-β data wave's regression suite, since they are MEANINGFUL only
//   after slime data is populated. The pure-input enumerate_branch_outcomes
//   helper is the testable substrate here.
// ============================================================================

namespace sts2::ai {
namespace {

using sts2::ai::CardCounts;
using sts2::ai::CompactState;
using sts2::ai::CompactStateBuilder;
using sts2::ai::EnemyStateBuilder;
using sts2::ai::Phase;
using sts2::game::CardId;
using sts2::game::MonsterKind;
using sts2::game::MoveEffectKind;
using sts2::game::MoveId;
using sts2::game::PowerKind;
using sts2::game::Stat;
using sts2::game::monster_moves::FollowUpRule;
using sts2::game::monster_moves::kMaxFollowUps;
using sts2::game::monster_moves::MonsterMove;
using sts2::tests::ai::make_counts;

constexpr double kEps = 1e-12;

// ---------------------------------------------------------------------------
// Synthetic-move helpers. Each constructs a MonsterMove with the indicated
// RandomBranch follow-up shape. The owning kMonsterMoveTables is NOT used —
// these flow only into enumerate_branch_outcomes for pure-input testing.
// ---------------------------------------------------------------------------

// LeafSlimeS-shape: TACKLE_MOVE(idx 0) ↔ GOOP_MOVE(idx 1), both CannotRepeat.
//   weights are unused under CannotRepeat alternation in upstream
//   AddBranch(MoveRepeatType.CannotRepeat) — the runtime weight defaults to
//   1 (upstream RandomBranchState semantics: every branch is added with an
//   implicit weight of 1 when only a MoveRepeatType is provided). We mirror
//   that with branch_weights[i]=1 for all i.
constexpr MonsterMove make_leaf_slime_s_random_branch() {
  MonsterMove m;
  m.id = MoveId::kTackleMove;  // placeholder; the move's own id is irrelevant
                               // for branch enumeration (which reads only the
                               // follow_up_rule + branch_* arrays).
  m.follow_up_rule = FollowUpRule::kRandomBranchCannotRepeat;
  m.branch_count = 2;
  m.branch_indices = {0, 1, 0, 0};  // TACKLE=0, GOOP=1
  m.branch_weights = {1, 1, 0, 0};  // implicit weight 1 per branch
  m.branch_cannot_repeat = {true, true, false, false};
  return m;
}

// TwigSlimeM-shape: POKEY_POUNCE(idx 0, weight 2, NOT CannotRepeat) ↔
//   STICKY_SHOT_MOVE(idx 1, weight 1, CannotRepeat). Upstream:
//     randomBranchState.AddBranch(moveState, 2);                    // POKEY
//     w=2 randomBranchState.AddBranch(moveState2, MoveRepeatType.CannotRepeat);
//                                                                     // STICKY
//                                                                     w=1 + CR
constexpr MonsterMove make_twig_slime_m_random_branch() {
  MonsterMove m;
  m.id = MoveId::kPokeyPounce;
  m.follow_up_rule = FollowUpRule::kWeightedRandomCannotRepeat;
  m.branch_count = 2;
  m.branch_indices = {0, 1, 0, 0};
  m.branch_weights = {2, 1, 0, 0};
  m.branch_cannot_repeat = {false, true, false, false};
  return m;
}

// ---------------------------------------------------------------------------
// Sum probabilities; helper for normalization checks.
// ---------------------------------------------------------------------------
double sum_probs(const std::vector<BranchOutcome>& v) {
  double s = 0.0;
  for (const auto& o : v) {
    s += o.probability;
  }
  return s;
}

double sum_probs(const std::vector<ChanceOutcome>& v) {
  double s = 0.0;
  for (const auto& o : v) {
    s += o.probability;
  }
  return s;
}

// Build a representative cultist-style CompactState in kAtChanceDraw phase
// for the draw-passthrough test. Mirrors test_zobrist.cc::make_cultist_state
// shape but with phase set so enumerate_chance_outcomes follows the draw
// branch. enemy_count=2, hand={2,2,1,0,0} (Slimed=0 — preserves invariant).
CompactState make_cultist_chance_state(int round, CardCounts hand,
                                       CardCounts draw, CardCounts discard) {
  return CompactStateBuilder()
      .player_hp(Stat{70})
      .player_block(Stat{0})
      .energy(Stat{3})
      .round(static_cast<uint16_t>(round))
      .phase(Phase::kAtChanceDraw)
      .enemy(0, EnemyStateBuilder()
                    .kind(MonsterKind::kCultistCalcified)
                    .hp(Stat{40})
                    .block(Stat{0})
                    .dark_strike_base(Stat{9})
                    .ritual_amount(Stat{2})
                    .current_move(MoveId::kDarkStrike)
                    .move_index(1)
                    .alive(true)
                    .performed_first_move(true)
                    .build())
      .enemy(1, EnemyStateBuilder()
                    .kind(MonsterKind::kCultistDamp)
                    .hp(Stat{52})
                    .block(Stat{0})
                    .dark_strike_base(Stat{1})
                    .ritual_amount(Stat{5})
                    .current_move(MoveId::kDarkStrike)
                    .move_index(1)
                    .alive(true)
                    .performed_first_move(true)
                    .build())
      .enemy_count(2)
      .hand(hand)
      .draw(draw)
      .discard(discard)
      .build();
}

// ===========================================================================
// Branch-outcome enumeration (pure helper) tests — wave-22.α.
// ===========================================================================

TEST(Chance, BranchOutcomes_LeafSlimeS_AfterTackle_DeterministicGoop) {
  // LeafSlimeS pattern: BOTH branches CannotRepeat with weight 1.
  // current_move_idx = 0 (TACKLE) → TACKLE branch (idx 0, CannotRepeat)
  // is filtered; only GOOP (idx 1) remains; p = 1/1 = 1.0.
  const auto m = make_leaf_slime_s_random_branch();
  const auto outcomes = enumerate_branch_outcomes(m, /*current_move_idx=*/0);
  ASSERT_EQ(outcomes.size(), 1U);
  EXPECT_EQ(outcomes[0].new_move_idx, 1U) << "expected GOOP (idx 1)";
  EXPECT_NEAR(outcomes[0].probability, 1.0, kEps);
  EXPECT_NEAR(sum_probs(outcomes), 1.0, kEps);
}

TEST(Chance, BranchOutcomes_LeafSlimeS_AfterGoop_DeterministicTackle) {
  // Mirror: current_move_idx = 1 (GOOP) → GOOP branch is filtered; only
  // TACKLE (idx 0) remains; p = 1.0.
  const auto m = make_leaf_slime_s_random_branch();
  const auto outcomes = enumerate_branch_outcomes(m, /*current_move_idx=*/1);
  ASSERT_EQ(outcomes.size(), 1U);
  EXPECT_EQ(outcomes[0].new_move_idx, 0U) << "expected TACKLE (idx 0)";
  EXPECT_NEAR(outcomes[0].probability, 1.0, kEps);
  EXPECT_NEAR(sum_probs(outcomes), 1.0, kEps);
}

TEST(Chance,
     BranchOutcomes_TwigSlimeM_AfterPokey_BothBranchesEligibleWeighted) {
  // current_move_idx = 0 (POKEY). POKEY has NOT-CannotRepeat → eligible
  // (weight 2). STICKY has CannotRepeat but current != STICKY → eligible
  // (weight 1). Normalizer = 3. p(POKEY) = 2/3, p(STICKY) = 1/3.
  const auto m = make_twig_slime_m_random_branch();
  const auto outcomes = enumerate_branch_outcomes(m, /*current_move_idx=*/0);
  ASSERT_EQ(outcomes.size(), 2U);
  // Outputs preserve declared branch order (i.e., index 0 first, then 1).
  EXPECT_EQ(outcomes[0].new_move_idx, 0U);
  EXPECT_NEAR(outcomes[0].probability, 2.0 / 3.0, kEps);
  EXPECT_EQ(outcomes[1].new_move_idx, 1U);
  EXPECT_NEAR(outcomes[1].probability, 1.0 / 3.0, kEps);
  EXPECT_NEAR(sum_probs(outcomes), 1.0, kEps);
}

TEST(Chance,
     BranchOutcomes_TwigSlimeM_AfterSticky_DeterministicPokeyViaCannotRepeat) {
  // current_move_idx = 1 (STICKY). STICKY has CannotRepeat AND current ==
  // STICKY → STICKY filtered. Only POKEY remains, weight 2 → normalizer 2
  // → p(POKEY) = 1.0.
  const auto m = make_twig_slime_m_random_branch();
  const auto outcomes = enumerate_branch_outcomes(m, /*current_move_idx=*/1);
  ASSERT_EQ(outcomes.size(), 1U);
  EXPECT_EQ(outcomes[0].new_move_idx, 0U);
  EXPECT_NEAR(outcomes[0].probability, 1.0, kEps);
  EXPECT_NEAR(sum_probs(outcomes), 1.0, kEps);
}

TEST(Chance,
     BranchOutcomes_OrderingDeterministic_DeclaredBranchOrderPreserved) {
  // Two RandomBranch invocations on the same synthetic move with the same
  // current_move_idx must return outcomes in the SAME order — chance-sum
  // FP order is load-bearing per chance.h documentation.
  const auto m = make_twig_slime_m_random_branch();
  const auto a = enumerate_branch_outcomes(m, /*current_move_idx=*/0);
  const auto b = enumerate_branch_outcomes(m, /*current_move_idx=*/0);
  ASSERT_EQ(a.size(), b.size());
  for (std::size_t i = 0; i < a.size(); ++i) {
    EXPECT_EQ(a[i].new_move_idx, b[i].new_move_idx) << "i=" << i;
    EXPECT_NEAR(a[i].probability, b[i].probability, kEps) << "i=" << i;
  }
}

TEST(Chance, BranchOutcomes_ThreeWayWeighted_NoCannotRepeat) {
  // Synthetic 3-way branch with weights 1/2/3 and no CannotRepeat. All
  // branches eligible regardless of current_move_idx; p_i = w_i / 6.
  MonsterMove m;
  m.id = MoveId::kIncantation;  // arbitrary
  m.follow_up_rule = FollowUpRule::kWeightedRandomCannotRepeat;
  m.branch_count = 3;
  m.branch_indices = {0, 1, 2, 0};
  m.branch_weights = {1, 2, 3, 0};
  m.branch_cannot_repeat = {false, false, false, false};
  const auto outcomes = enumerate_branch_outcomes(m, /*current_move_idx=*/0);
  ASSERT_EQ(outcomes.size(), 3U);
  EXPECT_NEAR(outcomes[0].probability, 1.0 / 6.0, kEps);
  EXPECT_NEAR(outcomes[1].probability, 2.0 / 6.0, kEps);
  EXPECT_NEAR(outcomes[2].probability, 3.0 / 6.0, kEps);
  EXPECT_NEAR(sum_probs(outcomes), 1.0, kEps);
}

// ===========================================================================
// kAtChanceDraw passthrough (regression guard for wave-19 B.2-β).
// ===========================================================================

TEST(Chance, AtChanceDraw_DrawOutcomesEnumerated_SumsToOne) {
  // Cultist-style state at kAtChanceDraw with non-empty draw + non-zero
  // hand to draw into. enumerate_chance_outcomes must return non-empty
  // outcomes summing to 1.0 (the wave-19 draw enumeration contract).
  const CardCounts hand = CardCounts{};             // empty hand pre-draw
  const CardCounts draw = make_counts(3, 3, 0, 1);  // 7 cards available
  const CardCounts discard = make_counts(0, 0, 0, 0);
  const CompactState s =
      make_cultist_chance_state(/*round=*/1, hand, draw, discard);

  const auto outcomes = enumerate_chance_outcomes(s);
  ASSERT_FALSE(outcomes.empty());
  EXPECT_NEAR(sum_probs(outcomes), 1.0, 1e-9);
  // Every outcome must advance phase to kPlayerActing (apply_draw contract).
  for (const auto& o : outcomes) {
    EXPECT_EQ(o.child_state.get_phase(), Phase::kPlayerActing);
  }
}

TEST(Chance, AtChanceDraw_DrawPlusDiscardReshuffle_StillSumsToOne) {
  // Draw pile alone can't satisfy k=5 (round 1 draws 5); discard supplies
  // the remainder via reshuffle. Outcomes must still sum to 1.0.
  const CardCounts hand = CardCounts{};
  const CardCounts draw = make_counts(2, 0, 0, 0);
  const CardCounts discard = make_counts(2, 3, 0, 1);
  const CompactState s =
      make_cultist_chance_state(/*round=*/1, hand, draw, discard);

  const auto outcomes = enumerate_chance_outcomes(s);
  ASSERT_FALSE(outcomes.empty());
  EXPECT_NEAR(sum_probs(outcomes), 1.0, 1e-9);
  for (const auto& o : outcomes) {
    EXPECT_EQ(o.child_state.get_phase(), Phase::kPlayerActing);
  }
}

// ===========================================================================
// Framework invariants — Phase enum + types still byte-compatible.
// ===========================================================================

TEST(Chance, PhaseEnum_kAtEnemyMoveRng_IsValueTwo) {
  // The wave-22.α APPEND-only contract: kAtEnemyMoveRng must be value 2
  // (after kPlayerActing=0, kAtChanceDraw=1). Inserting before existing
  // values would BREAK the cultist Zobrist byte-identity pin (the
  // Zobrist phase[] table consumes mt19937 outputs at indices 0+1 in
  // PHASE 1 and APPENDS index 2 in PHASE 2).
  EXPECT_EQ(static_cast<uint8_t>(Phase::kPlayerActing), 0U);
  EXPECT_EQ(static_cast<uint8_t>(Phase::kAtChanceDraw), 1U);
  EXPECT_EQ(static_cast<uint8_t>(Phase::kAtEnemyMoveRng), 2U);
}

TEST(Chance, CardIdEnum_kSlimed_IsValueFive) {
  // The wave-22.α APPEND-only contract for CardId: kSlimed=5 follows the
  // ordering invariant in state.h (CardCounts::to_index requires
  // kCountedCardIds[i] == CardId(i+1)).
  EXPECT_EQ(static_cast<int>(CardId::kNone), 0);
  EXPECT_EQ(static_cast<int>(CardId::kStrike), 1);
  EXPECT_EQ(static_cast<int>(CardId::kDefend), 2);
  EXPECT_EQ(static_cast<int>(CardId::kNeutralize), 3);
  EXPECT_EQ(static_cast<int>(CardId::kSurvivor), 4);
  EXPECT_EQ(static_cast<int>(CardId::kSlimed), 5);
}

TEST(Chance, MoveEffectKindEnum_kAddStatusCard_IsAppended) {
  // Wave-22.α APPEND-only contract: kAddStatusCard appended at the end.
  EXPECT_EQ(static_cast<uint8_t>(MoveEffectKind::kNone), 0U);
  EXPECT_EQ(static_cast<uint8_t>(MoveEffectKind::kAttack), 1U);
  EXPECT_EQ(static_cast<uint8_t>(MoveEffectKind::kDefend), 2U);
  EXPECT_EQ(static_cast<uint8_t>(MoveEffectKind::kBuffSelf), 3U);
  EXPECT_EQ(static_cast<uint8_t>(MoveEffectKind::kDebuffPlayer), 4U);
  EXPECT_EQ(static_cast<uint8_t>(MoveEffectKind::kAddStatusCard), 5U);
}

}  // namespace
}  // namespace sts2::ai

#include <gtest/gtest.h>

#include <cstddef>
#include <cstdint>
#include <random>
#include <vector>

#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/game/card.h"
#include "sts2/game/combat.h"
#include "sts2/game/player.h"
#include "sts2/game/types.h"
#include "tests/game/test_helpers.h"

namespace {

using sts2::ai::CardCounts;
using sts2::ai::CompactState;
using sts2::ai::from_combat;
using sts2::ai::transition::Action;
using sts2::ai::transition::ActionKind;
using sts2::ai::transition::apply_draw;
using sts2::ai::transition::apply_player_action;
using sts2::ai::transition::is_terminal;
using sts2::ai::transition::legal_actions;
using sts2::ai::transition::resolve_end_turn_pre_draw;
using sts2::game::CardId;
using sts2::tests::helpers::MakeStarterCombat;

constexpr int kSeedCount = 200;
constexpr int kMaxStepsPerSeed = 100;
constexpr uint64_t kSamplerSalt = 0x9E3779B97F4A7C15ULL;

Action pick_random_action(const CompactState& s, std::mt19937& rng) {
  const auto actions = legal_actions(s);
  if (actions.empty()) {
    Action end;
    end.kind = ActionKind::kEndTurn;
    return end;
  }
  std::uniform_int_distribution<std::size_t> dist(0, actions.size() - 1);
  return actions[dist(rng)];
}

int find_hand_index(const sts2::game::Combat& combat, CardId id) {
  const auto& hand = combat.player().hand;
  for (std::size_t i = 0; i < hand.size(); ++i) {
    if (hand[i].id == id) return static_cast<int>(i);
  }
  return -1;
}

CardCounts hand_to_counts(const sts2::game::Combat& combat) {
  CardCounts c;
  for (const auto& card : combat.player().hand) {
    switch (card.id) {
      case CardId::kStrike:     ++c.strike;     break;
      case CardId::kDefend:     ++c.defend;     break;
      case CardId::kNeutralize: ++c.neutralize; break;
      case CardId::kSurvivor:   ++c.survivor;   break;
      case CardId::kNone:                       break;
    }
  }
  return c;
}

TEST(AiStateParity, RandomWalk_CompactStateMatchesCombat) {
  for (int seed = 0; seed < kSeedCount; ++seed) {
    sts2::game::Combat combat = MakeStarterCombat(static_cast<uint64_t>(seed));
    CompactState compact = from_combat(combat);

    // Survivor's discard target is decided by the engine via a callback. To
    // keep the engine in lockstep with the AI's chosen discard_id, capture the
    // most-recent action by reference and resolve it to a hand index lazily.
    Action last_action;
    combat.set_pick_discard_callback(
        [&last_action](const sts2::game::Combat& c) -> int {
          if (last_action.survivor_discard_id == CardId::kNone) return 0;
          for (std::size_t i = 0; i < c.player().hand.size(); ++i) {
            if (c.player().hand[i].id == last_action.survivor_discard_id) {
              return static_cast<int>(i);
            }
          }
          return 0;
        });

    std::mt19937 sampler_rng(static_cast<std::uint_fast32_t>(
        static_cast<uint64_t>(seed) ^ kSamplerSalt));

    int steps_executed = 0;
    for (int step = 0; step < kMaxStepsPerSeed; ++step) {
      if (combat.combat_over() || is_terminal(compact)) break;

      ASSERT_EQ(from_combat(combat), compact)
          << "pre-step divergence; seed=" << seed << " step=" << step;

      const Action action = pick_random_action(compact, sampler_rng);
      last_action = action;

      if (action.kind == ActionKind::kEndTurn) {
        ASSERT_TRUE(apply_player_action(compact, action))
            << "EndTurn rejected; seed=" << seed << " step=" << step;
        resolve_end_turn_pre_draw(compact);

        if (is_terminal(compact)) {
          combat.end_turn();
          ++steps_executed;
          break;
        }

        combat.end_turn();
        if (combat.combat_over()) {
          // Engine declared combat over (player died) but our pre-draw resolver
          // did not — divergence.
          ASSERT_TRUE(is_terminal(compact))
              << "engine terminal but compact not; seed=" << seed
              << " step=" << step;
          ++steps_executed;
          break;
        }

        const CardCounts drawn = hand_to_counts(combat);
        apply_draw(compact, drawn);
      } else {
        const int hand_idx = find_hand_index(combat, action.card_id);
        ASSERT_GE(hand_idx, 0)
            << "card not in engine hand; seed=" << seed << " step=" << step
            << " card=" << static_cast<int>(action.card_id);
        const int target = (action.target_idx == sts2::ai::transition::kNoTarget)
                               ? -1
                               : static_cast<int>(action.target_idx);

        ASSERT_TRUE(apply_player_action(compact, action))
            << "AI rejected legal action; seed=" << seed << " step=" << step;
        ASSERT_TRUE(combat.play_card(hand_idx, target))
            << "engine rejected play; seed=" << seed << " step=" << step;
      }

      ++steps_executed;

      ASSERT_EQ(from_combat(combat), compact)
          << "post-step divergence; seed=" << seed << " step=" << step;
    }

    EXPECT_GE(steps_executed, 1) << "no steps ran; seed=" << seed;

    if (!combat.combat_over() && !is_terminal(compact)) {
      EXPECT_EQ(from_combat(combat), compact)
          << "final divergence; seed=" << seed;
    }
  }
}

}  // namespace

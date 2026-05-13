#pragma once

#include <cstdint>
#include <string>

#include "sts2/ai/transition.h"
#include "sts2/game/types.h"

// One pinned scenario row in the Q2 per-encounter regression registry.
// Q2-ADR-005 stamping discipline: every row carries algorithm-SHA +
// registry-SHA so downstream consumers (Q10 prioritized-sampling, Q12
// gate evaluator) can quarantine by (algorithm, registry) version.
//
// Phase-1A scope: CULTISTS_NORMAL only (Q2-ADR-002). The encounter_id
// field is forward-laid for per-encounter expansion; today all rows
// carry "CULTISTS_NORMAL".
//
// Consumed by S2 stream-B (within-CULTISTS_NORMAL search pin tests) and
// S2 stream-D (tools/seed-pinner --manifest extension).

namespace sts2::oracle::registry {

struct PinnedScenarioRow {
  std::string encounter_id;
  std::uint64_t seed = 0;
  std::string
      algorithm_sha;  // sts2::oracle::adapter::current_manifest().algorithm_sha
  std::string registry_sha;  // current_phase1_registry_sha256()
  // Pin payload — matches the AdapterRoundtrip pinned triple shape.
  sts2::ai::transition::ActionKind action_kind =
      sts2::ai::transition::ActionKind::kEndTurn;
  sts2::game::CardId action_card_id = sts2::game::CardId::kNone;
  int action_target_idx = -1;  // EnemySlot underlying value; -1 = none.
  double expected_hp = 0.0;
  double expected_rounds = 0.0;

  bool operator==(const PinnedScenarioRow&) const = default;
};

}  // namespace sts2::oracle::registry

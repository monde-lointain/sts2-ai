#include <gtest/gtest.h>

#include "sts2/ai/transition.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/manifest.h"
#include "sts2/oracle/registry/pin_row.h"
#include "sts2/oracle/registry/sha.h"

// Tests for PinnedScenarioRow (S2-T2). Covers default equality, field-
// inequality, and the Q2-ADR-005 interop path where rows are populated
// from current_manifest() + current_phase1_registry_sha256().

namespace {

using sts2::ai::transition::ActionKind;
using sts2::game::CardId;
using sts2::oracle::adapter::current_manifest;
using sts2::oracle::registry::current_phase1_registry_sha256;
using sts2::oracle::registry::PinnedScenarioRow;

PinnedScenarioRow fixture1_like_row() {
  // Mirrors the S1-T5 fixture #1 pin shape; concrete values are illustrative
  // (we do not pin Search outputs here — that's stream-B's job).
  return PinnedScenarioRow{
      .encounter_id = "CULTISTS_NORMAL",
      .seed = 0x42,
      .algorithm_sha = current_manifest().algorithm_sha,
      .registry_sha = current_phase1_registry_sha256(),
      .action_kind = ActionKind::kPlayCard,
      .action_card_id = CardId::kStrike,
      .action_target_idx = 0,
      .expected_hp = 60.774403172281517,
      .expected_rounds = 6.4320807758307383,
  };
}

TEST(PinnedScenarioRow, DefaultEquality_EqualRowsCompareEqual) {
  const auto a = fixture1_like_row();
  const auto b = fixture1_like_row();
  EXPECT_EQ(a, b);
}

TEST(PinnedScenarioRow, FieldInequality_DifferingSeedCompareUnequal) {
  auto a = fixture1_like_row();
  auto b = fixture1_like_row();
  b.seed = 0xC0FFEE;
  EXPECT_NE(a, b);
}

TEST(PinnedScenarioRow, FieldInequality_DifferingActionCompareUnequal) {
  auto a = fixture1_like_row();
  auto b = fixture1_like_row();
  b.action_card_id = CardId::kDefend;
  EXPECT_NE(a, b);
}

TEST(PinnedScenarioRow, FieldInequality_DifferingExpectedHpCompareUnequal) {
  auto a = fixture1_like_row();
  auto b = fixture1_like_row();
  b.expected_hp += 1e-3;
  EXPECT_NE(a, b);
}

TEST(PinnedScenarioRow, Q2Adr005Interop_AlgorithmAndRegistryShasStamped) {
  const auto row = fixture1_like_row();
  EXPECT_FALSE(row.algorithm_sha.empty())
      << "algorithm_sha must be populated from current_manifest()";
  EXPECT_FALSE(row.registry_sha.empty())
      << "registry_sha must be populated from current_phase1_registry_sha256()";
  EXPECT_EQ(row.registry_sha.size(), 64U)
      << "registry_sha is a 64-char hex string (SHA-256 lowercase-hex)";
  // algorithm_sha is the Phase-1A stub per Q2-ADR-005; just assert non-empty
  // shape, not specific value (the stub marker will change to real SHA at S3+).
}

}  // namespace

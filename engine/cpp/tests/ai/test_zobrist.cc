#include <gtest/gtest.h>

#include <array>
#include <cstdint>
#include <random>
#include <type_traits>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include "sts2/ai/state.h"
#include "sts2/ai/zobrist.h"
#include "sts2/game/combat.h"
#include "sts2/game/types.h"
#include "tests/ai/test_helpers.h"
#include "tests/game/test_helpers.h"
#include "tests/seeds/cultist_zobrist_pin.h"
#include "tests/seeds/expected_values.h"

namespace sts2::ai {
namespace {

using sts2::game::CardId;
using sts2::game::MonsterKind;
using sts2::game::MoveId;
using sts2::game::PowerKind;
using sts2::game::Stat;

// ---------------------------------------------------------------------------
// Test fixture helpers.
// ---------------------------------------------------------------------------

// Build a representative cultist-style CompactState. Mirrors the kind of
// state the search visits during the cultist solve.
CompactState make_cultist_state() {
  EnemyState e0 = EnemyStateBuilder()
                      .kind(MonsterKind::kCultistCalcified)
                      .hp(Stat{40})
                      .block(Stat{0})
                      .dark_strike_base(Stat{9})
                      .ritual_amount(Stat{2})
                      .current_move(MoveId::kIncantation)
                      .move_index(0)
                      .alive(true)
                      .performed_first_move(false)
                      .build();
  EnemyState e1 = EnemyStateBuilder()
                      .kind(MonsterKind::kCultistDamp)
                      .hp(Stat{52})
                      .block(Stat{0})
                      .dark_strike_base(Stat{1})
                      .ritual_amount(Stat{5})
                      .current_move(MoveId::kIncantation)
                      .move_index(0)
                      .alive(true)
                      .performed_first_move(false)
                      .build();
  return CompactStateBuilder()
      .player_hp(Stat{70})
      .player_block(Stat{0})
      .energy(Stat{3})
      .round(1)
      .phase(Phase::kPlayerActing)
      .enemy(0, e0)
      .enemy(1, e1)
      .enemy_count(2)
      .hand(sts2::tests::ai::make_counts(2, 2, 1, 0))
      .draw(sts2::tests::ai::make_counts(3, 3, 0, 1))
      .discard(sts2::tests::ai::make_counts(0, 0, 0, 0))
      .build();
}

// ---------------------------------------------------------------------------
// Layout / static-asserts.
// ---------------------------------------------------------------------------

TEST(ZobristKey, LayoutInvariants) {
  static_assert(sizeof(ZobristKey) == 16);
  static_assert(std::is_trivially_copyable_v<ZobristKey>);
  static_assert(std::is_standard_layout_v<ZobristKey>);

  const ZobristKey a{};
  const ZobristKey b{};
  EXPECT_EQ(a, b);
  const ZobristKey c{0x1, 0x2};
  EXPECT_NE(a, c);
}

// ---------------------------------------------------------------------------
// Required test cases (per dispatch spec §Task 4).
// ---------------------------------------------------------------------------

TEST(Zobrist, SameStateSameHash) {
  const CompactState s = make_cultist_state();
  const ZobristKey k1 = zobrist_of(s);
  const ZobristKey k2 = zobrist_of(s);
  EXPECT_EQ(k1, k2);

  // And again via a fresh copy of the same state.
  const CompactState s_copy = s;
  EXPECT_EQ(zobrist_of(s_copy), k1);
}

TEST(Zobrist, DistinctStatesDistinctHashes) {
  const CompactState base = make_cultist_state();
  const ZobristKey k_base = zobrist_of(base);

  // Each mutation differs from base in exactly one field.
  std::vector<CompactState> mutations;
  mutations.push_back(CompactStateBuilder(base).player_hp(Stat{69}).build());
  mutations.push_back(CompactStateBuilder(base).player_block(Stat{5}).build());
  mutations.push_back(CompactStateBuilder(base).energy(Stat{2}).build());
  mutations.push_back(CompactStateBuilder(base).round(2).build());
  mutations.push_back(
      CompactStateBuilder(base).phase(Phase::kAtChanceDraw).build());
  mutations.push_back(
      CompactStateBuilder(base).player_strength(Stat{3}).build());
  mutations.push_back(CompactStateBuilder(base).player_weak(Stat{1}).build());
  // Mutate enemy 0 hp.
  {
    EnemyState e0_mut =
        EnemyStateBuilder(base.get_enemy(0)).hp(Stat{30}).build();
    mutations.push_back(CompactStateBuilder(base).enemy(0, e0_mut).build());
  }
  // Mutate enemy 1 block.
  // Wave-22-fix-4/H.gamma dropped enemy_dsb + enemy_ritual Zobrist tables
  // (constant-per-MonsterKind), so ritual_amount mutations are NO LONGER
  // hash-distinct. Switched to block, which remains hashed.
  {
    EnemyState e1_mut =
        EnemyStateBuilder(base.get_enemy(1)).block(Stat{7}).build();
    mutations.push_back(CompactStateBuilder(base).enemy(1, e1_mut).build());
  }
  // Mutate hand: drop one Strike.
  mutations.push_back(CompactStateBuilder(base)
                          .hand(sts2::tests::ai::make_counts(1, 2, 1, 0))
                          .build());
  // Mutate draw: swap one Defend for a Strike.
  mutations.push_back(CompactStateBuilder(base)
                          .draw(sts2::tests::ai::make_counts(4, 2, 0, 1))
                          .build());
  // Mutate discard: add a Survivor.
  mutations.push_back(CompactStateBuilder(base)
                          .discard(sts2::tests::ai::make_counts(0, 0, 0, 1))
                          .build());

  // Every mutation must produce a key distinct from the base, AND distinct
  // from every other mutation (no accidental collisions across the
  // single-field-differs sample).
  std::unordered_set<uint64_t> seen_lo;
  std::unordered_set<uint64_t> seen_hi;
  seen_lo.insert(k_base.lo);
  seen_hi.insert(k_base.hi);
  for (const auto& m : mutations) {
    const ZobristKey k = zobrist_of(m);
    EXPECT_NE(k, k_base) << "mutation collides with base";
    // For 128-bit composite distinctness, distinct on EITHER half suffices.
    // We still check that no two mutations produce identical lo+hi pair.
    EXPECT_TRUE(seen_lo.insert(k.lo).second || seen_hi.insert(k.hi).second)
        << "mutation collides with prior mutation";
  }
}

TEST(Zobrist, PositionalNonAliasing) {
  // Two enemies with IDENTICAL content placed in swapped slots must hash
  // differently — the "Strength=5 on enemy 0" vs "Strength=5 on enemy 1"
  // invariant.
  EnemyState ea = EnemyStateBuilder()
                      .kind(MonsterKind::kCultistCalcified)
                      .hp(Stat{40})
                      .dark_strike_base(Stat{9})
                      .ritual_amount(Stat{2})
                      .current_move(MoveId::kIncantation)
                      .alive(true)
                      .strength(Stat{5})
                      .build();
  EnemyState eb = EnemyStateBuilder()
                      .kind(MonsterKind::kCultistDamp)
                      .hp(Stat{52})
                      .dark_strike_base(Stat{1})
                      .ritual_amount(Stat{5})
                      .current_move(MoveId::kIncantation)
                      .alive(true)
                      .build();

  const CompactState s_ab = CompactStateBuilder()
                                .player_hp(Stat{70})
                                .energy(Stat{3})
                                .round(1)
                                .enemy(0, ea)
                                .enemy(1, eb)
                                .enemy_count(2)
                                .build();
  const CompactState s_ba = CompactStateBuilder()
                                .player_hp(Stat{70})
                                .energy(Stat{3})
                                .round(1)
                                .enemy(0, eb)
                                .enemy(1, ea)
                                .enemy_count(2)
                                .build();
  EXPECT_NE(zobrist_of(s_ab), zobrist_of(s_ba))
      << "positional aliasing — Zobrist must distinguish enemy slots";
}

TEST(Zobrist, PowerCombinationCoverage) {
  // Across the (PowerKind, stacks) input space within audited bounds, every
  // distinct combination must produce a distinct key contribution at a given
  // power slot. flags variation is not reachable via public builders today;
  // covered indirectly by the lookup-table assertion fire at out-of-range
  // flag values.
  //
  // We probe two channels:
  //   1. Player powers via player_weak / player_strength typed builders
  //      (kWeak slot 0, kStrength slot 0 — only two kinds with typed setters).
  //   2. Enemy slot 0 powers via EnemyStateBuilder::add_power generic setter
  //      (covers all six PowerKind values; exercises a different table
  //      region from channel 1).

  constexpr int kStacksMax = 20;  // sub-sample (full 100 audited in zobrist.cc)

  // Channel 1: player_weak (PowerKind::kWeak) — varying stacks.
  {
    std::unordered_set<uint64_t> seen;
    for (int stacks = 1; stacks < kStacksMax; ++stacks) {
      CompactState s = CompactStateBuilder()
                           .player_hp(Stat{70})
                           .energy(Stat{3})
                           .round(1)
                           .enemy_count(0)
                           .player_weak(Stat{stacks})
                           .build();
      const ZobristKey k = zobrist_of(s);
      EXPECT_TRUE(seen.insert(k.lo).second)
          << "player_weak stacks=" << stacks << " collides";
    }
  }
  // Channel 1: player_strength (PowerKind::kStrength) — varying stacks.
  {
    std::unordered_set<uint64_t> seen;
    for (int stacks = 1; stacks < kStacksMax; ++stacks) {
      CompactState s = CompactStateBuilder()
                           .player_hp(Stat{70})
                           .energy(Stat{3})
                           .round(1)
                           .enemy_count(0)
                           .player_strength(Stat{stacks})
                           .build();
      const ZobristKey k = zobrist_of(s);
      EXPECT_TRUE(seen.insert(k.lo).second)
          << "player_strength stacks=" << stacks << " collides";
    }
  }
  // Channel 2: enemy slot 0 powers across ALL six PowerKind values.
  {
    std::unordered_set<uint64_t> seen;
    constexpr int kKinds = 6;  // kPowerKindCardinality
    for (int kind = 0; kind < kKinds; ++kind) {
      for (int stacks = 1; stacks < kStacksMax; ++stacks) {
        EnemyState e = EnemyStateBuilder()
                           .kind(MonsterKind::kCultistCalcified)
                           .hp(Stat{40})
                           .dark_strike_base(Stat{9})
                           .ritual_amount(Stat{2})
                           .alive(true)
                           .add_power(static_cast<PowerKind>(kind), stacks)
                           .build();
        CompactState s = CompactStateBuilder()
                             .player_hp(Stat{70})
                             .energy(Stat{3})
                             .round(1)
                             .enemy(0, e)
                             .enemy_count(1)
                             .build();
        const ZobristKey k = zobrist_of(s);
        EXPECT_TRUE(seen.insert(k.lo).second)
            << "enemy power coverage collision at kind=" << kind
            << " stacks=" << stacks;
      }
    }
  }
}

TEST(Zobrist, SeedDeterminism) {
  // Determinism contract: same state hashed in independent calls must give
  // identical keys. Equivalent to verifying the underlying table init is
  // seed-deterministic (Meyers singleton seeded once with kZobristSeedLo /
  // kZobristSeedHi — re-invoking zobrist_of pulls the same cached tables).
  const CompactState s = make_cultist_state();
  const ZobristKey k_a = zobrist_of(s);

  // Construct a structurally distinct state, then return to the original
  // via a fresh builder; hash must match.
  const CompactState other = CompactStateBuilder(s).round(99).build();
  (void)zobrist_of(other);  // force a second invocation between the two
  const CompactState s_again = make_cultist_state();
  const ZobristKey k_b = zobrist_of(s_again);
  EXPECT_EQ(k_a, k_b)
      << "Zobrist tables are not seed-deterministic across calls";
}

// ---------------------------------------------------------------------------
// Wave-21.β byte-identity gate: cultist root ZobristKey must match the
// constants captured pre-wave-21 (cultist_zobrist_pin.h). Asserts the
// kMaxEnemies 2→4 widening + Zobrist table append-only fill order preserved
// slot 0+1's mt19937_64 outputs. Failure = wave-21 rollback signal.
// ---------------------------------------------------------------------------
TEST(Zobrist, CultistRootKey_MatchesPreWave21Pin) {
  sts2::game::Combat combat = sts2::tests::helpers::make_starter_combat(
      sts2::tests::seeds::kCombatTestSeed);
  const CompactState state = from_combat(combat);
  const ZobristKey key = zobrist_of(state);

  EXPECT_EQ(key.lo, sts2::tests::seeds::kCultistZobristKeyLo)
      << "cultist Zobrist lo half drifted from pre-wave-21 pin — "
         "mt19937_64 fill order regressed OR fold_enemy loop bound is "
         "kMaxEnemies (must be enemy_count)";
  EXPECT_EQ(key.hi, sts2::tests::seeds::kCultistZobristKeyHi)
      << "cultist Zobrist hi half drifted from pre-wave-21 pin — "
         "mt19937_64 fill order regressed OR fold_enemy loop bound is "
         "kMaxEnemies (must be enemy_count)";
}

TEST(ZobristKeyHash, BucketSpreadSanity) {
  // Insert a synthetic batch of distinct ZobristKeys into a hash map and
  // verify the distribution is non-degenerate. We don't have absl wired into
  // the test target directly, so we use std::unordered_map<ZobristKey, ...,
  // ZobristKeyHash> which exposes load_factor + bucket_count APIs.
  std::unordered_map<ZobristKey, int, ZobristKeyHash> m;
  std::mt19937_64 rng(0xBEEFCAFEULL);
  constexpr std::size_t kN = 10'000;
  m.reserve(kN);
  for (std::size_t i = 0; i < kN; ++i) {
    ZobristKey k{rng(), rng()};
    m.emplace(k, 1);
  }
  EXPECT_GE(m.size(), kN - 5U);  // collision probability ~10^-15 per pair
  EXPECT_GT(m.bucket_count(), 0U);
  // Catch-degenerate-hash: the worst-case bucket should not hold more than
  // ~max(8, 2 * load_factor). A perfectly uniform hash on 10k entries with
  // load_factor ~1 would average ~1 per bucket; allow generous slack.
  std::size_t max_bucket = 0;
  for (std::size_t b = 0; b < m.bucket_count(); ++b) {
    max_bucket = std::max(max_bucket, m.bucket_size(b));
  }
  EXPECT_LT(max_bucket, 16U)
      << "Degenerate ZobristKeyHash distribution: max bucket depth "
      << max_bucket;
}

}  // namespace
}  // namespace sts2::ai

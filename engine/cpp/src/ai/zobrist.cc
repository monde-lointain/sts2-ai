// ============================================================================
// Zobrist key tables (cultist + Louse + slime + Nibbit search-pin invariant).
//
// Current cardinality table (live; bump in lockstep with the enums in
// engine/cpp/include/sts2/game/types.h):
//   Player HP / Block:        [0, 1024)
//   Player Energy:            [0, 8)
//   Round:                    [0, 256)
//   Phase:                    [0, 3)     — sts2::ai::Phase
//   PowerKind:                sts2::game::kPowerKindCardinality
//   MoveId:                   sts2::game::kMoveIdCardinality
//   MonsterKind:              sts2::game::kMonsterKindCardinality
//   MoveEffectKind:           sts2::game::kMoveEffectKindCardinality
//   PowerInstance.stacks:     [0, 256)
//   PowerInstance.flags:      [0, 4)
//   Enemy HP / Block:         [0, 1024)
//   kMaxEnemies:              4 (sts2::ai::kMaxEnemies)
//   kMaxPowersPerCreature:    4 (sts2::ai::kMaxPowersPerCreature)
//   CardCounts: kCountedCardIds.size() × kCardZoneCount=3 × [0, 64)
//
// Historical fill-order rationale + APPEND-only PHASE-N discipline + cultist
// Zobrist byte rotations across wave-21.β / wave-22.α / wave-22-fix-4/H.gamma /
// wave-23/J.beta / wave-25/L.α / wave-26/M.β / wave-33/A.β:
//   docs/specs/01-decisions-log.md §ADR-031 (Zobrist Cardinality Audit,
//   Archive). Cross-refs: Q2-ADR-013 Amendment 4, Q2-ADR-014, Q2-ADR-015.
//
// Cultist Zobrist BYTE pin (post-wave-33/A.β fill_enemy_slot extraction):
//   Lo=0xa5d5769283d589b5, Hi=0x403677d8cd214204
//   (see tests/seeds/cultist_zobrist_pin.h).
// ============================================================================

#include "sts2/ai/zobrist.h"

#include <algorithm>
#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <random>

#include "sts2/ai/state.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/types.h"

namespace sts2::ai {

namespace {

// ---------------------------------------------------------------------------
// Cardinality constants (audited above).
// Wave-23/J.beta widened HP/Block/Stacks/Card-count tables to match upstream
// STS2's wider stat domain (Q2-ADR-014). mt19937_64 fill order shifts; the
// cultist Zobrist BYTE rotates and pin is re-stamped; cultist + Louse search
// VALUES remain bit-identical (search invariant within reachable stat range).
// ---------------------------------------------------------------------------
constexpr std::size_t kMaxHp = 1024;
constexpr std::size_t kMaxBlock = 1024;
constexpr std::size_t kMaxEnergy = 8;
constexpr std::size_t kMaxRound = 256;
// Phase: pre-wave-22 cardinality was 2 (kPlayerActing=0, kAtChanceDraw=1).
// Wave-22.α APPENDS kAtEnemyMoveRng=2 — kPhasePreWave22Cardinality is the
// frozen pre-wave-22 slot count consumed during PHASE-1 mt19937 fill (slot
// 2 is APPEND-only in PHASE 2). Cultist + LouseProgenitor states never set
// phase=2, so byte identity holds.
constexpr std::size_t kPhasePreWave22Cardinality = 2;
constexpr std::size_t kPhaseCardinality = 3;
// PowerKind enum tail = kVulnerable=5; count = 6.
// kPowerKindCardinality migrated to include/sts2/game/types.h (wave-32/C1-β).
using sts2::game::kPowerKindCardinality;
// MoveId enum tail = kHissMove=12 post-wave-24/K.β; cardinality 13.
// Pre-wave-22 value was 5 (only cultist + Louse MoveIds used).
// Wave-22 widens with APPEND fill order to make slime MoveIds hashable.
// Wave-24/K.β APPENDS kButtMove(10), kSliceMove(11), kHissMove(12) → 13.
// kMoveIdCardinality migrated to include/sts2/game/types.h (wave-28/B.2).
using sts2::game::kMoveIdCardinality;
constexpr std::size_t kPreWave22MoveIdCardinality = 5;
static_assert(kPreWave22MoveIdCardinality <= kMoveIdCardinality);
// MonsterKind cardinality DECOUPLED from monster_moves::kMonsterKindCount
// (wave-21.β kept at 3 to preserve cultist byte identity). Wave-22 widens
// to 7 to make slime MonsterKinds hashable.
// Wave-24/K.β APPENDS kNibbit(7) → cardinality 8.
// kMonsterKindCardinality migrated to include/sts2/game/types.h (wave-32/C1-β).
using sts2::game::kMonsterKindCardinality;
constexpr std::size_t kPreWave22MonsterKindCardinality = 3;
static_assert(kPreWave22MonsterKindCardinality <= kMonsterKindCardinality);
constexpr std::size_t kMaxStacks = 256;
constexpr std::size_t kMaxFlags = 4;
constexpr std::size_t kMaxMovesPerMonster =
    sts2::game::monster_moves::kMaxMovesPerMonster;
// Wave-22-fix-4/H.gamma: kMaxDarkStrikeBase + kMaxRitualAmount removed
// (constant-per-MonsterKind; enemy_kind XOR already distinguishes cultist
// normal/elite). Q2-ADR-013 Amendment 4 §Cultist-byte-rotation.
constexpr std::size_t kMaxCountPerCardZone = 64;
constexpr std::size_t kCardIdCardinality =
    sts2::game::card_effects::kCountedCardIds.size();
// Pre-wave-22 CardId cardinality (kStrike,kDefend,kNeutralize,kSurvivor = 4
// rows). Wave-22.α APPENDS kSlimed at index 4. PHASE 1 fills rows [0,4) per
// pre-wave-22 mt19937 consumption order; PHASE 2 fills rows [4,5).
// IMPORTANT: the count=0 slot of the new row (card_counts[z][4][0]) is left
// AS ZERO (NOT consumed from mt19937) so cultist (kSlimed always 0) hashes
// XOR-contribute 0 → byte identity holds.
constexpr std::size_t kPreWave22CardIdCardinality = 4;
static_assert(kPreWave22CardIdCardinality <= kCardIdCardinality,
              "kPreWave22CardIdCardinality must not exceed kCardIdCardinality");
// 3 card zones: hand, draw, discard.
constexpr std::size_t kCardZoneCount = 3;
// Pre-wave-21 kMaxEnemies (used by generate_table() to phase the mt19937 fill
// order; PHASE 1 fills slots [0, kPreWave21MaxEnemies); PHASE 2 appends slots
// [kPreWave21MaxEnemies, kMaxEnemies)). Cultist + LouseProgenitor consume
// only PHASE-1 outputs, so their pre-wave-21 ZobristKey bytes are preserved.
constexpr std::size_t kPreWave21MaxEnemies = 2;
static_assert(kPreWave21MaxEnemies <= kMaxEnemies,
              "kPreWave21MaxEnemies must not exceed kMaxEnemies");

// ---------------------------------------------------------------------------
// ZobristTables — one logical instance per 64-bit half. Sized to the bounds
// above; out-of-range indices assert at lookup time.
// ---------------------------------------------------------------------------
struct ZobristTables {
  // Player slots.
  std::array<uint64_t, kMaxHp> player_hp{};
  std::array<uint64_t, kMaxBlock> player_block{};
  std::array<uint64_t, kMaxEnergy> player_energy{};
  std::array<uint64_t, kMaxRound> round{};
  std::array<uint64_t, kPhaseCardinality> phase{};

  // Player powers: [power_slot][PowerKind][stacks][flags].
  std::array<std::array<std::array<std::array<uint64_t, kMaxFlags>, kMaxStacks>,
                        kPowerKindCardinality>,
             kMaxPowersPerCreature>
      player_power{};
  std::array<uint64_t, static_cast<std::size_t>(kMaxPowersPerCreature) + 1>
      player_power_count{};

  // Enemy slots — outer dimension is enemy slot index in [0, kMaxEnemies).
  std::array<std::array<uint64_t, kMaxHp>, kMaxEnemies> enemy_hp{};
  std::array<std::array<uint64_t, kMaxBlock>, kMaxEnemies> enemy_block{};
  std::array<std::array<uint64_t, kMonsterKindCardinality>, kMaxEnemies>
      enemy_kind{};
  std::array<std::array<uint64_t, kMaxMovesPerMonster>, kMaxEnemies>
      enemy_move_idx{};
  std::array<std::array<uint64_t, kMoveIdCardinality>, kMaxEnemies>
      enemy_current_move{};
  std::array<std::array<uint64_t, 2>, kMaxEnemies> enemy_alive{};
  std::array<std::array<uint64_t, 2>, kMaxEnemies> enemy_pfm{};
  // Wave-22-fix-4/H.gamma: enemy_dsb + enemy_ritual tables removed
  // (constant-per-MonsterKind; enemy_kind XOR already distinguishes cultist
  // normal/elite). Saves ~2 KB static. Q2-ADR-013 Amendment 4 §Compression.

  // Enemy powers: [enemy_slot][power_slot][PowerKind][stacks][flags].
  std::array<
      std::array<
          std::array<std::array<std::array<uint64_t, kMaxFlags>, kMaxStacks>,
                     kPowerKindCardinality>,
          kMaxPowersPerCreature>,
      kMaxEnemies>
      enemy_power{};
  std::array<
      std::array<uint64_t, static_cast<std::size_t>(kMaxPowersPerCreature) + 1>,
      kMaxEnemies>
      enemy_power_count{};

  // Active enemy count (CompactState::enemy_count_ in [0, kMaxEnemies]).
  std::array<uint64_t, static_cast<std::size_t>(kMaxEnemies) + 1> enemy_count{};

  // Card zones × card_id × count.
  std::array<std::array<std::array<uint64_t, kMaxCountPerCardZone>,
                        kCardIdCardinality>,
             kCardZoneCount>
      card_counts{};
};

// Fill an N-dim array recursively. Generic helper so we don't open-code every
// table fill — guarantees identical seeding order across tables of any rank.
template <typename T, std::size_t N>
void fill_array(std::array<T, N>& a, std::mt19937_64& rng) noexcept;

template <std::size_t N>
void fill_array(std::array<uint64_t, N>& a, std::mt19937_64& rng) noexcept {
  std::generate(a.begin(), a.end(), [&rng]() { return rng(); });
}

template <typename T, std::size_t N>
void fill_array(std::array<T, N>& a, std::mt19937_64& rng) noexcept {
  for (auto& sub : a) {
    fill_array(sub, rng);
  }
}

// Fill a SUBSET of an outer-indexed enemy table — slots [slot_lo, slot_hi).
// Inner cardinality (per slot) is whatever the table type declares; we just
// iterate the outer index range. Used by generate_table() to split the
// mt19937_64 consumption into PHASE 1 (legacy slots) + PHASE 2 (appended).
template <typename T, std::size_t N>
void fill_slots(std::array<T, N>& a, std::size_t slot_lo, std::size_t slot_hi,
                std::mt19937_64& rng) noexcept {
  assert(slot_lo <= slot_hi);
  assert(slot_hi <= N);
  for (std::size_t i = slot_lo; i < slot_hi; ++i) {
    fill_array(a[i], rng);
  }
}

// Fill the 7 non-phased per-slot enemy fields for a given slot in EXACTLY this
// order (preserves mt19937_64 consumption order within each slot):
//   hp → block → move_idx → alive → pfm → power (4-level loop) → power_count.
// DO NOT include enemy_kind or enemy_current_move — they have phased
// cardinality and their manual inner loops MUST stay inline at PHASE-1 and
// PHASE-2 sites.
static void fill_enemy_slot(ZobristTables& t, std::size_t slot,
                            std::mt19937_64& rng) noexcept {
  fill_array(t.enemy_hp[slot], rng);
  fill_array(t.enemy_block[slot], rng);
  fill_array(t.enemy_move_idx[slot], rng);
  fill_array(t.enemy_alive[slot], rng);
  fill_array(t.enemy_pfm[slot], rng);
  for (std::size_t ps = 0; ps < static_cast<std::size_t>(kMaxPowersPerCreature);
       ++ps) {
    for (std::size_t pk = 0; pk < kPowerKindCardinality; ++pk) {
      for (std::size_t stacks = 0; stacks < kMaxStacks; ++stacks) {
        for (std::size_t flags = 0; flags < kMaxFlags; ++flags) {
          t.enemy_power[slot][ps][pk][stacks][flags] = rng();
        }
      }
    }
  }
  fill_array(t.enemy_power_count[slot], rng);
}

// Generate one half-table seeded by the given 64-bit seed. Deterministic and
// platform-independent (std::mt19937_64 is portable).
//
// Wave-21.β + wave-22.α fill-order contract (see audit-block for rationale):
//
//   PHASE 1 (preserve pre-wave-21 + pre-wave-22 mt19937 consumption order):
//     - All non-enemy tables (player_*, round, phase[0..1], player_power*).
//     - enemy_* tables for slots [0, kPreWave21MaxEnemies) ONLY.
//     - enemy_count entries [0, kPreWave21MaxEnemies] (3 entries: ec=0,1,2).
//     - card_counts[z][cid][count] for cid in [0, kPreWave22CardIdCardinality)
//       — manually iterated to AVOID consuming mt19937 outputs for the
//       wave-22.α APPEND-only cid=4 (kSlimed) row.
//
//   PHASE 2 (APPEND new wave-21.β + wave-22.α content):
//     - enemy_* tables for slots [kPreWave21MaxEnemies, kMaxEnemies).
//     - enemy_count entries [kPreWave21MaxEnemies+1, kMaxEnemies].
//     - phase[2] (kAtEnemyMoveRng) — APPENDED.
//     - card_counts[z][cid=4][count] for count in [1, kMaxCountPerCardZone).
//       The count=0 slot (card_counts[z][4][0]) is LEFT AT ZERO so cultist
//       (kSlimed always 0) XOR-contributes 0 → byte identity holds.
//
// Cultist (enemy_count=2, phase∈{0,1}, kSlimed=0) and LouseProgenitor (same
// phase + card constraints) only consume PHASE-1 outputs at hash time + the
// zeroed cid=4/count=0 slot → pre-wave-21 + pre-wave-22 byte identity
// preserved.
//
// IMPORTANT: any future widening (wave-22+ kMoveIdCardinality 5→10, etc.)
// MUST follow the same discipline — fill the PRE-WIDENING bytes first, then
// APPEND the new entries. Otherwise the cultist + LouseProgenitor pins drift.
ZobristTables generate_table(uint64_t seed) noexcept {
  std::mt19937_64 rng(seed);
  ZobristTables t;

  // ---- PHASE 1: pre-wave-21 + pre-wave-22 layout ----
  fill_array(t.player_hp, rng);
  fill_array(t.player_block, rng);
  fill_array(t.player_energy, rng);
  fill_array(t.round, rng);
  // phase[0..1] — pre-wave-22 entries. phase[2] APPENDED in PHASE 2.
  for (std::size_t i = 0; i < kPhasePreWave22Cardinality; ++i) {
    t.phase[i] = rng();
  }
  // player_power: kPowerKindCardinality = 6; full range filled here.
  for (std::size_t ps = 0; ps < static_cast<std::size_t>(kMaxPowersPerCreature);
       ++ps) {
    for (std::size_t pk = 0; pk < kPowerKindCardinality; ++pk) {
      for (std::size_t stacks = 0; stacks < kMaxStacks; ++stacks) {
        for (std::size_t flags = 0; flags < kMaxFlags; ++flags) {
          t.player_power[ps][pk][stacks][flags] = rng();
        }
      }
    }
  }
  fill_array(t.player_power_count, rng);
  // enemy_* tables — outer slot dimension iterates only [0,
  // kPreWave21MaxEnemies). Per-slot fill: 7 non-phased fields via
  // fill_enemy_slot; phased fields (kind, current_move) inline with pre-wave-22
  // cardinality so wave-22's bumps don't shift mt19937 consumption.
  // Wave-22-fix-4/H.gamma: enemy_dsb + enemy_ritual fill_slots dropped.
  for (std::size_t slot = 0; slot < kPreWave21MaxEnemies; ++slot) {
    fill_enemy_slot(t, slot, rng);
    // enemy_kind: pre-wave-22 cardinality; new kinds appended in PHASE 3.
    for (std::size_t k = 0; k < kPreWave22MonsterKindCardinality; ++k) {
      t.enemy_kind[slot][k] = rng();
    }
    // enemy_current_move: pre-wave-22 cardinality; new MoveIds in PHASE 3.
    for (std::size_t m = 0; m < kPreWave22MoveIdCardinality; ++m) {
      t.enemy_current_move[slot][m] = rng();
    }
  }
  // enemy_count[0..kPreWave21MaxEnemies] — 3 entries: ec=0, 1, 2.
  for (std::size_t i = 0; i <= kPreWave21MaxEnemies; ++i) {
    t.enemy_count[i] = rng();
  }
  // card_counts[z][cid][count] — manually iterate over PRE-wave-22 cid range
  // [0, kPreWave22CardIdCardinality) to preserve exact mt19937 consumption
  // order. The wave-22.α-appended cid=4 row is filled in PHASE 2 (count>=1
  // slots only; count=0 slots stay at zero).
  for (std::size_t z = 0; z < kCardZoneCount; ++z) {
    for (std::size_t cid = 0; cid < kPreWave22CardIdCardinality; ++cid) {
      for (std::size_t count = 0; count < kMaxCountPerCardZone; ++count) {
        t.card_counts[z][cid][count] = rng();
      }
    }
  }

  // ---- PHASE 2: APPEND new enemy slots [kPreWave21MaxEnemies, kMaxEnemies)
  // --- Same table-fill order as PHASE 1 for symmetry / reviewability.
  // Per-slot fill for slots [kPreWave21MaxEnemies, kMaxEnemies): 7 non-phased
  // fields via fill_enemy_slot; phased fields inline with pre-wave-22
  // cardinality. Wave-22-fix-4/H.gamma: enemy_dsb + enemy_ritual dropped.
  for (std::size_t slot = kPreWave21MaxEnemies;
       slot < static_cast<std::size_t>(kMaxEnemies); ++slot) {
    fill_enemy_slot(t, slot, rng);
    // enemy_kind: pre-wave-22 cardinality; new kinds appended in PHASE 3.
    for (std::size_t k = 0; k < kPreWave22MonsterKindCardinality; ++k) {
      t.enemy_kind[slot][k] = rng();
    }
    // enemy_current_move: pre-wave-22 cardinality; new MoveIds in PHASE 3.
    for (std::size_t m = 0; m < kPreWave22MoveIdCardinality; ++m) {
      t.enemy_current_move[slot][m] = rng();
    }
  }
  // enemy_count[kPreWave21MaxEnemies+1 .. kMaxEnemies] — entries ec=3, 4.
  for (std::size_t i = kPreWave21MaxEnemies + 1;
       i <= static_cast<std::size_t>(kMaxEnemies); ++i) {
    t.enemy_count[i] = rng();
  }
  // phase[2] = kAtEnemyMoveRng — APPENDED. Cultist + LouseProgenitor never
  // see phase=2; this value is consumed only by RandomBranch-enemy hashes.
  for (std::size_t p = kPhasePreWave22Cardinality; p < kPhaseCardinality; ++p) {
    t.phase[p] = rng();
  }
  // card_counts[z][cid=kPreWave22..end][count=1..end] — APPENDED kSlimed row
  // slots for count>=1. The count=0 slot stays at default-zero (see audit
  // block) so cultist (kSlimed always 0) XOR-contributes 0 at this slot.
  for (std::size_t z = 0; z < kCardZoneCount; ++z) {
    for (std::size_t cid = kPreWave22CardIdCardinality;
         cid < kCardIdCardinality; ++cid) {
      // INTENTIONAL: skip count=0; it stays at zero-init for byte identity.
      for (std::size_t count = 1; count < kMaxCountPerCardZone; ++count) {
        t.card_counts[z][cid][count] = rng();
      }
    }
  }

  // ---- PHASE 3: wave-22 + wave-24/K.β cardinality widening ----
  // Slime MonsterKinds (3..6) + MoveIds (5..9) become runtime-reachable
  //   (wave-22); Nibbit kind (7) + Nibbit MoveIds (10..12) appended in
  //   wave-24/K.β. Cultist + LouseProgenitor only index kinds 0..2 and MoveIds
  //   0..4, so byte identity holds.
  for (std::size_t slot = 0; slot < static_cast<std::size_t>(kMaxEnemies);
       ++slot) {
    for (std::size_t k = kPreWave22MonsterKindCardinality;
         k < kMonsterKindCardinality; ++k) {
      t.enemy_kind[slot][k] = rng();
    }
  }
  for (std::size_t slot = 0; slot < static_cast<std::size_t>(kMaxEnemies);
       ++slot) {
    for (std::size_t m = kPreWave22MoveIdCardinality; m < kMoveIdCardinality;
         ++m) {
      t.enemy_current_move[slot][m] = rng();
    }
  }

  return t;
}

// Meyers singletons — C++11 guarantees thread-safe one-time initialization.
const ZobristTables& tables_lo() noexcept {
  static const ZobristTables t = generate_table(kZobristSeedLo);
  return t;
}
const ZobristTables& tables_hi() noexcept {
  static const ZobristTables t = generate_table(kZobristSeedHi);
  return t;
}

// ---------------------------------------------------------------------------
// Indexed lookups with cardinality-bound asserts. The assert message tells the
// engineer to widen the cardinality constant + algorithm_sha if it ever fires
// in practice (out-of-range means the cardinality audit is stale).
// ---------------------------------------------------------------------------
template <typename Arr, typename Idx>
uint64_t at(const Arr& arr, Idx idx) noexcept {
  const auto i = static_cast<std::size_t>(idx);
  assert(i < arr.size() &&
         "Zobrist key index out of cardinality bound — audit needs widening");
  return arr[i];
}

// Wave-23/J.beta: stacks widened int16_t → int32_t to match upstream's
// uniform int stat storage (Q2-ADR-014).
uint64_t lookup_power(
    const std::array<std::array<std::array<uint64_t, kMaxFlags>, kMaxStacks>,
                     kPowerKindCardinality>& slot_table,
    sts2::game::PowerKind kind, int32_t stacks, uint8_t flags) noexcept {
  const auto kind_idx = static_cast<std::size_t>(kind);
  assert(kind_idx < kPowerKindCardinality &&
         "PowerKind index out of cardinality bound");
  // Stacks can be negative (Strength debuff post-card_effects); we encode
  // values in [0, kMaxStacks). Negative inputs would be a state-corruption
  // signal — assert to catch any drift.
  assert(stacks >= 0 && static_cast<std::size_t>(stacks) < kMaxStacks &&
         "PowerInstance stacks out of bound");
  const auto flag_idx = static_cast<std::size_t>(flags);
  assert(flag_idx < kMaxFlags && "PowerInstance flags out of bound");
  return slot_table[kind_idx][static_cast<std::size_t>(stacks)][flag_idx];
}

// ---------------------------------------------------------------------------
// Fold helpers — each XORs the contribution of one logical state group into
// the running hash `h` for the given half-table.
// ---------------------------------------------------------------------------
void fold_player(uint64_t& h, const ZobristTables& t,
                 const CompactState& s) noexcept {
  h ^= at(t.player_hp, s.get_player_hp().pack16());
  h ^= at(t.player_block, s.get_player_block().pack16());
  h ^= at(t.player_energy, s.get_energy().pack16());
  h ^= at(t.round, s.get_round());
  h ^= at(t.phase, static_cast<uint8_t>(s.get_phase()));

  const uint8_t pc = s.get_player_power_count();
  assert(pc <= kMaxPowersPerCreature && "player power_count out of bound");
  const auto& powers = s.get_player_powers();
  for (uint8_t i = 0; i < pc; ++i) {
    const PowerInstance& p = powers[i];
    h ^= lookup_power(t.player_power[i], p.kind, p.stacks, p.flags);
  }
  h ^= at(t.player_power_count, pc);
}

void fold_enemy(uint64_t& h, const ZobristTables& t, std::size_t slot,
                const EnemyState& e) noexcept {
  assert(slot < kMaxEnemies && "enemy slot out of bound");
  h ^= at(t.enemy_hp[slot], e.get_hp().pack16());
  h ^= at(t.enemy_block[slot], e.get_block().pack16());
  h ^= at(t.enemy_kind[slot], static_cast<uint8_t>(e.get_kind()));
  h ^= at(t.enemy_move_idx[slot], e.get_move_index());
  h ^= at(t.enemy_current_move[slot],
          static_cast<uint8_t>(e.get_current_move()));
  h ^= at(t.enemy_alive[slot], e.get_alive() ? 1U : 0U);
  h ^= at(t.enemy_pfm[slot], e.get_performed_first_move() ? 1U : 0U);
  // Wave-22-fix-4/H.gamma: enemy_dsb + enemy_ritual XOR contributions
  // dropped. Both are constant-per-MonsterKind; enemy_kind XOR already
  // separates cultist normal vs elite. Q2-ADR-013 Amendment 4
  // §Cultist-byte-rotation.

  const uint8_t pc = e.get_power_count();
  assert(pc <= kMaxPowersPerCreature && "enemy power_count out of bound");
  const auto& powers = e.get_powers();
  for (uint8_t i = 0; i < pc; ++i) {
    const PowerInstance& p = powers[i];
    h ^= lookup_power(t.enemy_power[slot][i], p.kind, p.stacks, p.flags);
  }
  h ^= at(t.enemy_power_count[slot], pc);
}

void fold_card_zones(uint64_t& h, const ZobristTables& t,
                     const CompactState& s) noexcept {
  const std::array<const CardCounts*, kCardZoneCount> zones = {
      &s.get_hand(), &s.get_draw(), &s.get_discard()};
  for (std::size_t z = 0; z < kCardZoneCount; ++z) {
    const auto& cnts = zones[z]->counts;
    for (std::size_t cid = 0; cid < cnts.size(); ++cid) {
      const auto count = static_cast<std::size_t>(cnts[cid]);
      assert(count < kMaxCountPerCardZone &&
             "CardCounts entry out of cardinality bound");
      h ^= t.card_counts[z][cid][count];
    }
  }
}

uint64_t zobrist_half(const CompactState& s, const ZobristTables& t) noexcept {
  uint64_t h = 0;
  fold_player(h, t, s);
  const uint8_t ec = s.get_enemy_count();
  assert(ec <= kMaxEnemies && "enemy_count out of bound");

  // Wave-25/L.α / Q2-ADR-015 Amendment 1: canonical-form pre-Zobrist swap.
  // Sort the active enemy slots [0..ec) by a deterministic lex-key BEFORE
  // folding. Symmetric reachable states (same-kind enemies in either slot
  // order) then hash IDENTICALLY → TT collapses them.
  //
  // Per-slot key tables (enemy_hp[slot], enemy_kind[slot], etc.) are still
  // indexed by the OUTER LOOP `i` — that's the canonical-form mechanism: the
  // same enemy ends up at the same "canonical slot" regardless of wire
  // position.
  //
  // CompactState slot order is UNCHANGED (only this hash function is
  // canonicalized); `target_idx` action semantics + `derive_best_action`
  // re-derivation per state remain correct (Q2-ADR-015 Amendment 1
  // §Correctness-analysis).
  //
  // Implementation note: ec ≤ kMaxEnemies = 4, so a small in-place
  // insertion sort is used (avoids GCC's std::sort __insertion_sort
  // false-positive -Warray-bounds for tiny ranges below _S_threshold=16).
  std::array<uint8_t, kMaxEnemies> perm{0, 1, 2, 3};
  const auto lex_less = [&s](uint8_t a, uint8_t b) noexcept -> bool {
    const auto& ea = s.get_enemy(a);
    const auto& eb = s.get_enemy(b);
    // Lex-key: most-distinguishing first.
    // 1. alive (true before false; dead slots last)
    if (ea.get_alive() != eb.get_alive()) {
      return ea.get_alive();
    }
    // 2. kind (asc by MonsterKind enum value)
    if (ea.get_kind() != eb.get_kind()) {
      return static_cast<uint8_t>(ea.get_kind()) <
             static_cast<uint8_t>(eb.get_kind());
    }
    // 3. hp
    if (ea.get_hp().value() != eb.get_hp().value()) {
      return ea.get_hp().value() < eb.get_hp().value();
    }
    // 4. current_move
    if (ea.get_current_move() != eb.get_current_move()) {
      return static_cast<int>(ea.get_current_move()) <
             static_cast<int>(eb.get_current_move());
    }
    // 5. block
    if (ea.get_block().value() != eb.get_block().value()) {
      return ea.get_block().value() < eb.get_block().value();
    }
    // 6. performed_first_move (false before true)
    if (ea.get_performed_first_move() != eb.get_performed_first_move()) {
      return !ea.get_performed_first_move();
    }
    // 7. move_index
    if (ea.get_move_index() != eb.get_move_index()) {
      return ea.get_move_index() < eb.get_move_index();
    }
    // 8. power_count
    if (ea.get_power_count() != eb.get_power_count()) {
      return ea.get_power_count() < eb.get_power_count();
    }
    // 9. PowerInstance fields per slot (extend tie-break depth as needed;
    //    first ~5 keys should disambiguate all reachable Phase-1 states).
    const auto& pa = ea.get_powers();
    const auto& pb = eb.get_powers();
    for (uint8_t k = 0; k < ea.get_power_count(); ++k) {
      if (pa[k].kind != pb[k].kind) {
        return static_cast<uint8_t>(pa[k].kind) <
               static_cast<uint8_t>(pb[k].kind);
      }
      if (pa[k].stacks != pb[k].stacks) {
        return pa[k].stacks < pb[k].stacks;
      }
      if (pa[k].flags != pb[k].flags) {
        return pa[k].flags < pb[k].flags;
      }
    }
    // Truly identical → stable.
    return false;
  };
  // Insertion sort over perm[0..ec). ec ≤ 4 — trivially fast.
  for (uint8_t i = 1; i < ec; ++i) {
    const uint8_t key = perm[i];
    uint8_t j = i;
    while (j > 0 && lex_less(key, perm[j - 1])) {
      perm[j] = perm[j - 1];
      --j;
    }
    perm[j] = key;
  }

  for (uint8_t i = 0; i < ec; ++i) {
    fold_enemy(h, t, i, s.get_enemy(perm[i]));
  }

  h ^= at(t.enemy_count, ec);
  fold_card_zones(h, t, s);
  return h;
}

}  // namespace

ZobristKey zobrist_of(const CompactState& s) noexcept {
  return ZobristKey{
      .lo = zobrist_half(s, tables_lo()),
      .hi = zobrist_half(s, tables_hi()),
  };
}

}  // namespace sts2::ai

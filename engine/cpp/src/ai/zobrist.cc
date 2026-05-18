// ============================================================================
// Cardinality audit (wave-21/B.1-β; revises wave-19/B.2-α; 2026-05-17)
// ============================================================================
// Per plan §1, each Zobrist key slot is sized to its feature's reachable
// range. Out-of-range values trigger assertion+abort in zobrist_of(). Audit
// results vs plan-baked bounds:
//
//   Player HP:        [0, 256)   — Stat::pack8 asserts v in [0,255]; cultist
//                                  max ≈ 70 (Silent starter 70 HP).
//   Player Block:     [0, 256)   — Stat::pack8; cultist max ≈ 30.
//   Player Energy:    [0, 8)     — kPlayerStartingEnergy=3 (turn_calc.h);
//                                  no in-combat gain in Phase-1; max = 3.
//   Round:            [0, 256)   — round_ is uint16_t in CompactState;
//                                  cultist solves in ≤ 20 rounds.
//   Phase:            [0, 2)     — enum {kPlayerActing=0, kAtChanceDraw=1}.
//                                  Wave-22 adds kAtEnemyMoveRng → widen to 3.
//   PowerKind:        [0, 6)     — enum has 6 values
//                                  {kWeak, kStrength, kRitual, kCurlUp,
//                                   kFrail, kVulnerable}. No kPowerKindCount
//                                  constant in types.h — bound encoded as
//                                  kPowerKindCardinality below; sync with
//                                  enum when extended.
//   MoveId:           [0, 5)     — table-pinned at pre-wave-21 cardinality
//                                  to preserve cultist Zobrist byte identity
//                                  through the wave-21.α MoveId enum extension
//                                  (5 → 10 enum values added: kPokeyPounce,
//                                  kStickyShot, kSpitBig, kSpitMed,
//                                  kSpitSmall). Wave-22's slime port WIDENS the
//                                  table when the new MoveIds first see runtime
//                                  use; that bump is APPEND-ONLY (mt19937 fill
//                                  order preserved for old kind 0..4).
//   MonsterKind:      [0, 3)     — table-pinned at pre-wave-21 cardinality
//                                  (kCultistCalcified, kCultistDamp,
//                                  kLouseProgenitor). Wave-21.α extends the
//                                  enum to 7 (slime variants); we DECOUPLE
//                                  kMonsterKindCardinality from
//                                  monster_moves::kMonsterKindCount so the
//                                  per-slot table size is stable through the
//                                  α-stream merge (cultist byte identity).
//                                  Wave-22 widens to 7 with APPEND fill order.
//   PowerInstance.stacks: [0, 100) — cultist Ritual = 2/5; Strength compounds
//                                    on Louse +5/cycle, observed ≤ 50.
//                                    Bound 100 leaves comfortable headroom.
//   PowerInstance.flags:  [0, 4)   — bit 0 (just_applied) used; widened to 4
//                                    (2 bits) for headroom per plan §1 table.
//   Enemy.HP:         [0, 256)   — Louse max_hp = 136; cultist ≤ 53.
//   Enemy.Block:      [0, 256)   — Stat::pack8.
//   Enemy.move_index: [0, 6)     — kMaxMovesPerMonster = 6.
//   Enemy.current_move: [0, 5)   — MoveId cardinality (see above).
//   Enemy.alive:      [0, 2)     — bool.
//   Enemy.performed_first_move: [0, 2) — bool.
//   Enemy.dark_strike_base: [0, 32) — cultist values 1 or 9.
//   Enemy.ritual_amount:    [0, 32) — cultist values 2 or 5.
//   Enemy.power_count: [0, kMaxPowersPerCreature+1=7).
//   kMaxEnemies = 4 (state.h; wave-21.β widened 2→4).
//   kMaxPowersPerCreature = 6 (state.h).
//   CardCounts: 4 card_ids × counts.
//     CardCounts.counts.size() = kCountedCardIds.size() = 4
//                                  (wave-22 adds kSlimed → 5).
//     count per (zone × card_id): [0, 16) — Silent starter is 5+5+1+1 = 12
//                                 cards; discard zone bounded by total.
//
// Wave-21.β fill-order contract:
//   * The cultist + LouseProgenitor ZobristKeys captured pre-wave-21
//     (cultist_zobrist_pin.h) MUST hold byte-identical after the kMaxEnemies
//     2→4 widening. To achieve this, generate_table() fills tables in two
//     phases: PHASE 1 reproduces the EXACT pre-wave-21 mt19937 consumption
//     order for slots 0+1 of all enemy_* tables, then fills card_counts,
//     then enemy_count[0..kPreWave21MaxEnemies]. PHASE 2 (APPEND) fills the
//     new enemy_* slot 2+3 rows + the new enemy_count[3..4] entries.
//     Cultist (enemy_count=2) only consumes phase-1 outputs → byte identity
//     holds. See generate_table() body for the literal sequence.
//
// Wave-21.β fold_enemy loop bound audit:
//   * zobrist_half() iterates `for (i = 0; i < s.get_enemy_count(); ++i)`
//     — NOT `kMaxEnemies`. Cultist (enemy_count=2) hashes only slots 0+1
//     even though slot 2+3 storage exists. If this loop ever changes to
//     iterate kMaxEnemies, cultist would XOR the slot-2+3 dead-default
//     contributions and break byte identity.
//
// Wave-21.β decoupling of kMonsterKindCardinality:
//   * Pre-wave-21, kMonsterKindCardinality was sourced from
//     monster_moves::kMonsterKindCount. Wave-21.α extends that constant
//     3 → 7 (adds slime monsters). To preserve cultist byte identity
//     across the α merge, this file PINS kMonsterKindCardinality = 3 as a
//     LITERAL — the table per-slot inner dimension does not grow when α
//     lands. Wave-22 (slime port) widens the table when slime monsters
//     first see runtime use, with the same APPEND-only fill discipline.
//
// Future widening required when:
//   - kMaxEnemies bumps 4 → higher (no current encounter requires this).
//   - kMonsterKindCardinality / kMoveIdCardinality bump (wave-22 slime port).
//   - kCountedCardIds gains kSlimed (wave-22).
//   - kPhaseCardinality 2 → 3 (wave-22 enemy-move RNG).
//   - SlimedBerserker HP > 255 (would force Stat::pack8 → pack16; deferred).
//
// Tables initialized once via Meyers singleton (thread-safe per C++11). Pure
// XOR-fold composition over state features per plan §1. Total static storage
// for both halves: ~2.4 MB post-wave-21.β (+~1.2 MB vs. pre-wave-21; well
// under 10 MB pathology threshold).
// ============================================================================

#include "sts2/ai/zobrist.h"

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
// ---------------------------------------------------------------------------
constexpr std::size_t kMaxHp = 256;
constexpr std::size_t kMaxBlock = 256;
constexpr std::size_t kMaxEnergy = 8;
constexpr std::size_t kMaxRound = 256;
constexpr std::size_t kPhaseCardinality = 2;
// PowerKind enum tail = kVulnerable=5; count = 6. (No kPowerKindCount in
// types.h — see audit block.)
constexpr std::size_t kPowerKindCardinality = 6;
// MoveId enum tail = kPounce=4 in pre-wave-21 types.h. Wave-21.α extends the
// enum (5 → 10 values); kMoveIdCardinality is PINNED at 5 here to preserve
// byte identity of slot 0+1 entries through the α merge. Wave-22 widens.
constexpr std::size_t kMoveIdCardinality = 5;
// MonsterKind cardinality DECOUPLED from monster_moves::kMonsterKindCount
// (wave-21.β) — α extends the enum count 3 → 7 and an auto-derived value
// would re-shape the per-slot table and break cultist byte identity.
// Wave-22 widens with APPEND fill order.
constexpr std::size_t kMonsterKindCardinality = 3;
constexpr std::size_t kMaxStacks = 100;
constexpr std::size_t kMaxFlags = 4;
constexpr std::size_t kMaxMovesPerMonster =
    sts2::game::monster_moves::kMaxMovesPerMonster;
constexpr std::size_t kMaxDarkStrikeBase = 32;
constexpr std::size_t kMaxRitualAmount = 32;
constexpr std::size_t kMaxCountPerCardZone = 16;
constexpr std::size_t kCardIdCardinality =
    sts2::game::card_effects::kCountedCardIds.size();
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
  std::array<std::array<uint64_t, kMaxDarkStrikeBase>, kMaxEnemies> enemy_dsb{};
  std::array<std::array<uint64_t, kMaxRitualAmount>, kMaxEnemies>
      enemy_ritual{};

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
  for (auto& slot : a) {
    slot = rng();
  }
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

// Generate one half-table seeded by the given 64-bit seed. Deterministic and
// platform-independent (std::mt19937_64 is portable).
//
// Wave-21.β fill-order contract (see audit-block above for rationale):
//
//   PHASE 1 (preserve pre-wave-21 mt19937 consumption order):
//     - All non-enemy tables (player_*, round, phase, player_power*).
//     - enemy_* tables for slots [0, kPreWave21MaxEnemies) ONLY.
//     - enemy_count entries [0, kPreWave21MaxEnemies] (3 entries: ec=0,1,2).
//     - card_counts.
//
//   PHASE 2 (APPEND new wave-21.β content):
//     - enemy_* tables for slots [kPreWave21MaxEnemies, kMaxEnemies).
//     - enemy_count entries [kPreWave21MaxEnemies+1, kMaxEnemies].
//
// Cultist (enemy_count=2) and LouseProgenitor (enemy_count=1) only consume
// PHASE-1 outputs at hash time → pre-wave-21 byte identity preserved.
//
// IMPORTANT: any future widening (wave-22 kMoveIdCardinality 5→10, etc.)
// MUST follow the same discipline — fill the PRE-WIDENING bytes first, then
// APPEND the new entries. Otherwise the cultist + LouseProgenitor pins drift.
ZobristTables generate_table(uint64_t seed) noexcept {
  std::mt19937_64 rng(seed);
  ZobristTables t;

  // ---- PHASE 1: pre-wave-21 layout (slots [0, kPreWave21MaxEnemies)) ----
  fill_array(t.player_hp, rng);
  fill_array(t.player_block, rng);
  fill_array(t.player_energy, rng);
  fill_array(t.round, rng);
  fill_array(t.phase, rng);
  fill_array(t.player_power, rng);
  fill_array(t.player_power_count, rng);
  // enemy_* tables — outer slot dimension iterates only [0,
  // kPreWave21MaxEnemies).
  fill_slots(t.enemy_hp, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_block, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_kind, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_move_idx, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_current_move, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_alive, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_pfm, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_dsb, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_ritual, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_power, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_power_count, 0, kPreWave21MaxEnemies, rng);
  // enemy_count[0..kPreWave21MaxEnemies] — 3 entries: ec=0, 1, 2.
  for (std::size_t i = 0; i <= kPreWave21MaxEnemies; ++i) {
    t.enemy_count[i] = rng();
  }
  fill_array(t.card_counts, rng);

  // ---- PHASE 2: APPEND new enemy slots [kPreWave21MaxEnemies, kMaxEnemies)
  // --- Same table-fill order as PHASE 1 for symmetry / reviewability.
  fill_slots(t.enemy_hp, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_block, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_kind, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_move_idx, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_current_move, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_alive, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_pfm, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_dsb, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_ritual, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_power, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_power_count, kPreWave21MaxEnemies, kMaxEnemies, rng);
  // enemy_count[kPreWave21MaxEnemies+1 .. kMaxEnemies] — entries ec=3, 4.
  for (std::size_t i = kPreWave21MaxEnemies + 1;
       i <= static_cast<std::size_t>(kMaxEnemies); ++i) {
    t.enemy_count[i] = rng();
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

uint64_t lookup_power(
    const std::array<std::array<std::array<uint64_t, kMaxFlags>, kMaxStacks>,
                     kPowerKindCardinality>& slot_table,
    sts2::game::PowerKind kind, int16_t stacks, uint8_t flags) noexcept {
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
  h ^= at(t.player_hp, s.get_player_hp().pack8());
  h ^= at(t.player_block, s.get_player_block().pack8());
  h ^= at(t.player_energy, s.get_energy().pack8());
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
  h ^= at(t.enemy_hp[slot], e.get_hp().pack8());
  h ^= at(t.enemy_block[slot], e.get_block().pack8());
  h ^= at(t.enemy_kind[slot], static_cast<uint8_t>(e.get_kind()));
  h ^= at(t.enemy_move_idx[slot], e.get_move_index());
  h ^= at(t.enemy_current_move[slot],
          static_cast<uint8_t>(e.get_current_move()));
  h ^= at(t.enemy_alive[slot], e.get_alive() ? 1U : 0U);
  h ^= at(t.enemy_pfm[slot], e.get_performed_first_move() ? 1U : 0U);
  h ^= at(t.enemy_dsb[slot], e.get_dark_strike_base().pack8());
  h ^= at(t.enemy_ritual[slot], e.get_ritual_amount().pack8());

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
  for (uint8_t i = 0; i < ec; ++i) {
    fold_enemy(h, t, i, s.get_enemy(i));
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

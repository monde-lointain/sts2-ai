// ============================================================================
// Cardinality audit (wave-23/J.beta; revises wave-22/C.2-α; 2026-05-18)
// ============================================================================
// Per plan §1, each Zobrist key slot is sized to its feature's reachable
// range. Out-of-range values trigger assertion+abort in zobrist_of(). Audit
// results vs plan-baked bounds — wave-23/J.beta widened to reflect upstream
// STS2's uniform int (32-bit signed) stat storage. The cultist Zobrist BYTE
// rotates (table sizes grow → mt19937_64 fill order shifts); cultist + Louse
// search-pin VALUES remain BIT-IDENTICAL (search invariant). Q2-ADR-014.
//
//   Player HP:        [0, 1024)  — Stat::pack16 asserts v in [0,65535];
//                                  cultist max ≈ 70 (Silent starter 70 HP);
//                                  SlimedBerserker (Phase-2+) HP 261-281
//                                  already would exceed the pre-wave-23 [0,256)
//                                  bound.
//   Player Block:     [0, 1024)  — Stat::pack16; cultist max ≈ 30.
//   Player Energy:    [0, 8)     — kPlayerStartingEnergy=3 (turn_calc.h);
//                                  no in-combat gain in Phase-1; max = 3.
//   Round:            [0, 256)   — round_ is int32_t in CompactState; cultist
//                                  solves in ≤ 20 rounds. Bound kept at 256
//                                  (per-search horizon).
//   Phase:            [0, 3)     — enum {kPlayerActing=0, kAtChanceDraw=1,
//                                  kAtEnemyMoveRng=2}. Wave-22.α APPENDED
//                                  kAtEnemyMoveRng with APPEND-only mt19937
//                                  fill order (cultist hashes phase=0
//                                  exclusively → byte identity preserved).
//   PowerKind:        [0, 7)     — enum has 7 values post-wave-26/M.β:
//                                  {kWeak, kStrength, kRitual, kCurlUp,
//                                   kFrail, kVulnerable, kSurprise}. No
//                                  kPowerKindCount constant in types.h —
//                                  bound encoded as kPowerKindCardinality
//                                  (types.h; wave-32/C1-β); sync with enum
//                                  when extended.
//                                  Wave-26/M.β APPENDS kSurprise(6);
//                                  cardinality 6 → 7. APPEND-ONLY: new
//                                  PHASE-3 entries fill AFTER [0,6).
//                                  Cultist + Louse + slime + Nibbit BYTE
//                                  PRESERVED.
//   MoveId:           [0, 5)     — table-pinned at pre-wave-21 cardinality
//                                  to preserve cultist Zobrist byte identity
//                                  through the wave-21.α MoveId enum extension
//                                  (5 → 10 enum values added: kPokeyPounce,
//                                  kStickyShot, kSpitBig, kSpitMed,
//                                  kSpitSmall). Wave-22's slime port WIDENS the
//                                  table when the new MoveIds first see runtime
//                                  use; that bump is APPEND-ONLY (mt19937 fill
//                                  order preserved for old kind 0..4).
//                                  Wave-24/K.β APPENDS kButtMove(10),
//                                  kSliceMove(11), kHissMove(12); cardinality
//                                  10 → 13 (kMoveIdCardinality). APPEND-ONLY:
//                                  new Phase-3 entries fill AFTER [0,10).
//                                  Cultist + Louse + slime BYTE PRESERVED.
//                                  Wave-26/M.β APPENDS kGimmeMove(13),
//                                  kDoubleSmashMove(14), kHeheMove(15),
//                                  kSpawnedMove(16), kFleeMove(17);
//                                  cardinality 13 → 18 (kMoveIdCardinality).
//                                  APPEND-ONLY: new PHASE-3 entries fill
//                                  AFTER [0,13). Cultist + Louse + slime +
//                                  Nibbit BYTE PRESERVED.
//   MonsterKind:      [0, 3)     — table-pinned at pre-wave-21 cardinality
//                                  (kCultistCalcified, kCultistDamp,
//                                  kLouseProgenitor). Wave-21.α extends the
//                                  enum to 7 (slime variants); we DECOUPLE
//                                  kMonsterKindCardinality from
//                                  monster_moves::kMonsterKindCount so the
//                                  per-slot table size is stable through the
//                                  α-stream merge (cultist byte identity).
//                                  Wave-22 widens to 7 with APPEND fill order.
//                                  Wave-24/K.β APPENDS kNibbit(7); cardinality
//                                  7 → 8 (kMonsterKindCardinality in types.h;
//                                  wave-32/C1-β).
//                                  APPEND-ONLY: new Phase-3 entry fills AFTER
//                                  [0,7). Cultist + Louse + slime BYTE
//                                  PRESERVED.
//                                  Wave-26/M.β APPENDS kGremlinMerc(8),
//                                  kSneakyGremlin(9), kFatGremlin(10);
//                                  cardinality 8 → 11
//                                  (kMonsterKindCardinality). APPEND-ONLY: new
//                                  PHASE-3 entries fill AFTER [0,8). Cultist +
//                                  Louse + slime + Nibbit BYTE PRESERVED.
//   PowerInstance.stacks: [0, 256) — int32_t backing post-wave-23/J.beta;
//                                    cultist Ritual = 2/5; Strength compounds
//                                    on Louse +5/cycle, observed ≤ 50.
//                                    Bound 256 absorbs larger Phase-2 stack
//                                    growth.
//   PowerInstance.flags:  [0, 4)   — bit 0 (just_applied) used; widened to 4
//                                    (2 bits) for headroom per plan §1 table.
//   Enemy.HP:         [0, 1024)  — Louse max_hp = 136; cultist ≤ 53;
//                                  SlimedBerserker A0 HP 261-281 fits in 1024.
//   Enemy.Block:      [0, 1024)  — Stat::pack16.
//   Enemy.move_index: [0, 6)     — kMaxMovesPerMonster = 6.
//   Enemy.current_move: [0, 5)   — MoveId cardinality (see above).
//   Enemy.alive:      [0, 2)     — bool.
//   Enemy.performed_first_move: [0, 2) — bool.
//   Enemy.dark_strike_base: REMOVED in wave-22-fix-4/H.gamma — dsb is
//                                  constant-per-MonsterKind (cultist normal=1,
//                                  elite=9; all others 0). enemy_kind XOR
//                                  already distinguishes; per-state dsb hash
//                                  contribution was redundant. Q2-ADR-013
//                                  Amendment 4 §Compression.
//   Enemy.ritual_amount:    REMOVED in wave-22-fix-4/H.gamma — same rationale:
//                                  constant-per-MonsterKind (cultist normal=2,
//                                  elite=5; all others 0). enemy_kind XOR
//                                  carries the distinction.
//   Enemy.power_count: [0, kMaxPowersPerCreature+1=5).
//   kMaxEnemies = 4 (state.h; wave-21.β widened 2→4).
//   kMaxPowersPerCreature = 4 (state.h; wave-22-fix-4/H.gamma narrowed 6→4).
//   CardCounts: 5 card_ids × counts (wave-22.α widened 4 → 5).
//     CardCounts.counts.size() = kCountedCardIds.size() = 5
//                                  (wave-22.α APPENDED kSlimed at index 4).
//     count per (zone × card_id): [0, 64) — int32_t backing
//     post-wave-23/J.beta;
//                                 Silent starter is 5+5+1+1 = 12 cards; discard
//                                 zone bounded by total. Cultist +
//                                 LouseProgenitor decks contain 0 Slimed cards.
//                                 For byte-identity preservation,
//                                 card_counts[z][cid=4][count=0] is LEFT AT
//                                 ZERO in generate_table() (NOT consumed from
//                                 mt19937). XOR'ing 0 against the cultist
//                                 running hash is a no-op → bytes preserved.
//                                 States that actually carry kSlimed (count>=1)
//                                 read from PHASE-2-filled slots
//                                 card_counts[z][4][1..63], which use fresh
//                                 mt19937 output for collision resistance.
//                                 Bound 64 absorbs Phase-2 Slimed accumulation
//                                 + post-pack16 wider arithmetic.
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
// Wave-22-fix-4/H.gamma byte rotation (NEW pin):
//   * enemy_dsb + enemy_ritual tables REMOVED (dsb + ritual_amount are
//     constant-per-MonsterKind; enemy_kind XOR already separates cultist
//     normal/elite). Dropping the two `fill_slots` calls REMOVES `2 *
//     kPreWave21MaxEnemies * 32 = 128` mt19937_64 outputs from PHASE 1
//     consumption (and `2 * (kMaxEnemies - kPreWave21MaxEnemies) * 32 = 128`
//     from PHASE 2). Downstream tables (enemy_power, enemy_power_count,
//     enemy_count, card_counts) SHIFT in the mt19937 stream by 128 outputs
//     per phase → cultist + LouseProgenitor hashes ROTATE. Pin file
//     `cultist_zobrist_pin.h` re-stamped post-edit; search semantics
//     invariant to byte rotation (cultist + Louse expectation pins still
//     bit-identical). Q2-ADR-013 Amendment 4 §Cultist-byte-rotation.
//
// Wave-21.β fold_enemy loop bound audit (revised wave-25/L.α):
//   * zobrist_half() iterates `for (i = 0; i < s.get_enemy_count(); ++i)`
//     — NOT `kMaxEnemies`. Cultist (enemy_count=2) hashes only slots 0+1
//     even though slot 2+3 storage exists. If this loop ever changes to
//     iterate kMaxEnemies, cultist would XOR the slot-2+3 dead-default
//     contributions and break byte identity.
//   * Wave-25/L.α: the loop body now reads `s.get_enemy(perm[i])` instead
//     of `s.get_enemy(i)` (canonical-form swap). The outer index `i`
//     (used to look up enemy_*[i][...]) is preserved — only the SOURCE
//     enemy is permuted. The loop BOUND remains `ec`; dead-default
//     contributions of slots 2+3 are still NOT folded.
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
// Wave-23/J.beta byte rotation (NEW pin):
//   * Stat-table widening: kMaxHp 256→1024, kMaxBlock 256→1024,
//     kMaxStacks 100→256, kMaxCountPerCardZone 16→64. Each enlarges the
//     mt19937_64 consumption per slot → cultist + LouseProgenitor hashes
//     ROTATE. Pin file `cultist_zobrist_pin.h` re-stamped post-edit; search
//     semantics invariant within reachable stat ranges (cultist + Louse
//     expectation pins still bit-identical). Q2-ADR-014.
//
//   * APPEND-only discipline is NOT REQUIRED for this widening (the cultist
//     BYTE is re-stamped anyway). Future widenings to support larger Phase-2+
//     stat ranges may also re-stamp; reserve append-only for cases where
//     pin-stability is contractually required upstream.
//
// Wave-24/K.α MoveEffectKind extension (NO byte impact):
//   * MoveEffectKind enum APPENDED kBuffEnemy (6) + kBlockSelf (7) for the
//     Nibbit port (HISS = Strength self-buff; SLICE = block self).
//     MoveEffectKind is a BEHAVIOR TAG that drives dispatch in transition.cc;
//     it is NOT a Zobrist key-table dimension (no `kMoveEffectKind*` table or
//     fold in this file). Adding values does NOT rotate the cultist BYTE; the
//     `0x569115efa81a95dc / 0x9a06f1e505846a80` pin is PRESERVED.
//   * The new kinds are dead-path for cultist + Louse + slimes (no existing
//     monster_moves table emits them); Nibbit emits them after K.β lands.
//
// Wave-25/L.α canonical-form pre-Zobrist swap (Q2-ADR-015 Amendment 1):
//   * zobrist_half() now sorts the active enemy slots [0..ec) by a
//     deterministic LEX-KEY (alive → kind → hp → current_move → block →
//     pfm → move_idx → power_count → powers) BEFORE folding. Per-slot
//     key tables (enemy_hp[slot], enemy_kind[slot], etc.) remain indexed by
//     the OUTER LOOP `i` — that's the canonical-form mechanism: the same
//     enemy ends up at the same "canonical slot" regardless of its wire
//     position. Symmetric reachable states (same-kind enemies in swapped
//     wire slots) collapse to a single TT entry, halving the NibbitsNormal
//     symmetric breadth (state-space cap recovery; L.β re-captures pin).
//   * CompactState slot order is UNCHANGED (only this hash function is
//     canonicalized); `target_idx` action semantics + `derive_best_action`
//     re-derivation per state remain correct (Q2-ADR-015 Amendment 1
//     §Correctness-analysis).
//   * Cultist BYTE outcome depends on Q1's BuildMonster wire order for the
//     2-cultist Normal encounter (Calcified-first → preserved; Damp-first
//     → rotated). The CultistRootKey pin file is the source of truth.
//
// Wave-26/M.β cardinality triple-update (NO byte rotation):
//   * kMonsterKindCardinality 8 → 11 (APPENDS kGremlinMerc, kSneakyGremlin,
//     kFatGremlin at indices 8, 9, 10).
//   * kMoveIdCardinality      13 → 18 (APPENDS kGimmeMove, kDoubleSmashMove,
//     kHeheMove, kSpawnedMove, kFleeMove at indices 13..17).
//   * kPowerKindCardinality    6 → 7  (APPENDS kSurprise at index 6).
//   * APPEND-ONLY discipline: new key-table draws come AFTER ALL existing
//     PHASE-1, PHASE-2, and pre-M.β PHASE-3 draws — appended to the END of
//     the mt19937_64 sequence per the wave-24/K.β precedent (and slime port
//     precedent before it). Cultist (kCultistCalcified=0,
//     MoveId∈{kIncantation=0, kDarkStrike=1}, PowerKind∈{kWeak=0,
//     kStrength=1, kRitual=2}) does NOT index any of the new entries; its
//     XOR contributions touch only PHASE-1 outputs → cultist BYTE
//     `0x569115efa81a95dc / 0x9a06f1e505846a80` PRESERVED.
//   * Cultist + Louse + slime + Nibbit search pin VALUES BIT-IDENTICAL
//     (none of these enemies index the new key-table slots; XOR-contribute
//     unchanged).
//
// Future widening required when:
//   - kMaxEnemies bumps 4 → higher (no current encounter requires this).
//   - kMonsterKindCardinality bumps 11 → higher (Phase-2 monster additions;
//     wave-26/M.β updated 8→11 for kGremlinMerc + kSneakyGremlin +
//     kFatGremlin).
//   - kMoveIdCardinality bumps 18 → higher (new MoveIds; wave-26/M.β
//     updated 13→18 for kGimmeMove + kDoubleSmashMove + kHeheMove +
//     kSpawnedMove + kFleeMove).
//   - kPowerKindCardinality bumps 7 → higher (new PowerKinds; wave-26/M.β
//     updated 6→7 for kSurprise).
//   - kCountedCardIds gains a 6th card id (Phase-2 status cards).
//   - Stat::pack16 saturates at 65535 (no current encounter; SlimedBerserker
//     HP 281 fits comfortably in pack16).
//
// Tables initialized once via Meyers singleton (thread-safe per C++11). Pure
// XOR-fold composition over state features per plan §1. Total static storage
// for both halves: ~6.6 MB post-wave-23/J.beta (+~4.2 MB vs. wave-22-fix-4;
// dominated by 4x larger HP + Block tables and 2.6x larger stacks table).
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
  // kPreWave21MaxEnemies).
  fill_slots(t.enemy_hp, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_block, 0, kPreWave21MaxEnemies, rng);
  // enemy_kind: manual iteration over pre-wave-22 cardinality so wave-22's
  // bump (3→7) doesn't shift mt19937 consumption. New kinds appended in
  // PHASE 3 below.
  for (std::size_t slot = 0; slot < kPreWave21MaxEnemies; ++slot) {
    for (std::size_t k = 0; k < kPreWave22MonsterKindCardinality; ++k) {
      t.enemy_kind[slot][k] = rng();
    }
  }
  fill_slots(t.enemy_move_idx, 0, kPreWave21MaxEnemies, rng);
  // enemy_current_move: manual iteration over pre-wave-22 cardinality so
  // wave-22's bump (5→10) doesn't shift mt19937 consumption. New MoveIds
  // appended in PHASE 3 below.
  for (std::size_t slot = 0; slot < kPreWave21MaxEnemies; ++slot) {
    for (std::size_t m = 0; m < kPreWave22MoveIdCardinality; ++m) {
      t.enemy_current_move[slot][m] = rng();
    }
  }
  fill_slots(t.enemy_alive, 0, kPreWave21MaxEnemies, rng);
  fill_slots(t.enemy_pfm, 0, kPreWave21MaxEnemies, rng);
  // Wave-22-fix-4/H.gamma: enemy_dsb + enemy_ritual fill_slots dropped.
  // enemy_power: manual iteration over PowerKind cardinality = 6.
  // kPowerKindCardinality = 6; full range filled here.
  for (std::size_t slot = 0; slot < kPreWave21MaxEnemies; ++slot) {
    for (std::size_t ps = 0;
         ps < static_cast<std::size_t>(kMaxPowersPerCreature); ++ps) {
      for (std::size_t pk = 0; pk < kPowerKindCardinality; ++pk) {
        for (std::size_t stacks = 0; stacks < kMaxStacks; ++stacks) {
          for (std::size_t flags = 0; flags < kMaxFlags; ++flags) {
            t.enemy_power[slot][ps][pk][stacks][flags] = rng();
          }
        }
      }
    }
  }
  fill_slots(t.enemy_power_count, 0, kPreWave21MaxEnemies, rng);
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
  fill_slots(t.enemy_hp, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_block, kPreWave21MaxEnemies, kMaxEnemies, rng);
  // enemy_kind: manual iteration over pre-wave-22 cardinality (new kinds
  // appended in PHASE 3 below).
  for (std::size_t slot = kPreWave21MaxEnemies;
       slot < static_cast<std::size_t>(kMaxEnemies); ++slot) {
    for (std::size_t k = 0; k < kPreWave22MonsterKindCardinality; ++k) {
      t.enemy_kind[slot][k] = rng();
    }
  }
  fill_slots(t.enemy_move_idx, kPreWave21MaxEnemies, kMaxEnemies, rng);
  // enemy_current_move: manual iteration over pre-wave-22 cardinality.
  for (std::size_t slot = kPreWave21MaxEnemies;
       slot < static_cast<std::size_t>(kMaxEnemies); ++slot) {
    for (std::size_t m = 0; m < kPreWave22MoveIdCardinality; ++m) {
      t.enemy_current_move[slot][m] = rng();
    }
  }
  fill_slots(t.enemy_alive, kPreWave21MaxEnemies, kMaxEnemies, rng);
  fill_slots(t.enemy_pfm, kPreWave21MaxEnemies, kMaxEnemies, rng);
  // Wave-22-fix-4/H.gamma: enemy_dsb + enemy_ritual fill_slots dropped.
  // enemy_power: kPowerKindCardinality = 6; full range filled here.
  for (std::size_t slot = kPreWave21MaxEnemies;
       slot < static_cast<std::size_t>(kMaxEnemies); ++slot) {
    for (std::size_t ps = 0;
         ps < static_cast<std::size_t>(kMaxPowersPerCreature); ++ps) {
      for (std::size_t pk = 0; pk < kPowerKindCardinality; ++pk) {
        for (std::size_t stacks = 0; stacks < kMaxStacks; ++stacks) {
          for (std::size_t flags = 0; flags < kMaxFlags; ++flags) {
            t.enemy_power[slot][ps][pk][stacks][flags] = rng();
          }
        }
      }
    }
  }
  fill_slots(t.enemy_power_count, kPreWave21MaxEnemies, kMaxEnemies, rng);
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

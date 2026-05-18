#pragma once

// Cultist ZobristKey pin — RE-STAMPED post-wave-22-fix-4/H.gamma rotation
// (algorithm_sha rotates with this commit; see Q2-ADR-005 source list).
//
// Wave-22-fix-4/H.gamma byte rotation: dropped enemy_dsb + enemy_ritual
// Zobrist tables (constant-per-MonsterKind; enemy_kind XOR already separates
// cultist normal/elite). Removing the two `fill_slots` calls shifts the
// downstream mt19937_64 consumption by 128 outputs/phase, rotating cultist
// + LouseProgenitor hash bytes. Search semantics are INVARIANT to byte
// rotation — cultist + Louse expectation-pin VALUES remain bit-identical.
// Q2-ADR-013 Amendment 4 §Cultist-byte-rotation.
//
// History:
//   pre-wave-21:  Lo=0xf812af56366b5548 Hi=0x2c51edb8b6bd404e (held through
//                 wave-21.β + wave-22.α via APPEND-only mt19937 fill order)
//   post-fix-4:   Lo=0x471665c4838c298d Hi=0x770eab2147499e6c (NEW)
//
// Used by `Zobrist.CultistRootKey_MatchesPreWave21Pin` (test name preserved
// for grep continuity, but now pins post-fix-4 bytes). Failure = mt19937
// fill order regressed OR fold_enemy loop bound changed → rollback.
//
// Regenerate via `WaveDiagnostic.DISABLED_DumpCultistZobristKey` test in
// `tests/seeds/dump_cultist_zobrist_key.cc` if the Zobrist seed or table
// composition ever changes again (Q2-ADR-010 §Recovery).

#include <cstdint>

namespace sts2::tests::seeds {

inline constexpr uint64_t kCultistZobristKeyLo = 0x471665c4838c298dULL;
inline constexpr uint64_t kCultistZobristKeyHi = 0x770eab2147499e6cULL;

}  // namespace sts2::tests::seeds

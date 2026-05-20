#pragma once

// Cultist ZobristKey pin — RE-STAMPED post-wave-23/J.beta rotation
// (algorithm_sha rotates with this commit; see Q2-ADR-005 source list).
//
// Wave-23/J.beta byte rotation: widened upstream-matching stat tables
// (kMaxHp 256→1024, kMaxBlock 256→1024, kMaxStacks 100→256,
// kMaxCountPerCardZone 16→64) shifts the mt19937_64 consumption order,
// rotating cultist + LouseProgenitor hash bytes. Search semantics are
// INVARIANT to byte rotation within the reachable stat range — cultist +
// Louse expectation-pin VALUES remain bit-identical.
// Q2-ADR-014 (wave-23) + Q2-ADR-010 §Recovery (rotation discipline).
//
// History:
//   pre-wave-21:        Lo=0xf812af56366b5548 Hi=0x2c51edb8b6bd404e (held
//   through
//                       wave-21.β + wave-22.α via APPEND-only mt19937 fill
//                       order)
//   post-fix-4:         Lo=0x471665c4838c298d Hi=0x770eab2147499e6c
//   post-J.beta:        Lo=0x569115efa81a95dc Hi=0x9a06f1e505846a80
//   post-damp-kind-fix: Lo=0x2641e6057b9af53a Hi=0x4faed2f7f9f09086
//     Cause: make_damp_cultist now sets e.kind=kCultistDamp (was defaulting
//     to kCultistCalcified=0); kCultistDamp folds into the Zobrist hash,
//     changing the key. Latent-bug fix; renderer prereq.
//   wave-28/G (gremlin removal): Lo=0x2641e6057b9af53a Hi=0x4faed2f7f9f09086
//   PRESERVED
//     PHASE-3-extension was append-only; cultist state never XOR-folds gremlin
//     slots → removing them does not shift any prior mt19937 position.
//     Q2-ADR-018 §Zobrist-BYTE-outcome (confirmed empirically 2026-05-19).
//   wave-33/A.β (fill_enemy_slot helper): Lo=0xa5d5769283d589b5
//   Hi=0x403677d8cd214204
//     Cause: fill_enemy_slot restructures PHASE-1/PHASE-2 from all-slots-per-
//     table to all-fields-per-slot, changing mt19937 consumption order within
//     each phase. Search semantics invariant (per-state XOR contributions
//     unchanged in reachable stat ranges). Re-stamped via
//     DumpCultistZobristKey.
//
// Used by `Zobrist.CultistRootKey_MatchesPreWave21Pin` (test name preserved
// for grep continuity, but now pins post-J.beta bytes). Failure = mt19937
// fill order regressed OR fold_enemy loop bound changed → rollback.
//
// Regenerate via `WaveDiagnostic.DISABLED_DumpCultistZobristKey` test in
// `tests/seeds/dump_cultist_zobrist_key.cc` if the Zobrist seed or table
// composition ever changes again (Q2-ADR-010 §Recovery).

#include <cstdint>

namespace sts2::tests::seeds {

inline constexpr uint64_t kCultistZobristKeyLo = 0xa5d5769283d589b5ULL;
inline constexpr uint64_t kCultistZobristKeyHi = 0x403677d8cd214204ULL;

}  // namespace sts2::tests::seeds

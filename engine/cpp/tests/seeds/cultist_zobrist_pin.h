#pragma once

// Cultist ZobristKey pin — captured pre-wave-21 dispatch on post-wave-20 main
// (algorithm_sha =
// ff5e1bfe8715b00b8835326b03083acf46a653e36d5090c9c384d9525bdd54ac).
//
// Used by wave-21.β's byte-identity assertion: after the kMaxEnemies 2→4 bump
// + Zobrist table widening, `zobrist_of(canonical_cultist_state)` MUST still
// produce these bytes. Failure = mt19937_64 fill order regressed OR
// fold_enemy loop bound changed → wave-21 rollback.
//
// Regenerate via `WaveDiagnostic.DISABLED_DumpCultistZobristKey` test in
// `tests/seeds/dump_cultist_zobrist_key.cc` if the Zobrist seed or table
// composition ever changes (Q2-ADR-010 §Recovery).

#include <cstdint>

namespace sts2::tests::seeds {

inline constexpr uint64_t kCultistZobristKeyLo = 0xf812af56366b5548ULL;
inline constexpr uint64_t kCultistZobristKeyHi = 0x2c51edb8b6bd404eULL;

}  // namespace sts2::tests::seeds

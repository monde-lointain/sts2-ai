#pragma once

#include <cstddef>
#include <unordered_map>

#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"

namespace sts2::ai {

// Two-tier optimization: max expected_hp; tiebreak: min expected_rounds.
struct Score {
  double expected_hp = 0.0;
  double expected_rounds = 0.0;

  // Lex compare with float-eps tolerance on the HP component.
  [[nodiscard]] bool better_than(Score other) const noexcept;

  static constexpr double kEps = 1e-9;
};

// Returned by Search::solve.
struct SearchResult {
  Score score;                     // expected outcome under optimal play
  transition::Action best_action;  // valid only if !terminal
  bool terminal = false;           // true iff input state is terminal
};

// Hash for CompactState (POD-like, but bool padding makes byte-hashing risky).
// Implementation hashes field-by-field to avoid uninitialized-padding pitfalls.
struct CompactStateHash {
  [[nodiscard]] std::size_t operator()(const CompactState& s) const noexcept;
};

// Provably optimal expectimax search over CompactState.
//
// Internal TT is keyed on the *full* CompactState (round included). round-bit
// affects future behavior (round 1 draws 7 cards via Ring of the Snake bonus,
// 5 thereafter), so excluding round from the key would risk wrong cache hits.
// Optimization opportunity for later: strip round to {0 if round==1, 1 else}.
class Search {
 public:
  [[nodiscard]] SearchResult solve(const CompactState& state);

  // For inspection / PV reconstruction by callers (T7). Returns nullptr if the
  // state hasn't been visited yet.
  [[nodiscard]] const SearchResult* peek(
      const CompactState& state) const noexcept;

  // Diagnostics.
  [[nodiscard]] std::size_t tt_size() const noexcept { return tt_.size(); }

 private:
  SearchResult solve_player(CompactState state);
  SearchResult solve_chance(CompactState state);

  std::unordered_map<CompactState, SearchResult, CompactStateHash> tt_;
};

}  // namespace sts2::ai

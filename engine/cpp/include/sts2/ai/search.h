#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>

#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/ai/zobrist.h"

namespace sts2::ai {

// Two-tier optimization: max expected_hp; tiebreak: min expected_rounds.
struct Score {
  double expected_hp = 0.0;
  double expected_rounds = 0.0;

  // Lex compare with float-eps tolerance on the HP component.
  [[nodiscard]] bool better_than(Score other) const noexcept;

  static constexpr double kEps = 1e-9;
};

// SolveStatus discriminates a converged result (score/action valid) from
// a cap-aborted result (score/action UNSPECIFIED — caller MUST NOT consume).
// Q2-ADR-011 §cap-policy.
enum class SolveStatus : uint8_t {
  kConverged = 0,
  kCapExceeded = 1,
};

// Returned by Search::solve. `best_action` is re-derived at root via 1-ply
// argmax (TT is hash-only, doesn't cache best_action). `terminal` is
// reconstructed from `transition::is_terminal(input_state)`.
struct SearchResult {
  Score score;
  transition::Action best_action;
  bool terminal = false;
  SolveStatus status = SolveStatus::kConverged;
  std::size_t entries_at_cap = 0;
};

// Hard TT cap. constexpr for inlining; ~14 GB at 38 B/entry (Q2-ADR-011).
constexpr std::size_t kMaxTtEntries = 370'000'000;

// Maximum search depth in rounds. States with `state.get_round() >
// kSearchHorizonRounds` return a horizon-truncated Score{expected_hp =
// player_hp.value(), expected_rounds = 0.0} from Search::solve_player +
// Search::solve_chance entry. Prevents non-termination on encounters where the
// player can survive indefinitely (e.g. SmallSlimes all-Defend branch — slime
// damage budget ~9.5/turn vs Silent's 15 block/turn).
//
// Conservative for Phase-1: cultist solves in ~6.5 rounds; LouseProgenitor in
// ~10. Phase-2+ encounters may need raising via Q2-ADR-013 Amendment 3+.
// Q2-ADR-013 Amendment 2 (2026-05-18) ratifies this cap.
constexpr uint16_t kSearchHorizonRounds = 50;

// Provably optimal expectimax search over CompactState.
//
// Transposition table is keyed by 128-bit Zobrist hash (Q2-ADR-010) and
// stores only Score (best_action re-derived via 1-ply argmax; terminal
// reconstructed from state). Per-entry footprint drops from ~56 B
// (std::unordered_map<CompactState, SearchResult>) to ~38 B
// (absl::flat_hash_map<ZobristKey, Score>).
//
// Capacity is reserved ONCE at construction (kMaxTtEntries slots; ~14 GB
// committed). solve() clear()s the TT between invocations — clear() retains
// capacity, so no allocator churn across solves.
//
// PIMPL pattern: the absl::flat_hash_map lives in a .cc-only TtData impl
// so absl headers don't leak into public consumers (absl int128 headers
// fail this project's -Wpedantic -Werror build).
class Search {
 public:
  Search();
  ~Search();
  Search(const Search&) = delete;
  Search& operator=(const Search&) = delete;
  Search(Search&&) noexcept;
  Search& operator=(Search&&) noexcept;

  [[nodiscard]] SearchResult solve(const CompactState& state);

  // Returns the Score cached for `state`, or nullopt if not in TT.
  // Replaces pre-wave `peek()` (best_action/terminal no longer cached —
  // re-derive via derive_best_action() in recommend.cc).
  [[nodiscard]] std::optional<Score> peek_score(
      const CompactState& state) const noexcept;

  // Diagnostics.
  [[nodiscard]] std::size_t tt_size() const noexcept;
  [[nodiscard]] bool cap_hit() const noexcept { return cap_hit_; }

 private:
  struct TtData;  // PIMPL — definition in search.cc.

  Score solve_player(CompactState state);
  Score solve_chance(CompactState state);

  // Returns false (and sets cap_hit_) when at capacity; caller MUST stop
  // recursing (returns Score{} which is the unspecified-on-cap sentinel).
  bool tt_insert(ZobristKey k, Score s);

  std::unique_ptr<TtData> tt_;
  bool cap_hit_ = false;
};

// Re-derive the optimal player action at `state` given that its converged
// expected-value is `state_score`. Walks legal_actions in canonical order
// (transition::legal_actions); for each action computes the action's
// expected value via 1-ply expansion + TT lookup of children; returns
// the first action whose value matches `state_score` within Score::kEps.
//
// Precondition: `state` is a player-decision node (Phase::kPlayerActing)
// and NOT terminal; `search` reflects a converged solve covering this
// state's reachable subtree (all chance children in TT). Violations are
// invariant bugs — function asserts.
//
// Sole source of truth for argmax recovery. Used by Search::solve() (root
// re-derivation) and recommend.cc (PV walk).
[[nodiscard]] transition::Action derive_best_action(const Search& search,
                                                    const CompactState& state,
                                                    Score state_score);

}  // namespace sts2::ai

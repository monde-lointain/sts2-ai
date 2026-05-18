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
//
// Wave-22-fix-4/H.beta: LRU eviction replaces hard-abort cap policy
// (Q2-ADR-013 Amendment 4 §LRU-eviction). kCapExceeded is RETAINED for ABI
// compatibility + future-use, but post-(c) solve() never returns it (LRU
// continues past cap by evicting the least-recently-used entry on insert).
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
  // Post wave-22-fix-4/H.beta: stays 0 (cap-abort path retired in favor of
  // LRU). Engineer-retained for ABI; cross-reference `Search::eviction_count`
  // for runtime LRU-eviction telemetry.
  std::size_t entries_at_cap = 0;
};

// Soft TT cap. constexpr for inlining. Wave-22-fix-4/H.beta:
// reduced 370M → 200M alongside LRU eviction (Q2-ADR-013 Amendment 4
// §LRU-memory-tradeoff). Per-entry footprint rises from ~38 B (Score 16 B +
// ZobristKey 16 B + ~6 B map overhead) to ~70 B with the LRU linkage
// (added std::list<ZobristKey> node ~24 B + 8 B iterator stored in map
// value). 200M × 70 B ≈ 14 GB stays under the 16 GB ceiling; 370M × 70 B =
// ~26 GB would blow it. Beyond cap, tt_insert evicts the LRU front instead
// of aborting.
constexpr std::size_t kMaxTtEntries = 200'000'000;

// Maximum search depth in rounds. States with `state.get_round() >
// kSearchHorizonRounds` return a horizon-truncated Score{expected_hp =
// player_hp.value(), expected_rounds = 0.0} from Search::solve_player +
// Search::solve_chance entry. Prevents non-termination on encounters where the
// player can survive indefinitely (e.g. SmallSlimes all-Defend branch — slime
// damage budget ~9.5/turn vs Silent's 15 block/turn).
//
// Phase-1: cultist solves in ~6.5 rounds + LouseProgenitor in ~10 — both well
// under 25. Reduced from 50 to 25 in wave-22-fix-3 / Q2-ADR-013 Amendment 3
// to bound SmallSlimes state-space breadth (depth halved → ~√(state-space)
// reachable distinct states). Phase-2+ encounters may need raising via further
// amendment.
// Q2-ADR-013 Amendment 3 (2026-05-18) ratifies this reduction.
constexpr uint16_t kSearchHorizonRounds = 25;

// Provably optimal expectimax search over CompactState.
//
// Transposition table is keyed by 128-bit Zobrist hash (Q2-ADR-010) and
// stores Score plus an LRU-list iterator (best_action re-derived via 1-ply
// argmax; terminal reconstructed from state). Per-entry footprint ~70 B with
// the LRU linkage (Score 16 B + ZobristKey 16 B + ~6 B map overhead +
// std::list<ZobristKey> node ~24 B + 8 B iterator).
//
// Wave-22-fix-4/H.beta: deterministic LRU eviction (Q2-ADR-013 Amendment 4
// §LRU-eviction). When tt_insert encounters a full TT it evicts the
// least-recently-used entry (front of `lru`) instead of aborting. solve_*
// TT-hits splice the hit key to the back (MRU) to maintain order. peek_score
// is a read-only diagnostic and does NOT splice (see contract on peek_score).
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
  //
  // CONTRACT (wave-22-fix-4/H.beta): peek_score is a const-correct read-only
  // diagnostic. It does NOT splice the hit key to the MRU end of the LRU
  // list. Splicing on peek would mutate LRU order non-deterministically
  // (depends on diagnostic-call timing — e.g. PV walk iteration); only the
  // recursive solve_player/solve_chance call sites splice. Callers
  // (derive_best_action / recommend.cc PV walk) MUST tolerate post-eviction
  // misses on peek and recover via re-solve.
  [[nodiscard]] std::optional<Score> peek_score(
      const CompactState& state) const noexcept;

  // Re-solve from `state` for child-recovery on an LRU-evicted TT entry.
  // Public so the namespace-scope derive_best_action helper can recover
  // evicted children; safe because solve_player/solve_chance are
  // pure-deterministic and idempotent on a converged TT.
  Score solve_player(CompactState state);
  Score solve_chance(CompactState state);

  // Diagnostics.
  [[nodiscard]] std::size_t tt_size() const noexcept;
  [[nodiscard]] std::size_t eviction_count() const noexcept {
    return eviction_count_;
  }

  // Test-only: override kMaxTtEntries for the lifetime of this Search
  // instance (until the next solve()). Lets unit tests exercise the LRU
  // eviction path without allocating 14 GB. Not part of the production
  // contract; production callers should leave this at kMaxTtEntries.
  void set_tt_cap_for_testing(std::size_t cap) noexcept {
    tt_cap_override_ = cap;
  }

  // Test-only: direct TT-insertion + key-level peek. Lets unit tests drive
  // the LRU eviction path with a known sequence of synthetic keys (no
  // dependency on which CompactStates a real solve would visit). Production
  // callers MUST NOT use these — the keys they insert have no relationship
  // to the search tree and would corrupt subsequent solves.
  void tt_insert_for_testing(ZobristKey k, Score s) { tt_insert(k, s); }
  [[nodiscard]] std::optional<Score> peek_score_by_key_for_testing(
      ZobristKey k) const noexcept;

 private:
  struct TtData;  // PIMPL — definition in search.cc.

  // Post wave-22-fix-4/H.beta: never returns false (LRU evicts on cap).
  // Return type retained as bool for caller-site ergonomics / forward
  // compatibility; callers ignore the return value.
  bool tt_insert(ZobristKey k, Score s);

  std::unique_ptr<TtData> tt_;
  std::size_t eviction_count_ = 0;
  std::size_t tt_cap_override_ = 0;  // 0 == use kMaxTtEntries
};

// Re-derive the optimal player action at `state` given that its converged
// expected-value is `state_score`. Walks legal_actions in canonical order
// (transition::legal_actions); for each action computes the action's
// expected value via 1-ply expansion + TT lookup of children; returns
// the first action whose value matches `state_score` within Score::kEps.
//
// Precondition: `state` is a player-decision node (Phase::kPlayerActing)
// and NOT terminal; `search` reflects a converged solve covering this
// state's reachable subtree. Pre wave-22-fix-4/H.beta the precondition
// "all chance children in TT" held strictly; post-LRU it holds only
// up to evictions — on a TT miss the helper re-solves the child via
// search.solve_player/solve_chance (deterministic + pure → recovers the
// same Score). Hence the parameter is `Search&` (non-const) rather than
// `const Search&`.
//
// Sole source of truth for argmax recovery. Used by Search::solve() (root
// re-derivation) and recommend.cc (PV walk).
[[nodiscard]] transition::Action derive_best_action(Search& search,
                                                    const CompactState& state,
                                                    Score state_score);

}  // namespace sts2::ai

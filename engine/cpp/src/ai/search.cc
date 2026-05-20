#include "sts2/ai/search.h"

// Suppress warnings that absl headers trip under our -Werror build (Q2 owns
// the project-wide warning flags; absl is a vendored dep):
//   -Wpedantic — __int128 type-name use in int128.h
//   -Woverflow — _mm_set1_epi8(0x80) signed/unsigned char overflow in
//                hashtable_control_bytes.h
// Both are well-defined GCC/Clang extensions on Q2's supported platforms
// (Q2-ADR-011 §FP-determinism platform list).
#if defined(__GNUC__) || defined(__clang__)
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wpedantic"
#pragma GCC diagnostic ignored "-Woverflow"
#endif
#include "absl/container/flat_hash_map.h"
#if defined(__GNUC__) || defined(__clang__)
#pragma GCC diagnostic pop
#endif

#include <cassert>
#include <cstddef>
#include <memory>
#include <utility>

#include "sts2/ai/chance.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"
#include "sts2/ai/zobrist.h"

namespace sts2::ai {

// PIMPL impl: wraps absl::flat_hash_map so the absl header never leaks into
// public consumers (which compile under -Wpedantic -Werror).
struct Search::TtData {
  absl::flat_hash_map<ZobristKey, Score, ZobristKeyHash> map;
};

// FP determinism is load-bearing here (Q2-ADR-010 §FP-determinism): solve's
// argmax and recommend.cc's derive_best_action walk the same FP computation
// graph; score equality is bit-exact so re-derivation finds the same winner.
// Build flags audited free of -ffast-math / -ffinite-math-only /
// -fassociative-math / -freciprocal-math / -march=native.

bool Score::better_than(Score other) const noexcept {
  const double hp_diff = expected_hp - other.expected_hp;
  if (hp_diff > kEps) {
    return true;
  }
  if (hp_diff < -kEps) {
    return false;
  }
  // Within HP epsilon: prefer fewer rounds.
  return (other.expected_rounds - expected_rounds) > kEps;
}

Search::Search() : tt_(std::make_unique<TtData>()) {
  // Reserve once for the Search object's lifetime; subsequent solve() calls
  // do clear() which retains capacity (Q2-ADR-011 §lifecycle). Avoids both
  // per-solve allocator churn (~14 GB alloc+free) and the rehash spike that
  // would transiently hold ~24 GB.
  tt_->map.reserve(kMaxTtEntries);
}

Search::~Search() = default;
Search::Search(Search&&) noexcept = default;
Search& Search::operator=(Search&&) noexcept = default;

std::size_t Search::tt_size() const noexcept { return tt_->map.size(); }

bool Search::tt_insert(ZobristKey k, Score s) {
  if (tt_->map.size() >= kMaxTtEntries) [[unlikely]] {
    cap_hit_ = true;
    return false;
  }
  tt_->map.emplace(k, s);
  return true;
}

std::optional<Score> Search::peek_score(
    const CompactState& state) const noexcept {
  const auto it = tt_->map.find(zobrist_of(state));
  if (it == tt_->map.end()) {
    return std::nullopt;
  }
  return it->second;
}

SearchResult Search::solve(const CompactState& state) {
  cap_hit_ = false;
  tt_->map.clear();  // retains capacity (absl::flat_hash_map::clear)

  // Terminal-at-root short-circuit (matches pre-wave behavior).
  if (transition::is_terminal(state)) {
    SearchResult r;
    r.score =
        Score{.expected_hp = static_cast<double>(state.get_player_hp().value()),
              .expected_rounds = 0.0};
    r.terminal = true;
    r.status = SolveStatus::kConverged;
    return r;
  }

  const Score score = (state.get_phase() == Phase::kPlayerActing)
                          ? solve_player(state)
                          : solve_chance(state);

  if (cap_hit_) {
    return SearchResult{
        .score = score,  // UNSPECIFIED — caller MUST gate on status
        .best_action = transition::Action{},
        .terminal = false,
        .status = SolveStatus::kCapExceeded,
        .entries_at_cap = tt_->map.size(),
    };
  }

  // Converged. Re-derive root best_action via 1-ply argmax (TT no longer
  // caches it). For chance-node roots, recommendation is the default
  // kEndTurn (no player choice available; matches pre-wave behavior).
  // Horizon-capped roots have no TT children (solve_player early-returned);
  // skip derive_best_action to avoid asserting under sanitizer builds.
  transition::Action best{};
  if (state.get_phase() == Phase::kPlayerActing &&
      state.get_round() <= kSearchHorizonRounds) {
    best = derive_best_action(*this, state, score);
  }
  return SearchResult{
      .score = score,
      .best_action = best,
      .terminal = false,
      .status = SolveStatus::kConverged,
      .entries_at_cap = 0,
  };
}

Score Search::solve_player(const CompactState& state) {
  if (cap_hit_) [[unlikely]] {
    return Score{};
  }

  const ZobristKey key = zobrist_of(state);
  if (const auto it = tt_->map.find(key); it != tt_->map.end()) {
    return it->second;
  }

  // Wave-22-fix-2 / Q2-ADR-013 Amendment 2: horizon-truncated Score for
  // non-terminating defensive-play branches (e.g. SmallSlimes all-Defend).
  // Not inserted into TT — horizon scores are state-specific by player_hp.
  if (state.get_round() > kSearchHorizonRounds) [[unlikely]] {
    return Score{
        .expected_hp = static_cast<double>(state.get_player_hp().value()),
        .expected_rounds = 0.0,
    };
  }

  const auto actions = transition::legal_actions(state);
  assert(!actions.empty());

  Score best{};
  bool have_best = false;

  for (const auto& action : actions) {
    const auto next_state = transition::apply_player_action(state, action);
    assert(next_state.has_value() &&
           "legal_actions returned an inapplicable action");
    const CompactState& next = *next_state;

    Score child;
    if (action.kind == transition::ActionKind::kEndTurn) {
      child = solve_chance(next);
    } else if (transition::is_terminal(next)) {
      child = Score{
          .expected_hp = static_cast<double>(next.get_player_hp().value()),
          .expected_rounds = 0.0};
    } else {
      child = solve_player(next);
    }
    if (cap_hit_) [[unlikely]] {
      return Score{};
    }

    if (!have_best || child.better_than(best)) {
      best = child;
      have_best = true;
    }
  }

  tt_insert(key, best);  // may set cap_hit_; recursion unwinds via the check
  return best;
}

Score Search::solve_chance(CompactState state) {
  if (cap_hit_) [[unlikely]] {
    return Score{};
  }

  const ZobristKey key = zobrist_of(state);
  if (const auto it = tt_->map.find(key); it != tt_->map.end()) {
    return it->second;
  }

  // Wave-22-fix-2 / Q2-ADR-013 Amendment 2: horizon cap.
  if (state.get_round() > kSearchHorizonRounds) [[unlikely]] {
    return Score{
        .expected_hp = static_cast<double>(state.get_player_hp().value()),
        .expected_rounds = 0.0,
    };
  }

  state = transition::resolve_end_turn_pre_draw(state);

  if (transition::is_terminal(state)) {
    const Score r =
        Score{.expected_hp = static_cast<double>(state.get_player_hp().value()),
              .expected_rounds = 1.0};
    tt_insert(key, r);
    return r;
  }

  // FP-order critical: enumerate_chance_outcomes returns outcomes in the
  // same canonical order pre-wave solve_chance inlined (probability::
  // enumerate_draws preserves iteration order). Weighted sum must walk
  // outcomes in this order; reordering would drift the last few ULPs and
  // break the cultist pin.
  const auto outcomes = enumerate_chance_outcomes(state);
  double exp_hp = 0.0;
  double exp_rounds = 0.0;
  for (const auto& o : outcomes) {
    const Score child = solve_player(o.child_state);
    if (cap_hit_) [[unlikely]] {
      return Score{};
    }
    exp_hp += o.probability * child.expected_hp;
    exp_rounds += o.probability * child.expected_rounds;
  }
  exp_rounds += 1.0;

  const Score r = Score{.expected_hp = exp_hp, .expected_rounds = exp_rounds};
  tt_insert(key, r);
  return r;
}

namespace {

// Compute the expected-value Score for a single action played at `state`,
// reading children from the converged TT. Mirrors solve_player's per-action
// child evaluation EXACTLY so derive_best_action's argmax matches solve's.
Score action_value(const Search& search, const CompactState& state,
                   const transition::Action& action) {
  const auto next_state = transition::apply_player_action(state, action);
  assert(next_state.has_value() &&
         "legal_actions returned an inapplicable action");
  const CompactState& next = *next_state;

  if (action.kind == transition::ActionKind::kEndTurn) {
    // EndTurn child is a chance node; solve_chance cached its Score under
    // the pre-resolve_end_turn_pre_draw state (the chance node itself).
    const auto cached = search.peek_score(next);
    assert(cached.has_value() &&
           "derive_best_action: EndTurn child missing from TT — converged "
           "solve invariant violated");
    return *cached;
  }
  if (transition::is_terminal(next)) {
    // Terminal play-card child: score computed inline by solve_player
    // (not TT-cached). Recompute the same way.
    return Score{
        .expected_hp = static_cast<double>(next.get_player_hp().value()),
        .expected_rounds = 0.0};
  }
  // Non-terminal play-card child is a player node; solve_player cached it.
  const auto cached = search.peek_score(next);
  assert(cached.has_value() &&
         "derive_best_action: play-card child missing from TT — converged "
         "solve invariant violated");
  return *cached;
}

}  // namespace

transition::Action derive_best_action(const Search& search,
                                      const CompactState& state,
                                      Score state_score) {
  assert(state.get_phase() == Phase::kPlayerActing &&
         "derive_best_action called on non-player-decision state");
  assert(!transition::is_terminal(state) &&
         "derive_best_action called on terminal state");

  const auto actions = transition::legal_actions(state);
  assert(!actions.empty());

  // Walk canonical-order actions; pick the FIRST whose value matches
  // state_score. Score equality via mutual !better_than (kEps tolerance);
  // matches solve_player's argmax tie-break (first-better-than wins,
  // equivalent siblings keep the earlier one).
  for (const auto& action : actions) {
    const Score v = action_value(search, state, action);
    if (!v.better_than(state_score) && !state_score.better_than(v)) {
      return action;
    }
  }

  // Unreachable on a converged solve: state_score IS the value of one of
  // these actions. If we got here, either solve diverged from
  // re-derivation (algorithm bug) or FP determinism was violated.
  assert(false && "derive_best_action: no matching action — invariant bug");
  return transition::Action{};
}

}  // namespace sts2::ai

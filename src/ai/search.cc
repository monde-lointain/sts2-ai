#include "sts2/ai/search.h"

#include <cassert>
#include <cstddef>
#include <cstdint>
#include <functional>

#include "sts2/ai/probability.h"
#include "sts2/ai/state.h"
#include "sts2/ai/transition.h"

namespace sts2::ai {

namespace {

constexpr std::size_t hash_combine(std::size_t a, std::size_t b) noexcept {
  return a ^ (b + 0x9E3779B97F4A7C15ULL + (a << 6) + (a >> 2));
}

// Pack small integral fields into a single uint64 then hash that, so we never
// bit-cast the struct (its bool/enum padding would feed uninitialized bytes
// into a byte-wise hasher).
std::size_t hash_u64(uint64_t v) noexcept { return std::hash<uint64_t>{}(v); }

uint64_t pack_player(const CompactState& s) noexcept {
  uint64_t v = 0;
  v |= static_cast<uint64_t>(s.player_hp.pack8());
  v |= static_cast<uint64_t>(s.player_block.pack8()) << 8;
  v |= static_cast<uint64_t>(s.player_strength.pack8()) << 16;
  v |= static_cast<uint64_t>(s.player_weak.pack8()) << 24;
  v |= static_cast<uint64_t>(s.energy.pack8()) << 32;
  v |= static_cast<uint64_t>(s.round) << 40;
  v |= static_cast<uint64_t>(static_cast<uint8_t>(s.phase)) << 56;
  return v;
}

uint64_t pack_enemy(const EnemyState& e) noexcept {
  uint64_t v = 0;
  v |= static_cast<uint64_t>(e.hp.pack8());
  v |= static_cast<uint64_t>(e.block.pack8()) << 8;
  v |= static_cast<uint64_t>(e.strength.pack8()) << 16;
  v |= static_cast<uint64_t>(e.weak.pack8()) << 24;
  v |= static_cast<uint64_t>(e.dark_strike_base.pack8()) << 32;
  v |= static_cast<uint64_t>(e.ritual_amount.pack8()) << 40;
  v |= static_cast<uint64_t>(e.just_applied_ritual ? 1u : 0u) << 48;
  v |= static_cast<uint64_t>(e.performed_first_move ? 1u : 0u) << 49;
  v |= static_cast<uint64_t>(e.alive ? 1u : 0u) << 50;
  v |= static_cast<uint64_t>(static_cast<uint8_t>(e.current_move)) << 56;
  return v;
}

uint64_t pack_counts(const CardCounts& c) noexcept {
  uint64_t v = 0;
  for (std::size_t i = 0; i < c.counts.size(); ++i) {
    v |= static_cast<uint64_t>(c.counts[i]) << (8 * i);
  }
  return v;
}

}  // namespace

bool Score::better_than(Score other) const noexcept {
  const double hp_diff = expected_hp - other.expected_hp;
  if (hp_diff > kEps) return true;
  if (hp_diff < -kEps) return false;
  // Within HP epsilon: prefer fewer rounds.
  return (other.expected_rounds - expected_rounds) > kEps;
}

std::size_t CompactStateHash::operator()(const CompactState& s) const noexcept {
  std::size_t h = hash_u64(pack_player(s));
  h = hash_combine(h, hash_u64(pack_enemy(s.enemies[0])));
  h = hash_combine(h, hash_u64(pack_enemy(s.enemies[1])));
  h = hash_combine(h, hash_u64(pack_counts(s.hand)));
  h = hash_combine(h, hash_u64(pack_counts(s.draw)));
  h = hash_combine(h, hash_u64(pack_counts(s.discard)));
  return h;
}

SearchResult Search::solve(const CompactState& state) {
  // No depth/time limit: search relies on the state machine terminating.
  // Production enemies' positive dark_strike_base + Ritual ramp force
  // termination within finite rounds; default-zero-damage enemies would recurse
  // forever (test fixtures use non-zero values).
  if (transition::is_terminal(state)) {
    SearchResult r;
    r.score = Score{static_cast<double>(state.player_hp.value()), 0.0};
    r.terminal = true;
    return r;
  }
  if (state.phase == Phase::kPlayerActing) {
    return solve_player(state);
  }
  return solve_chance(state);
}

const SearchResult* Search::peek(const CompactState& state) const noexcept {
  const auto it = tt_.find(state);
  if (it == tt_.end()) return nullptr;
  return &it->second;
}

SearchResult Search::solve_player(CompactState state) {
  if (const auto it = tt_.find(state); it != tt_.end()) {
    return it->second;
  }

  const auto actions = transition::legal_actions(state);
  assert(!actions.empty());

  SearchResult best;
  bool have_best = false;

  for (const auto& action : actions) {
    CompactState next = state;
    const bool ok = transition::apply_player_action(next, action);
    assert(ok && "legal_actions returned an inapplicable action");
    (void)ok;

    SearchResult child;
    if (action.kind == transition::ActionKind::kEndTurn) {
      child = solve_chance(next);
    } else if (transition::is_terminal(next)) {
      child.score = Score{static_cast<double>(next.player_hp.value()), 0.0};
      child.terminal = true;
    } else {
      child = solve_player(next);
    }

    if (!have_best || child.score.better_than(best.score)) {
      best.score = child.score;
      best.best_action = action;
      best.terminal = false;
      have_best = true;
    }
  }

  const auto [it, _] = tt_.emplace(state, best);
  return it->second;
}

SearchResult Search::solve_chance(CompactState state) {
  if (const auto it = tt_.find(state); it != tt_.end()) {
    return it->second;
  }

  const CompactState key = state;

  transition::resolve_end_turn_pre_draw(state);

  if (transition::is_terminal(state)) {
    SearchResult r;
    r.score = Score{static_cast<double>(state.player_hp.value()), 1.0};
    r.terminal = false;
    const auto [it, _] = tt_.emplace(key, r);
    return it->second;
  }

  const int k = transition::draw_count(state);

  double exp_hp = 0.0;
  double exp_rounds = 0.0;

  const int draw_total = state.draw.total();
  const int discard_total = state.discard.total();
  if (draw_total >= k) {
    const auto outcomes = probability::enumerate_draws(state.draw, k);
    for (const auto& o : outcomes) {
      CompactState next = state;
      transition::apply_draw(next, o.hand);
      const SearchResult child = solve_player(next);
      exp_hp += o.weight * child.score.expected_hp;
      exp_rounds += o.weight * child.score.expected_rounds;
    }
  } else if (draw_total + discard_total <= k) {
    // Engine semantics (Combat::draw): when both piles run dry, draw stops
    // early. Player deterministically gets every remaining card.
    CompactState next = state;
    const CardCounts everything = state.draw + state.discard;
    transition::apply_draw(next, everything);
    const SearchResult child = solve_player(next);
    exp_hp = child.score.expected_hp;
    exp_rounds = child.score.expected_rounds;
  } else {
    // Draw pile alone can't satisfy k but draw+discard can: take all of draw
    // deterministically and mix in remainder from discard via reshuffle.
    const CardCounts forced_from_draw = state.draw;
    const int remaining = k - draw_total;
    const auto outcomes =
        probability::enumerate_draws(state.discard, remaining);
    for (const auto& o : outcomes) {
      const CardCounts full_drawn = forced_from_draw + o.hand;
      CompactState next = state;
      transition::apply_draw(next, full_drawn);
      const SearchResult child = solve_player(next);
      exp_hp += o.weight * child.score.expected_hp;
      exp_rounds += o.weight * child.score.expected_rounds;
    }
  }

  exp_rounds += 1.0;

  SearchResult r;
  r.score = Score{exp_hp, exp_rounds};
  r.best_action = transition::Action{};  // EndTurn (default kind)
  r.terminal = false;
  const auto [it, _] = tt_.emplace(key, r);
  return it->second;
}

}  // namespace sts2::ai

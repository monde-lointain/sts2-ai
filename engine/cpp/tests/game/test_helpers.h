// tests/game/test_helpers.h
#pragma once

#include <gtest/gtest.h>

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <ostream>
#include <utility>
#include <vector>

#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/enemy.h"
#include "sts2/game/index_types.h"
#include "sts2/game/power.h"
#include "sts2/game/rng.h"
#include "sts2/game/types.h"
#include "sts2/game/vitals.h"

// gtest customization point: print PowerKind by name in failure messages.
// Test-only; kept out of production headers. `PrintTo` must live in the same
// namespace as the type for gtest's ADL lookup; `PowerKind` is in
// sts2::game (per sts2/game/types.h), so this overload sits there too.
namespace sts2::game {
// NOLINTNEXTLINE(readability-identifier-naming) — gtest ADL hook name is fixed.
inline void PrintTo(PowerKind k, std::ostream* os) {
  switch (k) {
    case PowerKind::kWeak:
      *os << "Weak";
      break;
    case PowerKind::kStrength:
      *os << "Strength";
      break;
    case PowerKind::kRitual:
      *os << "Ritual";
      break;
    case PowerKind::kCurlUp:
      *os << "CurlUp";
      break;
    case PowerKind::kFrail:
      *os << "Frail";
      break;
    case PowerKind::kVulnerable:
      *os << "Vulnerable";
      break;
  }
}
}  // namespace sts2::game

namespace sts2::tests::helpers {

template <typename T, std::size_t N>
void expect_shuffle_matches_pinned(const std::vector<T>& shuffled,
                                   const std::array<T, N>& pinned,
                                   const std::vector<T>& original) {
  ASSERT_EQ(shuffled.size(), pinned.size());
  for (std::size_t i = 0; i < shuffled.size(); ++i) {
    EXPECT_EQ(shuffled[i], pinned[i]) << "mismatch at index " << i;
  }
  EXPECT_TRUE(std::is_permutation(shuffled.begin(), shuffled.end(),
                                  original.begin(), original.end()));
}

// Compact constructor for Power test data; `just_applied` defaults to false
// because that matches every spec input except T-PWR-105.
constexpr sts2::game::Power make_power(sts2::game::PowerKind kind, int amount,
                                       bool just_applied = false) {
  return sts2::game::Power{
      .kind = kind, .amount = amount, .just_applied = just_applied};
}

// Element-wise comparison of two Power vectors with diagnostic indexing.
// Implemented as a function (not a macro) so failures land on this line and
// the debugger can step into it. SCOPED_TRACE attaches call-site context to
// failures so the gtest report includes vector sizes alongside the helper line.
inline void expect_powers_eq(const std::vector<sts2::game::Power>& actual,
                             const std::vector<sts2::game::Power>& expected) {
  SCOPED_TRACE(::testing::Message()
               << "expect_powers_eq actual.size()=" << actual.size()
               << " expected.size()=" << expected.size());
  ASSERT_EQ(actual.size(), expected.size()) << "powers vector size mismatch";
  for (std::size_t i = 0; i < expected.size(); ++i) {
    EXPECT_EQ(actual[i].kind, expected[i].kind)
        << "kind mismatch at index " << i;
    EXPECT_EQ(actual[i].amount, expected[i].amount)
        << "amount mismatch at index " << i;
    EXPECT_EQ(actual[i].just_applied, expected[i].just_applied)
        << "just_applied mismatch at index " << i;
  }
}

// Build a Combat with one enemy at given hp (and full block=0, no powers, no
// name). Used by card tests and (later) Combat tests that need a single
// damageable target.
inline sts2::game::Combat make_combat_with_enemy(uint64_t seed, int hp = 40) {
  sts2::game::Combat c{seed};
  sts2::game::Enemy e{};
  e.vitals = sts2::game::Vitals{
      sts2::game::Stat{hp}, sts2::game::Stat{hp}, sts2::game::Stat{0}, {}};
  c.add_enemy(std::move(e));
  return c;
}

// Standard "starter" combat: two cultists rolled with a separate Rng (matching
// main.cc's pattern), pick-discard callback returns HandIndex{0}, started with
// the full 12-card silent starter deck shuffled by Combat's seeded Rng.
// Returns post-start state: round 1, hand size 7, energy 3, both enemies alive.
inline sts2::game::Combat make_starter_combat(uint64_t seed) {
  sts2::game::Combat c{seed};
  sts2::game::Rng enemy_rng{seed};
  c.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));
  c.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));
  c.set_pick_discard_callback(
      [](const sts2::game::Combat&) { return sts2::game::HandIndex{0}; });
  c.start(sts2::cards::make_silent_starter_deck());
  return c;
}

// Kill an enemy through the public API by dealing massive damage.
// Used in tests that need a "one dead enemy in middle/edge" precondition.
inline void kill_enemy(sts2::game::Combat& c, int idx) {
  c.deal_damage_to_enemy(sts2::game::EnemySlot{idx}, 99999);
}

// Drain player_.energy to 0 by playing hand[0] up to 10 times.
// Setup helper for tests of can_play / play_card under the "no energy" branch.
// Does NOT assert: callers verify drained state themselves. If hand[0] targets
// AnyEnemy, the first alive enemy is selected; otherwise EnemySlot::none().
inline void drain_player_energy(sts2::game::Combat& c) {
  int safety = 10;
  while (c.player().energy > 0 && !c.player().hand.empty() && safety-- > 0) {
    const auto& card = c.player().hand.at(sts2::game::HandIndex{0});
    sts2::game::EnemySlot target = sts2::game::EnemySlot::none();
    if (card.target == sts2::game::TargetType::kAnyEnemy) {
      for (std::size_t i = 0; i < c.enemies().size(); ++i) {
        if (c.enemies()[i].vitals.hp > sts2::game::Stat{0}) {
          target = sts2::game::EnemySlot{static_cast<int>(i)};
          break;
        }
      }
    }
    c.play_card(sts2::game::HandIndex{0}, target);
  }
}

}  // namespace sts2::tests::helpers

#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

#include "sts2/game/card.h"
#include "sts2/game/combat.h"
#include "sts2/game/types.h"

namespace sts2::app {

struct ScenarioPlayer {
  std::optional<int> hp;      // defaults to max_hp
  std::optional<int> max_hp;  // defaults to 70 (Silent baseline)
  std::optional<std::vector<sts2::game::CardId>>
      deck;  // defaults to silent starter
};

struct Scenario {
  int schema_version = 1;  // future-compat marker; loader enforces == 1
  std::optional<std::uint64_t> seed;  // overridden by --seed if both present
  std::string encounter;              // verbatim Godot encounter name
  ScenarioPlayer player;              // all fields optional
};

// Combat with enemies + player vitals applied but NOT started, plus the
// prepared deck. Caller must register discard callback then call
// `combat.start(std::move(deck))`. This preserves main.cc's legacy ordering
// (construct → add_enemy → set_callback → start).
struct BuiltCombat {
  sts2::game::Combat combat;
  std::vector<sts2::game::Card> deck;
};

// Throws std::runtime_error on file-not-found, JSON parse errors, schema
// errors, unknown encounter, unknown card-id, invalid value (negative HP, hp >
// max_hp, unsupported schema_version, …).
Scenario load_scenario(std::string_view path);

// Builds the Combat from a Scenario, applying enemies + player overrides,
// but does NOT call combat.start() — caller is responsible for setting any
// callbacks and starting. Seed resolution order:
//   seed_override → scenario.seed → sts2::app::random_seed()
BuiltCombat build_combat(const Scenario& s,
                         std::optional<std::uint64_t> seed_override);

}  // namespace sts2::app

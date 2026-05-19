#include "sts2/app/scenario.h"

#include <fstream>
#include <nlohmann/json.hpp>
#include <sstream>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <utility>

#include "sts2/app/args.h"  // for sts2::app::random_seed()
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/encounter_registry.h"
#include "sts2/game/rng.h"

namespace sts2::app {

namespace {

using json = nlohmann::json;

constexpr int kDefaultMaxHp = 70;
constexpr int kCurrentSchemaVersion = 1;

const std::unordered_map<std::string, sts2::game::CardId>& card_id_map() {
  static const std::unordered_map<std::string, sts2::game::CardId> m = {
      {"Strike", sts2::game::CardId::kStrike},
      {"Defend", sts2::game::CardId::kDefend},
      {"Neutralize", sts2::game::CardId::kNeutralize},
      {"Survivor", sts2::game::CardId::kSurvivor},
      {"Slimed", sts2::game::CardId::kSlimed},
  };
  return m;
}

sts2::game::CardId parse_card_id(const std::string& s) {
  const auto& m = card_id_map();
  auto it = m.find(s);
  if (it == m.end()) {
    std::ostringstream os;
    os << "scenario: unknown card id '" << s << "' (supported: ";
    bool first = true;
    for (const auto& kv : m) {
      if (!first) os << ", ";
      first = false;
      os << kv.first;
    }
    os << ")";
    throw std::runtime_error(os.str());
  }
  return it->second;
}

// Allowed top-level keys for strict validation.
bool is_known_top_key(std::string_view k) {
  return k == "schema_version" || k == "seed" || k == "encounter" ||
         k == "player";
}
bool is_known_player_key(std::string_view k) {
  return k == "hp" || k == "max_hp" || k == "deck";
}

}  // namespace

Scenario load_scenario(std::string_view path) {
  std::ifstream in{std::string(path)};
  if (!in) {
    throw std::runtime_error("scenario: cannot open file '" +
                             std::string(path) + "'");
  }
  json j;
  try {
    in >> j;
  } catch (const json::exception& e) {
    throw std::runtime_error(std::string("scenario: JSON parse error: ") +
                             e.what());
  }
  if (!j.is_object()) {
    throw std::runtime_error("scenario: top-level must be an object");
  }
  for (auto it = j.begin(); it != j.end(); ++it) {
    if (!is_known_top_key(it.key())) {
      throw std::runtime_error("scenario: unknown top-level key '" + it.key() +
                               "'");
    }
  }

  Scenario s;
  // schema_version (optional; defaults to current).
  if (j.contains("schema_version")) {
    if (!j.at("schema_version").is_number_integer()) {
      throw std::runtime_error("scenario: 'schema_version' must be an integer");
    }
    const int v = j.at("schema_version").get<int>();
    if (v != kCurrentSchemaVersion) {
      throw std::runtime_error("scenario: unsupported schema_version " +
                               std::to_string(v) + " (this build supports v" +
                               std::to_string(kCurrentSchemaVersion) + ")");
    }
    s.schema_version = v;
  } else {
    s.schema_version = kCurrentSchemaVersion;
  }
  // seed.
  if (j.contains("seed")) {
    if (!j.at("seed").is_number_unsigned()) {
      throw std::runtime_error(
          "scenario: 'seed' must be a non-negative integer");
    }
    s.seed = j.at("seed").get<std::uint64_t>();
  }
  // encounter (must be registered AND have a non-null spawn).
  if (!j.contains("encounter")) {
    throw std::runtime_error("scenario: 'encounter' is required");
  }
  if (!j.at("encounter").is_string()) {
    throw std::runtime_error("scenario: 'encounter' must be a string");
  }
  s.encounter = j.at("encounter").get<std::string>();
  const auto* spec = sts2::game::encounter_registry::find_by_id(s.encounter);
  if (spec == nullptr) {
    std::ostringstream os;
    os << "scenario: unknown encounter '" << s.encounter << "' (supported: ";
    bool first = true;
    for (const auto& e : sts2::game::encounter_registry::all()) {
      if (e.spawn == nullptr) continue;  // skip adapter-only entries
      if (!first) os << ", ";
      first = false;
      os << e.encounter_id;
    }
    os << ")";
    throw std::runtime_error(os.str());
  }
  if (spec->spawn == nullptr) {
    throw std::runtime_error(
        "scenario: encounter '" + s.encounter +
        "' is adapter-detection-only (cannot be constructed from name "
        "alone — requires a state-blob fixture)");
  }

  // player (all fields optional).
  if (j.contains("player")) {
    const json& p = j.at("player");
    if (!p.is_object()) {
      throw std::runtime_error("scenario: 'player' must be an object");
    }
    for (auto it = p.begin(); it != p.end(); ++it) {
      if (!is_known_player_key(it.key())) {
        throw std::runtime_error("scenario: unknown player key '" + it.key() +
                                 "'");
      }
    }
    if (p.contains("hp")) s.player.hp = p.at("hp").get<int>();
    if (p.contains("max_hp")) s.player.max_hp = p.at("max_hp").get<int>();
    if (p.contains("deck")) {
      if (!p.at("deck").is_array()) {
        throw std::runtime_error("scenario: 'player.deck' must be an array");
      }
      std::vector<sts2::game::CardId> deck;
      for (const auto& el : p.at("deck")) {
        if (!el.is_string()) {
          throw std::runtime_error(
              "scenario: 'player.deck' entries must be strings");
        }
        deck.push_back(parse_card_id(el.get<std::string>()));
      }
      s.player.deck = std::move(deck);
    }
    // Validate hp ≤ max_hp + positivity.
    const int max_hp = s.player.max_hp.value_or(kDefaultMaxHp);
    const int hp = s.player.hp.value_or(max_hp);
    if (max_hp <= 0 || hp <= 0) {
      throw std::runtime_error("scenario: player hp/max_hp must be positive");
    }
    if (hp > max_hp) {
      throw std::runtime_error("scenario: player.hp exceeds player.max_hp");
    }
  }
  return s;
}

namespace {

void spawn_for_encounter(sts2::game::Combat& c, const std::string& name,
                         sts2::game::Rng& rng) {
  // load_scenario has already validated that `name` is registered AND
  // spawn != nullptr. Re-lookup is cheap (linear over ~6 entries).
  const auto* spec = sts2::game::encounter_registry::find_by_id(name);
  if (spec == nullptr || spec->spawn == nullptr) {
    // Defense in depth: should be unreachable given load_scenario validation.
    throw std::runtime_error(
        "scenario: encounter '" + name +
        "' not buildable (internal: validation should have caught this)");
  }
  spec->spawn(c, rng);
}

}  // namespace

BuiltCombat build_combat(const Scenario& s,
                         std::optional<std::uint64_t> seed_override) {
  // Seed resolution: --seed flag → scenario.seed → random_seed() fallback.
  // Matches main.cc's legacy behavior when no --seed flag is given.
  const std::uint64_t seed =
      seed_override.value_or(s.seed.value_or(sts2::app::random_seed()));

  BuiltCombat out{sts2::game::Combat{seed}, {}};

  // Player vitals (staged; caller is responsible for start()).
  const int max_hp = s.player.max_hp.value_or(kDefaultMaxHp);
  const int hp = s.player.hp.value_or(max_hp);
  out.combat.set_player_vitals(sts2::game::Vitals{
      sts2::game::Stat{hp}, sts2::game::Stat{max_hp}, sts2::game::Stat{0}, {}});

  // Enemies.
  sts2::game::Rng enemy_rng{seed};
  spawn_for_encounter(out.combat, s.encounter, enemy_rng);

  // Deck — staged for the caller to pass into combat.start().
  if (s.player.deck.has_value()) {
    out.deck.reserve(s.player.deck->size());
    for (sts2::game::CardId id : *s.player.deck) {
      out.deck.push_back(sts2::cards::make_card(id));
    }
  } else {
    out.deck = sts2::cards::make_silent_starter_deck();
  }
  // Intentionally NO out.combat.start(...) here. Caller must:
  //   1. Register any discard callbacks via
  //   combat.set_pick_discard_callback(...)
  //   2. Then call combat.start(std::move(deck))
  return out;
}

}  // namespace sts2::app

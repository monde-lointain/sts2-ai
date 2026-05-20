#include <cstdint>
#include <iostream>
#include <optional>
#include <string>
#include <utility>
#include <vector>

#include "sts2/ai/recommend.h"
#include "sts2/app/args.h"
#include "sts2/app/prompts.h"
#include "sts2/app/scenario.h"
#include "sts2/game/card.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/index_types.h"
#include "sts2/game/rng.h"
#include "sts2/input/input.h"
#include "sts2/render/ai_recommendation.h"
#include "sts2/render/ansi.h"
#include "sts2/render/console.h"
#include "sts2/render/render.h"

int main(int argc, char** argv) {
  uint64_t seed = 0;
  bool seed_provided = false;
  std::optional<std::string> scenario_path;
  if (!sts2::app::parse_args(argc, argv, seed, seed_provided, scenario_path,
                             std::cerr)) {
    return 1;
  }
  if (!seed_provided) {
    seed = sts2::app::random_seed();
  }

  sts2::console::enable_ansi_and_utf8();

  // Build the Combat + deck. Two paths converge on a (Combat, deck) pair so
  // the same callback-then-start sequence runs unconditionally below.
  sts2::game::Combat combat{seed};
  std::vector<sts2::game::Card> deck;

  if (scenario_path.has_value()) {
    try {
      sts2::app::Scenario s = sts2::app::load_scenario(*scenario_path);
      sts2::app::BuiltCombat bc = sts2::app::build_combat(
          s, seed_provided ? std::optional<std::uint64_t>{seed} : std::nullopt);
      combat = std::move(bc.combat);
      deck = std::move(bc.deck);
    } catch (const std::exception& e) {
      std::cerr << "scenario load failed: " << e.what() << "\n";
      return 1;
    }
  } else {
    // Legacy path: Calcified + Damp cultists, silent starter deck.
    // Intentional: enemy rolls use a separate Rng to keep Combat::rng_
    // private. Same seed is still deterministic; bit-identical replay across
    // versions is not required.
    sts2::game::Rng enemy_rng{seed};
    combat.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));
    combat.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));
    deck = sts2::cards::make_silent_starter_deck();
  }

  // Register discard callback BEFORE start() — preserves the legacy ordering.
  combat.set_pick_discard_callback([](const sts2::game::Combat& c) {
    return sts2::app::prompt_discard(c, std::cin, std::cout);
  });

  combat.start(std::move(deck));

  sts2::ai::Recommender ai;

  while (true) {
    sts2::render::render_combat(combat, std::cout);
    if (combat.combat_over()) {
      return 0;
    }

    std::cout << sts2::ansi::kCyan << "Analyzing combat..."
              << sts2::ansi::kReset << std::flush;
    sts2::ai::Recommendation rec = ai.recommend(combat);
    std::cout << "\x1b[2K\r" << std::flush;
    sts2::render::render_ai_recommendation(rec, combat, std::cout);

    std::cout << sts2::ansi::kGreen << ">" << sts2::ansi::kReset
              << " Play card [index], (e)nd turn, (q)uit: " << std::flush;
    sts2::input::Action a = sts2::input::read_action(std::cin);
    switch (a.kind) {
      case sts2::input::Action::kQuit:
        return 0;
      case sts2::input::Action::kEndTurn:
        combat.end_turn();
        break;
      case sts2::input::Action::kPlayCard: {
        if (!combat.can_play(a.card_idx)) {
          std::cout << sts2::ansi::kRed << "  unplayable." << sts2::ansi::kReset
                    << "\n";
          break;
        }
        const sts2::game::TargetType target_type =
            combat.player().hand.at(a.card_idx).target;
        sts2::game::EnemySlot target = sts2::game::EnemySlot::none();
        if (target_type == sts2::game::TargetType::kAnyEnemy) {
          target = sts2::app::prompt_target(combat, std::cin, std::cout);
          if (!target.valid()) {
            std::cout << sts2::ansi::kRed << "  no valid target."
                      << sts2::ansi::kReset << "\n";
            break;
          }
        }
        combat.play_card(a.card_idx, target);
        break;
      }
      case sts2::input::Action::kInvalid:
        std::cout << sts2::ansi::kRed << "  invalid input."
                  << sts2::ansi::kReset << "\n";
        break;
    }
  }
}

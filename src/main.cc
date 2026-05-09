#include <cstdint>
#include <iostream>

#include "sts2/ai/recommend.h"
#include "sts2/app/args.h"
#include "sts2/app/prompts.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/player.h"
#include "sts2/game/rng.h"
#include "sts2/input/input.h"
#include "sts2/render/ai_recommendation.h"
#include "sts2/render/ansi.h"
#include "sts2/render/console.h"
#include "sts2/render/render.h"

int main(int argc, char** argv) {
  uint64_t seed = 0;
  bool seed_provided = false;
  if (!sts2::app::parse_args(argc, argv, seed, seed_provided, std::cerr)) {
    return 1;
  }
  if (!seed_provided) {
    seed = sts2::app::random_seed();
  }

  sts2::console::enable_ansi_and_utf8();

  sts2::game::Combat combat{seed};

  // Intentional: enemy rolls use a separate Rng to keep Combat::rng_ private.
  // Same seed is still deterministic; bit-identical replay across versions is
  // not required.
  sts2::game::Rng enemy_rng{seed};
  combat.add_enemy(sts2::enemies::make_calcified_cultist(enemy_rng));
  combat.add_enemy(sts2::enemies::make_damp_cultist(enemy_rng));

  combat.set_pick_discard_callback([](const sts2::game::Combat& c) {
    return sts2::app::prompt_discard(c, std::cin, std::cout);
  });

  combat.start(sts2::cards::make_silent_starter_deck());

  sts2::ai::Recommender ai;

  while (true) {
    sts2::render::render_combat(combat, std::cout);
    if (combat.combat_over()) {
      return 0;
    }

    std::cout << sts2::ansi::kCyan << "Analyzing combat..." << sts2::ansi::kReset
              << std::flush;
    sts2::ai::Recommendation rec = ai.recommend(combat);
    std::cout << "\r                   \r";
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
        sts2::game::TargetType target_type =
            combat.player().hand[static_cast<size_t>(a.card_idx)].target;
        int target = -1;
        if (target_type == sts2::game::TargetType::kAnyEnemy) {
          target = sts2::app::prompt_target(combat, std::cin, std::cout);
          if (target < 0) {
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

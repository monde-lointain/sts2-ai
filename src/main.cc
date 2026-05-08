#include <cstdint>
#include <iostream>
#include <string>

#include "sts2/app/args.h"
#include "sts2/app/prompts.h"
#include "sts2/game/cards.h"
#include "sts2/game/combat.h"
#include "sts2/game/enemies.h"
#include "sts2/game/player.h"
#include "sts2/game/rng.h"
#include "sts2/input/input.h"
#include "sts2/render/ansi.h"
#include "sts2/render/console.h"
#include "sts2/render/render.h"

int main(int argc, char** argv) {
    uint64_t seed = 0;
    bool seed_provided = false;
    if (!app::parse_args(argc, argv, seed, seed_provided, std::cerr)) return 1;
    if (!seed_provided) seed = app::random_seed();

    console::enable_ansi_and_utf8();

    Combat combat{seed};

    // Intentional: enemy rolls use a separate Rng to keep Combat::rng_ private.
    // Same seed is still deterministic; bit-identical replay across versions is not required.
    Rng enemy_rng{seed};
    combat.add_enemy(enemies::make_calcified_cultist(enemy_rng));
    combat.add_enemy(enemies::make_damp_cultist(enemy_rng));

    combat.set_pick_discard_callback([](const Combat& c) {
        return app::prompt_discard(c, std::cin, std::cout);
    });

    combat.start(cards::make_silent_starter_deck());

    while (true) {
        render::render_combat(combat, std::cout);
        if (combat.combat_over()) return 0;

        std::cout << ansi::kGreen << ">" << ansi::kReset << " Play card [index], (e)nd turn, (q)uit: " << std::flush;
        input::Action a = input::read_action(std::cin);
        switch (a.kind) {
            case input::Action::Quit:
                return 0;
            case input::Action::EndTurn:
                combat.end_turn();
                break;
            case input::Action::PlayCard: {
                if (!combat.can_play(a.card_idx)) {
                    std::cout << ansi::kRed << "  unplayable." << ansi::kReset << "\n";
                    break;
                }
                TargetType target_type = combat.player().hand[static_cast<size_t>(a.card_idx)].target;
                int target = -1;
                if (target_type == TargetType::AnyEnemy) {
                    target = app::prompt_target(combat, std::cin, std::cout);
                    if (target < 0) {
                        std::cout << ansi::kRed << "  no valid target." << ansi::kReset << "\n";
                        break;
                    }
                }
                combat.play_card(a.card_idx, target);
                break;
            }
            case input::Action::Invalid:
                std::cout << ansi::kRed << "  invalid input." << ansi::kReset << "\n";
                break;
        }
    }
}

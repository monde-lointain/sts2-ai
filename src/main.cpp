#include <cstdint>
#include <cstdio>
#include <iostream>
#include <random>
#include <string>

#include "game/Cards.h"
#include "game/Combat.h"
#include "game/Enemies.h"
#include "game/Player.h"
#include "input/Input.h"
#include "render/Ansi.h"
#include "render/Console.h"
#include "render/Render.h"

namespace {

bool parse_uint64(const std::string& s, uint64_t& out) {
    if (s.empty()) return false;
    uint64_t v = 0;
    for (char ch : s) {
        if (ch < '0' || ch > '9') return false;
        uint64_t next = v * 10 + static_cast<uint64_t>(ch - '0');
        if (next < v) return false;
        v = next;
    }
    out = v;
    return true;
}

bool parse_args(int argc, char** argv, uint64_t& seed_out, bool& seed_provided) {
    seed_provided = false;
    for (int i = 1; i < argc; ++i) {
        std::string arg = argv[i];
        if (arg == "--seed") {
            if (i + 1 >= argc) {
                std::fprintf(stderr, "error: --seed requires a value\n");
                return false;
            }
            if (!parse_uint64(argv[i + 1], seed_out)) {
                std::fprintf(stderr, "error: --seed value '%s' is not a valid uint64\n", argv[i + 1]);
                return false;
            }
            seed_provided = true;
            ++i;
        } else {
            std::fprintf(stderr, "error: unknown argument '%s'\n", arg.c_str());
            return false;
        }
    }
    return true;
}

uint64_t random_seed() {
    std::random_device rd;
    uint64_t hi = static_cast<uint64_t>(rd());
    uint64_t lo = static_cast<uint64_t>(rd());
    return (hi << 32) | lo;
}

int prompt_target(const Combat& c) {
    int alive = 0;
    int last_alive_idx = -1;
    for (size_t i = 0; i < c.enemies.size(); ++i) {
        if (c.enemies[i].hp > 0) { ++alive; last_alive_idx = static_cast<int>(i); }
    }
    if (alive == 0) return -1;
    if (alive == 1) return last_alive_idx;
    while (true) {
        std::cout << "\n> Target enemy [index]: " << std::flush;
        int idx = input::read_index(std::cin, static_cast<int>(c.enemies.size()) - 1);
        if (idx < 0) {
            std::cout << ansi::kRed << "  invalid target." << ansi::kReset << "\n";
            continue;
        }
        if (c.enemies[static_cast<size_t>(idx)].hp <= 0) {
            std::cout << ansi::kRed << "  that enemy is already dead." << ansi::kReset << "\n";
            continue;
        }
        return idx;
    }
}

int prompt_discard(const Combat& combat) {
    const Player& p = combat.player;
    if (p.hand.size() == 1) return 0;
    while (true) {
        render::render_combat(combat, std::cout);
        std::cout << "  Discard which? [0-" << p.hand.size() - 1 << "]: " << std::flush;
        int idx = input::read_index(std::cin, static_cast<int>(p.hand.size()) - 1);
        if (idx >= 0) return idx;
        std::cout << ansi::kRed << "  invalid index." << ansi::kReset << "\n";
    }
}

}

int main(int argc, char** argv) {
    uint64_t seed = 0;
    bool seed_provided = false;
    if (!parse_args(argc, argv, seed, seed_provided)) return 1;
    if (!seed_provided) seed = random_seed();

    console::enable_ansi_and_utf8();

    Combat combat{seed};

    combat.enemies.push_back(enemies::make_calcified_cultist(combat.rng));
    combat.enemies.push_back(enemies::make_damp_cultist(combat.rng));

    combat.on_pick_discard = prompt_discard;

    combat.start(cards::make_silent_starter_deck());

    while (true) {
        render::render_combat(combat, std::cout);
        if (combat.combat_over) return 0;

        std::cout << "> Play card [index], (e)nd turn, (q)uit: " << std::flush;
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
                TargetType target_type = combat.player.hand[static_cast<size_t>(a.card_idx)].target;
                int target = -1;
                if (target_type == TargetType::AnyEnemy) {
                    target = prompt_target(combat);
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

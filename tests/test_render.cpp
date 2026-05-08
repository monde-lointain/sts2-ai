#include <sstream>
#include <string>

#include "game/Cards.h"
#include "game/Combat.h"
#include "game/Enemies.h"
#include "game/Enemy.h"
#include "game/Player.h"
#include "game/Power.h"
#include "game/Powers.h"
#include "game/Rng.h"
#include "game/Types.h"
#include "render/Bar.h"
#include "render/Render.h"
#include "test_helpers.h"
#include "test_runner.h"

static const std::string kFullBlock("\xe2\x96\x88");
static const std::string kEmptyBlock("\xe2\x96\x91");

static int count_substr(const std::string& s, const std::string& needle) {
    if (needle.empty()) return 0;
    int n = 0;
    size_t pos = 0;
    while ((pos = s.find(needle, pos)) != std::string::npos) { ++n; pos += needle.size(); }
    return n;
}

TEST(bar_full_hp_returns_all_filled) {
    std::string b = render::hp_bar(100, 100, 10);
    CHECK(count_substr(b, kFullBlock) == 10);
    CHECK(count_substr(b, kEmptyBlock) == 0);
}

TEST(bar_zero_hp_returns_all_empty) {
    std::string b = render::hp_bar(0, 100, 10);
    CHECK(count_substr(b, kFullBlock) == 0);
    CHECK(count_substr(b, kEmptyBlock) == 10);
}

TEST(bar_half_hp_returns_half_filled) {
    std::string b = render::hp_bar(50, 100, 10);
    CHECK(count_substr(b, kFullBlock) == 5);
    CHECK(count_substr(b, kEmptyBlock) == 5);
}

TEST(bar_one_of_hundred_shows_at_least_one_filled) {
    std::string b = render::hp_bar(1, 100, 10);
    CHECK(count_substr(b, kFullBlock) == 1);
    CHECK(count_substr(b, kEmptyBlock) == 9);
}

TEST(bar_zero_width_returns_empty) {
    CHECK(render::hp_bar(50, 100, 0).empty());
    CHECK(render::hp_bar(50, 100, -3).empty());
}

TEST(bar_zero_max_does_not_divide_by_zero) {
    std::string b = render::hp_bar(0, 0, 5);
    CHECK(count_substr(b, kFullBlock) == 0);
    CHECK(count_substr(b, kEmptyBlock) == 5);
}

TEST(card_short_stats_returns_expected_strings) {
    CHECK(cards::make_strike().short_stats == "6dmg");
    CHECK(cards::make_defend().short_stats == "5blk");
    CHECK(cards::make_neutralize().short_stats == "3dmg");
    CHECK(cards::make_survivor().short_stats == "8blk");
}

TEST(card_description_returns_expected_lines) {
    auto strike = cards::make_strike().description;
    CHECK(strike.size() == 1u);
    CHECK(strike[0] == "Deal 6 damage.");
    auto defend = cards::make_defend().description;
    CHECK(defend.size() == 1u);
    CHECK(defend[0] == "Gain 5 Block.");
    auto neutralize = cards::make_neutralize().description;
    CHECK(neutralize.size() == 2u);
    CHECK(neutralize[0] == "Deal 3 damage.");
    CHECK(neutralize[1] == "Apply 1 Weak.");
    auto survivor = cards::make_survivor().description;
    CHECK(survivor.size() == 2u);
    CHECK(survivor[0] == "Gain 8 Block.");
    CHECK(survivor[1] == "Discard 1 card.");
}

TEST(render_combat_includes_round_number) {
    Combat c{1};
    c.add_enemy(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("Round 1") != std::string::npos);
}

TEST(render_combat_includes_player_hp) {
    Combat c{1};
    c.add_enemy(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("70/70") != std::string::npos);
    CHECK(os.str().find("The Silent") != std::string::npos);
}

TEST(render_combat_shows_relic_line) {
    Combat c{1};
    c.add_enemy(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("Ring of the Snake") != std::string::npos);
}

TEST(render_combat_shows_deck_total) {
    Combat c{1};
    c.add_enemy(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("Deck 12") != std::string::npos);
}

TEST(render_combat_omits_block_when_zero) {
    Combat c{1};
    c.add_enemy(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find(" blk") == std::string::npos);
}

TEST(render_combat_shows_block_when_nonzero) {
    Combat c{1};
    c.add_enemy(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    CombatTestAccess{c}.player().vitals.block = 7;
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("7") != std::string::npos);
    CHECK(os.str().find(" blk") != std::string::npos);
}

TEST(render_combat_shows_target_arrow_for_attack_cards) {
    Combat c{1};
    c.add_enemy(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("\xe2\x86\x92") != std::string::npos);
}

TEST(render_combat_lists_each_enemy_with_index) {
    Combat c{1};
    Rng r(7);
    c.add_enemy(enemies::make_calcified_cultist(r));
    c.add_enemy(enemies::make_damp_cultist(r));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    const std::string out = os.str();
    CHECK(out.find("[0]") != std::string::npos);
    CHECK(out.find("[1]") != std::string::npos);
    CHECK(out.find("Calcified Cultist") != std::string::npos);
    CHECK(out.find("Damp Cultist") != std::string::npos);
}

TEST(render_combat_shows_attack_intent_for_dark_strike) {
    Combat c{1};
    Enemy e = make_dummy_enemy(40);
    e.dark_strike_base = 9;
    e.current_move = MoveId::DarkStrike;
    e.performed_first_move = true;
    c.add_enemy(std::move(e));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("\xe2\x9a\x94 9") != std::string::npos);
}

TEST(render_combat_shows_buff_intent_for_incantation) {
    Combat c{1};
    Rng r(7);
    c.add_enemy(enemies::make_calcified_cultist(r));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("\xe2\xac\x86" "Buff") != std::string::npos);
}

TEST(render_combat_omits_dead_enemies) {
    Combat c{1};
    Enemy alive = make_dummy_enemy(50);
    alive.name = "Survivor";
    Enemy dead = make_dummy_enemy(0);
    dead.name = "Goner";
    c.add_enemy(std::move(alive));
    c.add_enemy(std::move(dead));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    const std::string out = os.str();
    CHECK(out.find("Survivor") != std::string::npos);
    CHECK(out.find("Goner") == std::string::npos);
    CHECK(out.find("(slain)") == std::string::npos);
}

TEST(render_combat_renumbers_indices_when_first_enemy_dead) {
    Combat c{1};
    Enemy dead = make_dummy_enemy(0);
    dead.name = "Slain";
    Enemy alive = make_dummy_enemy(50);
    alive.name = "Standing";
    c.add_enemy(std::move(dead));
    c.add_enemy(std::move(alive));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    const std::string out = os.str();
    CHECK(out.find("[0] \x1b[1mStanding") != std::string::npos);
    CHECK(out.find("[1] \x1b[1mStanding") == std::string::npos);
    CHECK(out.find("Slain") == std::string::npos);
}

TEST(render_combat_shows_card_inline_stats) {
    Combat c{1};
    c.add_enemy(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("Strike") != std::string::npos);
    CHECK(os.str().find("6dmg") != std::string::npos);
}

TEST(render_combat_emits_ansi_escape_codes) {
    Combat c{1};
    c.add_enemy(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("\x1b[") != std::string::npos);
}

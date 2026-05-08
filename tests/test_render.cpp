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
    std::string b = hp_bar(100, 100, 10);
    CHECK(count_substr(b, kFullBlock) == 10);
    CHECK(count_substr(b, kEmptyBlock) == 0);
}

TEST(bar_zero_hp_returns_all_empty) {
    std::string b = hp_bar(0, 100, 10);
    CHECK(count_substr(b, kFullBlock) == 0);
    CHECK(count_substr(b, kEmptyBlock) == 10);
}

TEST(bar_half_hp_returns_half_filled) {
    std::string b = hp_bar(50, 100, 10);
    CHECK(count_substr(b, kFullBlock) == 5);
    CHECK(count_substr(b, kEmptyBlock) == 5);
}

TEST(bar_one_of_hundred_shows_at_least_one_filled) {
    std::string b = hp_bar(1, 100, 10);
    CHECK(count_substr(b, kFullBlock) == 1);
    CHECK(count_substr(b, kEmptyBlock) == 9);
}

TEST(bar_zero_width_returns_empty) {
    CHECK(hp_bar(50, 100, 0).empty());
    CHECK(hp_bar(50, 100, -3).empty());
}

TEST(bar_zero_max_does_not_divide_by_zero) {
    std::string b = hp_bar(0, 0, 5);
    CHECK(count_substr(b, kFullBlock) == 0);
    CHECK(count_substr(b, kEmptyBlock) == 5);
}

TEST(card_inline_stats_returns_expected_strings) {
    CHECK(render::card_inline_stats(cards::IdStrike) == "6dmg");
    CHECK(render::card_inline_stats(cards::IdDefend) == "5blk");
    CHECK(render::card_inline_stats(cards::IdNeutralize) == "3dmg + Weak 1");
    CHECK(render::card_inline_stats(cards::IdSurvivor) == "8blk, discard 1");
    CHECK(render::card_inline_stats(999).empty());
}

TEST(render_combat_includes_round_number) {
    Combat c{1};
    c.enemies.push_back(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("Round 1") != std::string::npos);
}

TEST(render_combat_includes_player_hp) {
    Combat c{1};
    c.enemies.push_back(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("70/70") != std::string::npos);
    CHECK(os.str().find("Silent") != std::string::npos);
}

TEST(render_combat_lists_each_enemy_with_index) {
    Combat c{1};
    Rng r(7);
    c.enemies.push_back(enemies::make_calcified_cultist(r));
    c.enemies.push_back(enemies::make_damp_cultist(r));
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
    c.enemies.push_back(std::move(e));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("ATK 9") != std::string::npos);
}

TEST(render_combat_shows_buff_intent_for_incantation) {
    Combat c{1};
    Rng r(7);
    c.enemies.push_back(enemies::make_calcified_cultist(r));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("BUFF") != std::string::npos);
}

TEST(render_combat_marks_dead_enemies_as_slain) {
    Combat c{1};
    c.enemies.push_back(make_dummy_enemy(0));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("(slain)") != std::string::npos);
}

TEST(render_combat_shows_card_inline_stats) {
    Combat c{1};
    c.enemies.push_back(make_dummy_enemy(50));
    c.start(cards::make_silent_starter_deck());
    std::ostringstream os;
    render::render_combat(c, os);
    CHECK(os.str().find("Strike") != std::string::npos);
    CHECK(os.str().find("6dmg") != std::string::npos);
}

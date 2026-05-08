#include <cstdint>
#include <vector>

#include "game/Cards.h"
#include "game/Combat.h"
#include "game/Enemy.h"
#include "game/Player.h"
#include "game/Power.h"
#include "game/Powers.h"
#include "game/Types.h"
#include "test_helpers.h"
#include "test_runner.h"

TEST(card_strike_factory_fields) {
    Card s = cards::make_strike();
    CHECK(s.id == cards::IdStrike);
    CHECK(s.name == "Strike");
    CHECK(s.cost == 1);
    CHECK(s.type == CardType::Attack);
    CHECK(s.target == TargetType::AnyEnemy);
    CHECK(static_cast<bool>(s.on_play));
}

TEST(card_strike_deals_six_damage_to_enemy) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    cards::make_strike().on_play(c, 0);
    CHECK(c.enemies[0].hp == 44);
    CHECK(c.enemies[0].block == 0);
    CHECK(c.player.hp == 70);
}

TEST(card_strike_respects_block) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    c.enemies[0].block = 10;
    cards::make_strike().on_play(c, 0);
    CHECK(c.enemies[0].hp == 50);
    CHECK(c.enemies[0].block == 4);
}

TEST(card_defend_factory_fields) {
    Card d = cards::make_defend();
    CHECK(d.id == cards::IdDefend);
    CHECK(d.name == "Defend");
    CHECK(d.cost == 1);
    CHECK(d.type == CardType::Skill);
    CHECK(d.target == TargetType::Self);
    CHECK(static_cast<bool>(d.on_play));
}

TEST(card_defend_grants_five_block) {
    Combat c{42};
    c.player.block = 0;
    cards::make_defend().on_play(c, -1);
    CHECK(c.player.block == 5);
}

TEST(card_defend_block_stacks) {
    Combat c{42};
    c.player.block = 3;
    cards::make_defend().on_play(c, -1);
    CHECK(c.player.block == 8);
}

TEST(card_neutralize_factory_fields) {
    Card n = cards::make_neutralize();
    CHECK(n.id == cards::IdNeutralize);
    CHECK(n.name == "Neutralize");
    CHECK(n.cost == 0);
    CHECK(n.type == CardType::Attack);
    CHECK(n.target == TargetType::AnyEnemy);
    CHECK(static_cast<bool>(n.on_play));
}

TEST(card_neutralize_deals_three_and_applies_weak) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    cards::make_neutralize().on_play(c, 0);
    CHECK(c.enemies[0].hp == 47);
    CHECK(powers::amount(c.enemies[0].powers, PowerKind::Weak) == 1);
}

TEST(card_neutralize_weak_stacks_on_repeat) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    cards::make_neutralize().on_play(c, 0);
    cards::make_neutralize().on_play(c, 0);
    CHECK(powers::amount(c.enemies[0].powers, PowerKind::Weak) == 2);
}

TEST(card_survivor_factory_fields) {
    Card s = cards::make_survivor();
    CHECK(s.id == cards::IdSurvivor);
    CHECK(s.name == "Survivor");
    CHECK(s.cost == 1);
    CHECK(s.type == CardType::Skill);
    CHECK(s.target == TargetType::Self);
    CHECK(static_cast<bool>(s.on_play));
}

TEST(card_survivor_grants_eight_block_and_discards_chosen) {
    Combat c{42};
    c.player.block = 0;
    c.player.hand.push_back(cards::make_strike());
    c.player.hand.push_back(cards::make_defend());
    c.player.hand.push_back(cards::make_neutralize());
    c.on_pick_discard = [](const Player& p) -> int {
        for (size_t i = 0; i < p.hand.size(); ++i) {
            if (p.hand[i].id == cards::IdDefend) return static_cast<int>(i);
        }
        return 0;
    };
    cards::make_survivor().on_play(c, -1);
    CHECK(c.player.block == 8);
    CHECK(c.player.hand.size() == 2);
    CHECK(c.player.discard_pile.size() == 1);
    CHECK(c.player.discard_pile[0].id == cards::IdDefend);
    CHECK(c.player.hand[0].id == cards::IdStrike);
    CHECK(c.player.hand[1].id == cards::IdNeutralize);
}

TEST(card_survivor_no_op_discard_when_hand_empty) {
    Combat c{42};
    c.player.block = 0;
    bool callback_invoked = false;
    c.on_pick_discard = [&](const Player&) -> int { callback_invoked = true; return 0; };
    cards::make_survivor().on_play(c, -1);
    CHECK(c.player.block == 8);
    CHECK(c.player.hand.empty());
    CHECK(c.player.discard_pile.empty());
    CHECK(!callback_invoked);
}

TEST(card_starter_deck_size_and_composition) {
    auto deck = cards::make_silent_starter_deck();
    CHECK(deck.size() == 12u);
    int strike = 0, defend = 0, neutralize = 0, survivor = 0;
    for (const auto& card : deck) {
        if (card.id == cards::IdStrike) ++strike;
        else if (card.id == cards::IdDefend) ++defend;
        else if (card.id == cards::IdNeutralize) ++neutralize;
        else if (card.id == cards::IdSurvivor) ++survivor;
    }
    CHECK(strike == 5);
    CHECK(defend == 5);
    CHECK(neutralize == 1);
    CHECK(survivor == 1);
}

TEST(card_strike_with_strength_two_deals_eight) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    powers::apply(c.player.powers, PowerKind::Strength, 2);
    cards::make_strike().on_play(c, 0);
    CHECK(c.enemies[0].hp == 42);
}

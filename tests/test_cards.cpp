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
    CHECK(s.id == CardId::Strike);
    CHECK(s.name == "Strike");
    CHECK(s.cost == 1);
    CHECK(s.type == CardType::Attack);
    CHECK(s.target == TargetType::AnyEnemy);
    CHECK(static_cast<bool>(s.on_play));
}

TEST(card_strike_deals_six_damage_to_enemy) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    cards::make_strike().on_play(c, 0);
    CHECK(c.enemies()[0].vitals.hp == 44);
    CHECK(c.enemies()[0].vitals.block == 0);
    CHECK(c.player().vitals.hp == 70);
}

TEST(card_strike_respects_block) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    CombatTestAccess{c}.enemy(0).vitals.block = 10;
    cards::make_strike().on_play(c, 0);
    CHECK(c.enemies()[0].vitals.hp == 50);
    CHECK(c.enemies()[0].vitals.block == 4);
}

TEST(card_defend_factory_fields) {
    Card d = cards::make_defend();
    CHECK(d.id == CardId::Defend);
    CHECK(d.name == "Defend");
    CHECK(d.cost == 1);
    CHECK(d.type == CardType::Skill);
    CHECK(d.target == TargetType::Self);
    CHECK(static_cast<bool>(d.on_play));
}

TEST(card_defend_grants_five_block) {
    Combat c{42};
    CombatTestAccess{c}.player().vitals.block = 0;
    cards::make_defend().on_play(c, -1);
    CHECK(c.player().vitals.block == 5);
}

TEST(card_defend_block_stacks) {
    Combat c{42};
    CombatTestAccess{c}.player().vitals.block = 3;
    cards::make_defend().on_play(c, -1);
    CHECK(c.player().vitals.block == 8);
}

TEST(card_neutralize_factory_fields) {
    Card n = cards::make_neutralize();
    CHECK(n.id == CardId::Neutralize);
    CHECK(n.name == "Neutralize");
    CHECK(n.cost == 0);
    CHECK(n.type == CardType::Attack);
    CHECK(n.target == TargetType::AnyEnemy);
    CHECK(static_cast<bool>(n.on_play));
}

TEST(card_neutralize_deals_three_and_applies_weak) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    cards::make_neutralize().on_play(c, 0);
    CHECK(c.enemies()[0].vitals.hp == 47);
    CHECK(powers::amount(c.enemies()[0].vitals.powers, PowerKind::Weak) == 1);
}

TEST(card_neutralize_weak_stacks_on_repeat) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    cards::make_neutralize().on_play(c, 0);
    cards::make_neutralize().on_play(c, 0);
    CHECK(powers::amount(c.enemies()[0].vitals.powers, PowerKind::Weak) == 2);
}

TEST(card_survivor_factory_fields) {
    Card s = cards::make_survivor();
    CHECK(s.id == CardId::Survivor);
    CHECK(s.name == "Survivor");
    CHECK(s.cost == 1);
    CHECK(s.type == CardType::Skill);
    CHECK(s.target == TargetType::Self);
    CHECK(static_cast<bool>(s.on_play));
}

TEST(card_survivor_grants_eight_block_and_discards_chosen) {
    Combat c{42};
    auto& p = CombatTestAccess{c}.player();
    p.vitals.block = 0;
    p.hand.push_back(cards::make_strike());
    p.hand.push_back(cards::make_defend());
    p.hand.push_back(cards::make_neutralize());
    c.set_pick_discard_callback([](const Combat& combat) -> int {
        for (size_t i = 0; i < combat.player().hand.size(); ++i) {
            if (combat.player().hand[i].id == CardId::Defend) return static_cast<int>(i);
        }
        return 0;
    });
    cards::make_survivor().on_play(c, -1);
    CHECK(c.player().vitals.block == 8);
    CHECK(c.player().hand.size() == 2);
    CHECK(c.player().discard_pile.size() == 1);
    CHECK(c.player().discard_pile[0].id == CardId::Defend);
    CHECK(c.player().hand[0].id == CardId::Strike);
    CHECK(c.player().hand[1].id == CardId::Neutralize);
}

TEST(card_survivor_no_op_discard_when_hand_empty) {
    Combat c{42};
    CombatTestAccess{c}.player().vitals.block = 0;
    bool callback_invoked = false;
    c.set_pick_discard_callback([&](const Combat&) -> int { callback_invoked = true; return 0; });
    cards::make_survivor().on_play(c, -1);
    CHECK(c.player().vitals.block == 8);
    CHECK(c.player().hand.empty());
    CHECK(c.player().discard_pile.empty());
    CHECK(!callback_invoked);
}

TEST(card_starter_deck_size_and_composition) {
    auto deck = cards::make_silent_starter_deck();
    CHECK(deck.size() == 12u);
    int strike = 0, defend = 0, neutralize = 0, survivor = 0;
    for (const auto& card : deck) {
        if (card.id == CardId::Strike) ++strike;
        else if (card.id == CardId::Defend) ++defend;
        else if (card.id == CardId::Neutralize) ++neutralize;
        else if (card.id == CardId::Survivor) ++survivor;
    }
    CHECK(strike == 5);
    CHECK(defend == 5);
    CHECK(neutralize == 1);
    CHECK(survivor == 1);
}

TEST(card_strike_with_strength_two_deals_eight) {
    Combat c = make_combat_with(make_dummy_enemy(50));
    powers::apply(CombatTestAccess{c}.player().vitals.powers, PowerKind::Strength, 2);
    cards::make_strike().on_play(c, 0);
    CHECK(c.enemies()[0].vitals.hp == 42);
}

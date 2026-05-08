#include "game/Cards.h"

#include <utility>

#include "game/Combat.h"
#include "game/Damage.h"
#include "game/Powers.h"

namespace cards {

Card make_strike() {
    Card c;
    c.id = IdStrike;
    c.name = "Strike";
    c.cost = 1;
    c.type = CardType::Attack;
    c.target = TargetType::AnyEnemy;
    c.on_play = [](Combat& combat, int target_idx) {
        Enemy& e = combat.enemies[static_cast<size_t>(target_idx)];
        int dmg = damage::compute_outgoing(combat.player.powers, 6);
        damage::apply_to_defender(e.block, e.hp, dmg);
    };
    return c;
}

Card make_defend() {
    Card c;
    c.id = IdDefend;
    c.name = "Defend";
    c.cost = 1;
    c.type = CardType::Skill;
    c.target = TargetType::Self;
    c.on_play = [](Combat& combat, int) {
        combat.player.block += 5;
    };
    return c;
}

Card make_neutralize() {
    Card c;
    c.id = IdNeutralize;
    c.name = "Neutralize";
    c.cost = 0;
    c.type = CardType::Attack;
    c.target = TargetType::AnyEnemy;
    c.on_play = [](Combat& combat, int target_idx) {
        Enemy& e = combat.enemies[static_cast<size_t>(target_idx)];
        int dmg = damage::compute_outgoing(combat.player.powers, 3);
        damage::apply_to_defender(e.block, e.hp, dmg);
        powers::apply(e.powers, PowerKind::Weak, 1);
    };
    return c;
}

Card make_survivor() {
    Card c;
    c.id = IdSurvivor;
    c.name = "Survivor";
    c.cost = 1;
    c.type = CardType::Skill;
    c.target = TargetType::Self;
    c.on_play = [](Combat& combat, int) {
        combat.player.block += 8;
        if (combat.player.hand.empty()) return;
        int idx = combat.on_pick_discard(combat);
        Card chosen = std::move(combat.player.hand[static_cast<size_t>(idx)]);
        combat.player.hand.erase(combat.player.hand.begin() + idx);
        combat.player.discard_pile.push_back(std::move(chosen));
    };
    return c;
}

std::vector<Card> make_silent_starter_deck() {
    std::vector<Card> deck;
    deck.reserve(12);
    for (int i = 0; i < 5; ++i) deck.push_back(make_strike());
    for (int i = 0; i < 5; ++i) deck.push_back(make_defend());
    deck.push_back(make_neutralize());
    deck.push_back(make_survivor());
    return deck;
}

}

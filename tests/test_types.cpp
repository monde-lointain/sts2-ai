#include <vector>
#include "game/Card.h"
#include "game/Enemy.h"
#include "game/Player.h"
#include "game/Power.h"
#include "game/Types.h"
#include "test_runner.h"

TEST(player_default_state) {
    Player p;
    CHECK(p.vitals.hp == 70);
    CHECK(p.vitals.max_hp == 70);
    CHECK(p.vitals.block == 0);
    CHECK(p.energy == 0);
    CHECK(p.max_energy == 3);
    CHECK(p.draw_pile.empty());
    CHECK(p.hand.empty());
    CHECK(p.discard_pile.empty());
    CHECK(p.exhaust_pile.empty());
    CHECK(p.vitals.powers.empty());
}

TEST(enemy_default_state) {
    Enemy e;
    CHECK(e.vitals.hp == 0);
    CHECK(e.vitals.max_hp == 0);
    CHECK(e.vitals.block == 0);
    CHECK(e.vitals.powers.empty());
    CHECK(e.current_move == MoveId::Incantation);
    CHECK(!e.performed_first_move);
    CHECK(e.dark_strike_base == 0);
    CHECK(e.ritual_amount == 0);
}

TEST(power_default_state) {
    Power p;
    CHECK(p.kind == PowerKind::Weak);
    CHECK(p.amount == 0);
    CHECK(!p.just_applied);
}

TEST(card_default_state) {
    Card c;
    CHECK(c.id == CardId::None);
    CHECK(c.cost == 0);
    CHECK(c.type == CardType::Skill);
    CHECK(c.target == TargetType::Self);
    CHECK(!c.on_play);
    CHECK(c.name.empty());
}

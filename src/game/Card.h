#pragma once

#include <functional>
#include <string>
#include "game/Types.h"

class Combat;

struct Card {
    int id = 0;
    std::string name;
    int cost = 0;
    CardType type = CardType::Skill;
    TargetType target = TargetType::Self;
    std::function<void(Combat&, int target_idx)> on_play;
};

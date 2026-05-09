#pragma once

#include <functional>
#include <string>
#include <vector>

#include "sts2/game/types.h"

namespace sts2::game {

class Combat;

struct Card {
  CardId id = CardId::kNone;
  std::string name;
  int cost = 0;
  CardType type = CardType::kSkill;
  TargetType target = TargetType::kSelf;
  int base_damage = 0;
  int base_block = 0;
  std::string short_stats;
  std::vector<std::string> description;
  std::function<void(Combat&, int target_idx)> on_play;
};

}  // namespace sts2::game

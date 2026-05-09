#include "sts2/game/damage.h"

#include "sts2/game/damage_calc.h"
#include "sts2/game/power.h"
#include "sts2/game/powers.h"
#include "sts2/game/types.h"

namespace sts2::damage {

int compute_outgoing(const std::vector<sts2::game::Power>& attacker_powers,
                     int base_damage) {
  const int strength = sts2::powers::amount(
      attacker_powers, sts2::game::PowerKind::kStrength);
  const int weak =
      sts2::powers::amount(attacker_powers, sts2::game::PowerKind::kWeak);
  return compute_outgoing(base_damage, strength, weak);
}

int apply_to_defender(sts2::game::Vitals& target, int incoming) {
  return apply_to_defender(target.hp, target.block, incoming);
}

}  // namespace sts2::damage

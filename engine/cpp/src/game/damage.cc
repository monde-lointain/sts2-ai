#include "sts2/game/damage.h"

#include "sts2/game/damage_calc.h"
#include "sts2/game/power.h"
#include "sts2/game/powers.h"
#include "sts2/game/types.h"

namespace sts2::damage {

int compute_outgoing(const std::vector<sts2::game::Power>& attacker_powers,
                     int base_damage) {
  const int strength =
      sts2::powers::amount(attacker_powers, sts2::game::PowerKind::kStrength);
  const int weak =
      sts2::powers::amount(attacker_powers, sts2::game::PowerKind::kWeak);
  return compute_outgoing(base_damage, strength, weak);
}

int apply_to_defender(sts2::game::Vitals& target, int incoming) {
  return apply_to_defender(target.hp, target.block, incoming);
}

int compute_outgoing_attack(int base, int attacker_strength,
                            int attacker_weak) noexcept {
  // Delegates to damage_calc::compute_outgoing; same formula, explicit args.
  return sts2::damage::compute_outgoing(base, attacker_strength, attacker_weak);
}

int compute_outgoing_block(int base, int gainer_dexterity, bool gainer_frail,
                           bool is_powered_source) noexcept {
  // STS canonical block formula. Reference: BlockCmd.cs (upstream STS2).
  // effective_block = floor((base + dex) * (frail && powered ? 0.75 : 1.0))
  // Integer floor via (v * 3) / 4 for the frail case.
  const int adjusted = base + gainer_dexterity;
  int result = adjusted;
  if (gainer_frail && is_powered_source) {
    result = (adjusted * 3) / 4;  // integer floor of * 0.75
  }
  return result < 0 ? 0 : result;
}

}  // namespace sts2::damage

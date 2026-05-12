#include "sts2/oracle/adapter/cultists_projection.h"

#include <algorithm>
#include <cassert>
#include <cstdint>
#include <string>
#include <string_view>

#include "sts2/ai/state.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/enemies.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/state_blob.h"

// CULTISTS_NORMAL projection. Q1-wire ModelId / Name strings are pinned by
// the C# content classes
//   src/Sts2Headless.Domain/Content/Monsters/CalcifiedCultist.cs
//   src/Sts2Headless.Domain/Content/Monsters/DampCultist.cs
//   src/Sts2Headless.Domain/Content/Cards/{StrikeSilent,DefendSilent,
//     Neutralize,Survivor}.cs
// and verified against fixture #1's dump.

namespace sts2::oracle::adapter {

namespace {

// Q1-emitted PowerInstance.ModelId strings consumed by the projection.
constexpr std::string_view kPowerIdStrength = "Strength";
constexpr std::string_view kPowerIdWeak = "Weak";
constexpr std::string_view kPowerIdRitual = "Ritual";

sts2::game::CardId map_card_model_id(std::string_view model_id) {
  const sts2::game::CardId id =
      sts2::game::card_effects::card_id_from_wire_model_id(model_id);
  if (id != sts2::game::CardId::kNone) { return id;
}
  throw StateCodecError("unknown card ModelId: " + std::string(model_id));
}

sts2::game::MoveId map_move_id(std::string_view move_id) {
  sts2::game::MoveId id = sts2::game::MoveId::kIncantation;
  if (sts2::game::move_calc::try_move_id_from_wire_id(move_id, id)) { return id;
}
  throw StateCodecError("unknown MoveId: " + std::string(move_id));
}

// Find power stack count by ModelId; 0 if absent.
std::int32_t power_stacks(const ParsedCreature& cr, std::string_view id) {
  for (const auto& p : cr.powers) {
    if (p.model_id == id) {
      return p.stacks;
    }
  }
  return 0;
}

bool has_power_with_just_applied(const ParsedCreature& cr,
                                 std::string_view id) {
  for (const auto& p : cr.powers) {
    if (p.model_id == id) {
      return p.just_applied;
    }
  }
  return false;
}

void tally_pile(sts2::ai::CardCounts& counts,
                const std::vector<ParsedCardInstance>& pile) {
  for (const auto& card : pile) {
    const sts2::game::CardId id = map_card_model_id(card.model_id);
    ++counts[id];
  }
}

bool is_calcified_or_damp_name(std::string_view name) {
  return sts2::enemies::cultist_archetype_from_wire_name(name) != nullptr;
}

sts2::ai::EnemyState project_one_enemy(const ParsedCreature& cr) {
  const sts2::enemies::CultistArchetype* archetype =
      sts2::enemies::cultist_archetype_from_wire_name(cr.name);
  if (archetype == nullptr) {
    throw StateCodecError("unknown cultist Creature.Name: " +
                          std::string(cr.name));
  }

  // performed_first_move is a C++-prototype-private bool tracking whether
  // the enemy has yet acted on its initial intent. Q1's wire doesn't expose
  // it directly; for a Q1-fixture-boot snapshot (post-StartCombat, pre-
  // first-script-action) the enemy has not yet acted, so this is false.
  // If a future fixture is mid-combat we'll need to infer this differently
  // — surfaced as a comment for future expansion.
  // The wire's intent.move_id records the enemy's CURRENT intent (the move
  // it will perform on its next act). Map to MoveId enum.
  const sts2::game::MoveId current_move =
      cr.intent_present ? map_move_id(cr.intent.move_id)
                        : sts2::game::MoveId::kIncantation;
  return sts2::ai::EnemyStateBuilder{}
      .alive(cr.current_hp > 0)
      .hp(sts2::game::Stat{cr.current_hp})
      .block(sts2::game::Stat{cr.block})
      .strength(sts2::game::Stat{power_stacks(cr, kPowerIdStrength)})
      .weak(sts2::game::Stat{power_stacks(cr, kPowerIdWeak)})
      .dark_strike_base(sts2::game::Stat{archetype->dark_strike_base})
      .ritual_amount(sts2::game::Stat{archetype->ritual_amount})
      .just_applied_ritual(has_power_with_just_applied(cr, kPowerIdRitual))
      .performed_first_move(false)
      .current_move(current_move)
      .build();
}

}  // namespace

bool is_cultists_normal(const ParsedCombatState& combat) {
  if (combat.enemy_count != 2 || combat.enemies.size() != 2U) {
    return false;
  }
  // Both enemies must be alive (HP > 0) — a CULTISTS_NORMAL snapshot with
  // one cultist already dead is mid-combat and Phase-1A's prototype expects
  // the starter shape. Looser semantics can land later.
  for (const auto& e : combat.enemies) {
    if (e.current_hp <= 0) { return false;
}
  }
  // The name pair must be exactly { Calcified, Damp } in either order.
  const std::string_view a = combat.enemies[0].name;
  const std::string_view b = combat.enemies[1].name;
  if (!is_calcified_or_damp_name(a) || !is_calcified_or_damp_name(b)) {
    return false;
  }
  return a != b;  // disallow (Calcified, Calcified) etc.
}

sts2::ai::CompactState project_cultists_normal(
    const ParsedCombatState& combat) {
  assert(is_cultists_normal(combat));

  sts2::ai::CompactStateBuilder builder;
  builder.player_hp(sts2::game::Stat{combat.player.current_hp})
      .player_block(sts2::game::Stat{combat.player.block})
      .player_strength(
          sts2::game::Stat{power_stacks(combat.player, kPowerIdStrength)})
      .player_weak(sts2::game::Stat{power_stacks(combat.player, kPowerIdWeak)})
      .energy(sts2::game::Stat{combat.energy});
  // Q1's turn_counter is 1-based at the smoke fixture boot (turn=1, pre-
  // first-action). The C++ prototype's `round` field is also 1-based at
  // combat start (round=1 enables Ring of the Snake's 7-card first-turn
  // draw). Map turn_counter -> round directly; floor at 1 for defense.
  builder.round(static_cast<std::uint16_t>(std::max(1, combat.turn_counter)))
      .phase(sts2::ai::Phase::kPlayerActing);

  builder.enemy(0, project_one_enemy(combat.enemies[0]))
      .enemy(1, project_one_enemy(combat.enemies[1]));

  sts2::ai::CardCounts hand;
  sts2::ai::CardCounts draw;
  sts2::ai::CardCounts discard;
  tally_pile(hand, combat.hand_pile);
  tally_pile(draw, combat.draw_pile);
  tally_pile(discard, combat.discard_pile);
  builder.hand(hand).draw(draw).discard(discard);
  // ExhaustPile is intentionally not surfaced into CompactState — the
  // prototype doesn't model exhausted cards in the search state (Survivor
  // discard semantics are handled at action-time per transition.cc).

  return builder.build();
}

}  // namespace sts2::oracle::adapter

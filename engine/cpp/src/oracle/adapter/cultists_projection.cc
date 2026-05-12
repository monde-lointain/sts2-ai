#include "sts2/oracle/adapter/cultists_projection.h"

#include <algorithm>
#include <array>
#include <cassert>
#include <cstdint>
#include <string>
#include <string_view>

#include "sts2/ai/state.h"
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

// Q1-emitted Creature.Name strings on the cultist pair (verified against
// fixture #1 bytes on 2026-05-12). Format note: Q1 wire uses single-token
// names (no spaces); the C++ prototype's internal Enemy::name happens to
// use "Calcified Cultist" / "Damp Cultist" with spaces — those are *not*
// what flows over the wire.
constexpr std::string_view kCalcifiedCultistName = "CalcifiedCultist";
constexpr std::string_view kDampCultistName = "DampCultist";

// Q1-emitted MonsterIntent.MoveId strings. The wire records the *next*
// intent the enemy will perform; both cultists start round 1 with
// INCANTATION_MOVE (ritual buff), then loop on DARK_STRIKE_MOVE.
constexpr std::string_view kIncantationMoveId = "INCANTATION_MOVE";
constexpr std::string_view kDarkStrikeMoveId = "DARK_STRIKE_MOVE";

// Q1-emitted PowerInstance.ModelId strings consumed by the projection.
constexpr std::string_view kPowerIdStrength = "Strength";
constexpr std::string_view kPowerIdWeak = "Weak";
constexpr std::string_view kPowerIdRitual = "Ritual";

// Card ModelId → Q2 CardId enum. Phase-1A Silent starter deck only.
sts2::game::CardId map_card_model_id(std::string_view model_id) {
  if (model_id == "StrikeSilent") return sts2::game::CardId::kStrike;
  if (model_id == "DefendSilent") return sts2::game::CardId::kDefend;
  if (model_id == "Neutralize") return sts2::game::CardId::kNeutralize;
  if (model_id == "Survivor") return sts2::game::CardId::kSurvivor;
  throw StateCodecError("unknown card ModelId: " + std::string(model_id));
}

// MoveId string → MoveId enum. Sentinel-on-unknown so the projection fails
// loud rather than producing a Search-tractable but wrong state.
sts2::game::MoveId map_move_id(std::string_view move_id) {
  if (move_id == kIncantationMoveId) return sts2::game::MoveId::kIncantation;
  if (move_id == kDarkStrikeMoveId) return sts2::game::MoveId::kDarkStrike;
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
  return name == kCalcifiedCultistName || name == kDampCultistName;
}

// Cultist-specific base damage. Sourced from C++ prototype
// enemies.cc:make_calcified_cultist (dark_strike_base=9) and
// make_damp_cultist (dark_strike_base=1); cross-verified against C# content
// (CalcifiedCultist.DarkStrikeDamage=9, DampCultist.DarkStrikeDamage=1).
// Ritual amounts: Calcified=2, Damp=5 per same sources.
struct CultistParams {
  sts2::game::Stat dark_strike_base;
  sts2::game::Stat ritual_amount;
};

CultistParams params_for(std::string_view name) {
  if (name == kCalcifiedCultistName) {
    return CultistParams{sts2::game::Stat{9}, sts2::game::Stat{2}};
  }
  // Damp.
  return CultistParams{sts2::game::Stat{1}, sts2::game::Stat{5}};
}

sts2::ai::EnemyState project_one_enemy(const ParsedCreature& cr) {
  sts2::ai::EnemyState e;
  e.alive = cr.current_hp > 0;
  e.hp = sts2::game::Stat{cr.current_hp};
  e.block = sts2::game::Stat{cr.block};
  e.strength = sts2::game::Stat{power_stacks(cr, kPowerIdStrength)};
  e.weak = sts2::game::Stat{power_stacks(cr, kPowerIdWeak)};

  const CultistParams params = params_for(cr.name);
  e.dark_strike_base = params.dark_strike_base;
  e.ritual_amount = params.ritual_amount;

  e.just_applied_ritual = has_power_with_just_applied(cr, kPowerIdRitual);
  // performed_first_move is a C++-prototype-private bool tracking whether
  // the enemy has yet acted on its initial intent. Q1's wire doesn't expose
  // it directly; for a Q1-fixture-boot snapshot (post-StartCombat, pre-
  // first-script-action) the enemy has not yet acted, so this is false.
  // If a future fixture is mid-combat we'll need to infer this differently
  // — surfaced as a comment for future expansion.
  e.performed_first_move = false;

  // The wire's intent.move_id records the enemy's CURRENT intent (the move
  // it will perform on its next act). Map to MoveId enum.
  if (cr.intent_present) {
    e.current_move = map_move_id(cr.intent.move_id);
  } else {
    e.current_move = sts2::game::MoveId::kIncantation;
  }
  return e;
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
    if (e.current_hp <= 0) return false;
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

  sts2::ai::CompactState s;
  s.player_hp = sts2::game::Stat{combat.player.current_hp};
  s.player_block = sts2::game::Stat{combat.player.block};
  s.player_strength =
      sts2::game::Stat{power_stacks(combat.player, kPowerIdStrength)};
  s.player_weak = sts2::game::Stat{power_stacks(combat.player, kPowerIdWeak)};
  s.energy = sts2::game::Stat{combat.energy};
  // Q1's turn_counter is 1-based at the smoke fixture boot (turn=1, pre-
  // first-action). The C++ prototype's `round` field is also 1-based at
  // combat start (round=1 enables Ring of the Snake's 7-card first-turn
  // draw). Map turn_counter -> round directly; floor at 1 for defense.
  s.round = static_cast<std::uint16_t>(std::max(1, combat.turn_counter));
  s.phase = sts2::ai::Phase::kPlayerActing;

  s.enemies[0] = project_one_enemy(combat.enemies[0]);
  s.enemies[1] = project_one_enemy(combat.enemies[1]);

  tally_pile(s.hand, combat.hand_pile);
  tally_pile(s.draw, combat.draw_pile);
  tally_pile(s.discard, combat.discard_pile);
  // ExhaustPile is intentionally not surfaced into CompactState — the
  // prototype doesn't model exhausted cards in the search state (Survivor
  // discard semantics are handled at action-time per transition.cc).

  return s;
}

}  // namespace sts2::oracle::adapter

#include "sts2/oracle/adapter/louse_progenitor_projection.h"

#include <algorithm>
#include <cassert>
#include <cstdint>
#include <string>
#include <string_view>

#include "sts2/ai/state.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/project_powers.h"
#include "sts2/oracle/adapter/state_blob.h"

// LOUSE_PROGENITOR_NORMAL projection. Q1-wire Name is pinned by
//   engine/headless/.../Phase1Monsters.cs:159 (LouseProgenitor::CanonicalId).
// Phase-1A single-monster solo encounter. Move table: see monster_moves.cc.

namespace sts2::oracle::adapter {

namespace {

constexpr std::string_view kLouseProgenitorWireName = "LouseProgenitor";

// Q1-emitted PowerInstance.ModelId strings consumed by this projection.
constexpr std::string_view kPowerIdCurlUp = "CurlUp";

// Spawn-power expectation: CurlUp(14) at A0. If the wire blob omits it
// (Q1 silent-drop pattern), synthesize it per Q2-ADR-005.
constexpr int32_t kCurlUpSpawnStacks = 14;

// Map MoveId from wire; throws StateCodecError on unknown.
sts2::game::MoveId map_move_id(std::string_view move_id) {
  sts2::game::MoveId id = sts2::game::MoveId::kWebCannon;
  if (sts2::game::move_calc::try_move_id_from_wire_id(move_id, id)) {
    return id;
  }
  throw StateCodecError("LouseProgenitor: unknown MoveId: " +
                        std::string(move_id));
}

// Find the move_index in the LouseProgenitor table for the given MoveId.
uint8_t move_index_for(sts2::game::MoveId move) noexcept {
  using sts2::game::MoveId;
  switch (move) {
    case MoveId::kWebCannon:
      return 0;
    case MoveId::kCurlAndGrow:
      return 1;
    case MoveId::kPounce:
      return 2;
    case MoveId::kIncantation:
    case MoveId::kDarkStrike:
    case MoveId::kTackleMove:
    case MoveId::kGoopMove:
    case MoveId::kClumpShot:
    case MoveId::kStickyShot:
    case MoveId::kPokeyPounce:
    case MoveId::kButtMove:
    case MoveId::kSliceMove:
    case MoveId::kHissMove:
      // Not louse moves; should never be reached.
      return 0;
  }
  return 0;  // unreachable; silence compiler
}

sts2::ai::EnemyState project_louse(const ParsedCreature& cr) {
  const sts2::game::MoveId current_move = cr.intent_present
                                              ? map_move_id(cr.intent.move_id)
                                              : sts2::game::MoveId::kWebCannon;
  const uint8_t midx = move_index_for(current_move);

  sts2::ai::EnemyStateBuilder builder;
  builder.alive(cr.current_hp > 0)
      .hp(sts2::game::Stat{cr.current_hp})
      .block(sts2::game::Stat{cr.block})
      .strength(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(cr, "Strength"))})
      .weak(sts2::game::Stat{static_cast<int>(parsed_power_stacks(cr, "Weak"))})
      .kind(sts2::game::MonsterKind::kLouseProgenitor)
      .current_move(current_move)
      .move_index(midx)
      .performed_first_move(false);

  // CurlUp: read from wire; synthesize if absent (Q2-ADR-005 silent-drop).
  // Wave-23/J.beta: stacks widened int16_t → int32_t (Q2-ADR-014).
  const std::int32_t wire_curl = parsed_power_stacks(cr, kPowerIdCurlUp);
  const int32_t curl_stacks = (wire_curl > 0) ? wire_curl : kCurlUpSpawnStacks;
  builder.add_power(sts2::game::PowerKind::kCurlUp, curl_stacks);

  // Frail on the enemy (not expected at smoke boot but handle for
  // completeness).
  const std::int32_t wire_frail = parsed_power_stacks(cr, "Frail");
  if (wire_frail > 0) {
    builder.add_power(sts2::game::PowerKind::kFrail, wire_frail);
  }

  return builder.build();
}

void tally_pile(sts2::ai::CardCounts& counts,
                const std::vector<ParsedCardInstance>& pile) {
  for (const auto& card : pile) {
    const sts2::game::CardId id =
        sts2::game::card_effects::card_id_from_wire_model_id(card.model_id);
    if (id != sts2::game::CardId::kNone) {
      ++counts[id];
    }
    // Unknown card model ids are silently skipped at the adapter level;
    // the diagnostic (StateCodecError) is reserved for hard failures.
  }
}

}  // namespace

bool is_louse_progenitor_normal(const ParsedCombatState& combat) {
  if (combat.enemy_count != 1 || combat.enemies.size() != 1U) {
    return false;
  }
  const ParsedCreature& e = combat.enemies[0];
  if (e.name != kLouseProgenitorWireName) {
    return false;
  }
  // Must be alive (HP > 0).
  return e.current_hp > 0;
}

sts2::ai::CompactState project_louse_progenitor_normal(
    const ParsedCombatState& combat) {
  assert(is_louse_progenitor_normal(combat));

  sts2::ai::CompactStateBuilder builder;
  builder.player_hp(sts2::game::Stat{combat.player.current_hp})
      .player_block(sts2::game::Stat{combat.player.block})
      .player_strength(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(combat.player, "Strength"))})
      .player_weak(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(combat.player, "Weak"))})
      .energy(sts2::game::Stat{combat.energy})
      .round(std::max(1, combat.turn_counter))
      .phase(sts2::ai::Phase::kPlayerActing);

  builder.enemy(0, project_louse(combat.enemies[0]));

  sts2::ai::CardCounts hand;
  sts2::ai::CardCounts draw;
  sts2::ai::CardCounts discard;
  tally_pile(hand, combat.hand_pile);
  tally_pile(draw, combat.draw_pile);
  tally_pile(discard, combat.discard_pile);
  builder.hand(hand).draw(draw).discard(discard);

  return builder.build();
}

}  // namespace sts2::oracle::adapter

#include "sts2/oracle/adapter/nibbits_normal_projection.h"

#include <algorithm>
#include <cassert>
#include <cstdint>
#include <string>
#include <string_view>

#include "sts2/ai/state.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/project_powers.h"
#include "sts2/oracle/adapter/state_blob.h"

// NIBBITS_NORMAL projection. Q1-wire Name is pinned by
//   engine/headless/.../NibbitsNormal encounter + Nibbit.cs:
// Two-Nibbit encounter with per-slot move overrides:
//   slot 0 (front): SLICE_MOVE, slot 1 (back): HISS_MOVE.
// Source fixture: 08-nibbits-normal-seed42.

namespace sts2::oracle::adapter {

namespace {

constexpr std::string_view kNibbitWireName = "Nibbit";

// Map MoveId from wire; throws StateCodecError on unknown.
sts2::game::MoveId map_move_id(std::string_view move_id,
                               sts2::game::MoveId fallback) {
  sts2::game::MoveId id = fallback;
  if (sts2::game::move_calc::try_move_id_from_wire_id(move_id, id)) {
    return id;
  }
  throw StateCodecError("Nibbit: unknown MoveId: " + std::string(move_id));
}

sts2::ai::EnemyState project_nibbit_slot(const ParsedCreature& cr,
                                         sts2::game::MoveId fallback_move) {
  const sts2::game::MoveId current_move =
      cr.intent_present ? map_move_id(cr.intent.move_id, fallback_move)
                        : fallback_move;
  const uint8_t midx = sts2::game::monster_moves::find_move_index(
      sts2::game::MonsterKind::kNibbit, current_move);

  sts2::ai::EnemyStateBuilder builder;
  builder.alive(cr.current_hp > 0)
      .hp(sts2::game::Stat{cr.current_hp})
      .block(sts2::game::Stat{cr.block})
      .strength(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(cr, "Strength"))})
      .weak(sts2::game::Stat{static_cast<int>(parsed_power_stacks(cr, "Weak"))})
      .kind(sts2::game::MonsterKind::kNibbit)
      .current_move(current_move)
      .move_index(midx)
      .performed_first_move(false);

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
  }
}

}  // namespace

bool is_nibbits_normal(const ParsedCombatState& s) noexcept {
  if (s.enemy_count != 2 || s.enemies.size() != 2U) {
    return false;
  }
  if (s.enemies[0].name != kNibbitWireName ||
      s.enemies[1].name != kNibbitWireName) {
    return false;
  }
  // Both must be alive.
  return s.enemies[0].current_hp > 0 && s.enemies[1].current_hp > 0;
}

sts2::ai::CompactState project_nibbits_normal(const ParsedCombatState& s) {
  assert(is_nibbits_normal(s));

  sts2::ai::CompactStateBuilder builder;
  builder.player_hp(sts2::game::Stat{s.player.current_hp})
      .player_block(sts2::game::Stat{s.player.block})
      .player_strength(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(s.player, "Strength"))})
      .player_weak(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(s.player, "Weak"))})
      .energy(sts2::game::Stat{s.energy})
      .round(std::max(1, s.turn_counter))
      .phase(sts2::ai::Phase::kPlayerActing);

  // Slot 0: front Nibbit (SLICE_MOVE per Q1 fixture 08).
  builder.enemy(
      0, project_nibbit_slot(s.enemies[0], sts2::game::MoveId::kSliceMove));
  // Slot 1: back Nibbit (HISS_MOVE per Q1 fixture 08).
  builder.enemy(
      1, project_nibbit_slot(s.enemies[1], sts2::game::MoveId::kHissMove));

  sts2::ai::CardCounts hand;
  sts2::ai::CardCounts draw;
  sts2::ai::CardCounts discard;
  tally_pile(hand, s.hand_pile);
  tally_pile(draw, s.draw_pile);
  tally_pile(discard, s.discard_pile);
  builder.hand(hand).draw(draw).discard(discard);

  return builder.build();
}

}  // namespace sts2::oracle::adapter

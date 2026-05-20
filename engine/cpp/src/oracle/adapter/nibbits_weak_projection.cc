#include "sts2/oracle/adapter/nibbits_weak_projection.h"

#include <algorithm>
#include <cassert>
#include <cstdint>
#include <string>
#include <string_view>

#include "sts2/ai/state.h"
#include "sts2/ai/state_builders.h"
#include "sts2/game/card_effects.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/stat.h"
#include "sts2/game/types.h"
#include "sts2/oracle/adapter/project_powers.h"
#include "sts2/oracle/adapter/state_blob.h"

// NIBBITS_WEAK projection. Q1-wire Name is pinned by
//   engine/headless/.../NibbitsWeak encounter + Nibbit.cs:
// Single-Nibbit encounter. Initial move: BUTT_MOVE (IsAlone=true default,
// initial_move_index=0). No spawn powers. Source fixture:
// 07-nibbits-weak-seed42.

namespace sts2::oracle::adapter {

namespace {

constexpr std::string_view kNibbitWireName = "Nibbit";

// Map MoveId from wire; throws StateCodecError on unknown.
sts2::game::MoveId map_move_id(std::string_view move_id) {
  sts2::game::MoveId id = sts2::game::MoveId::kButtMove;
  if (sts2::game::move_calc::try_move_id_from_wire_id(move_id, id)) {
    return id;
  }
  throw StateCodecError("Nibbit: unknown MoveId: " + std::string(move_id));
}

sts2::ai::EnemyState project_nibbit(const ParsedCreature& cr) {
  const sts2::game::MoveId current_move = cr.intent_present
                                              ? map_move_id(cr.intent.move_id)
                                              : sts2::game::MoveId::kButtMove;
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

bool is_nibbits_weak(const ParsedCombatState& s) noexcept {
  if (s.enemy_count != 1 || s.enemies.size() != 1U) {
    return false;
  }
  const ParsedCreature& e = s.enemies[0];
  if (e.name != kNibbitWireName) {
    return false;
  }
  return e.current_hp > 0;
}

sts2::ai::CompactState project_nibbits_weak(const ParsedCombatState& s) {
  assert(is_nibbits_weak(s));

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

  builder.enemy(0, project_nibbit(s.enemies[0]));

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

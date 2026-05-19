#include "sts2/oracle/adapter/gremlin_merc_projection.h"

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

// GREMLIN_MERC_NORMAL projection. Q1-wire Name is pinned by
//   engine/headless/.../GremlinMerc.cs (GremlinMerc::CanonicalId =
//   "GremlinMerc").
// Single-GremlinMerc encounter. Initial move: GIMME_MOVE (initial_move_index=0
// per GremlinMerc.cs:70). Source fixture: 09-gremlin-merc-normal-seed42.
//
// Power projection:
//   - SurprisePower(1) wire → kSurprise(1) (recognized; projected).
//   - ThieveryPower(20) wire → UNRECOGNIZED → silent-drop (Q2-ADR-005).
//
// B1 decision: fixture 09 does NOT emit next_spawn_hps; B1 medians used
// (SneakyGremlin=12, FatGremlin=15) — baked in M.β kSurpriseSpawnTable.

namespace sts2::oracle::adapter {

namespace {

constexpr std::string_view kGremlinMercWireName = "GremlinMerc";

// Q1-wire PowerInstance.ModelId for the recognized power.
constexpr std::string_view kSurprisePowerWireId = "SurprisePower";

// Map MoveId from wire; throws StateCodecError on unknown.
sts2::game::MoveId map_move_id(std::string_view move_id) {
  sts2::game::MoveId id = sts2::game::MoveId::kGimmeMove;
  if (sts2::game::move_calc::try_move_id_from_wire_id(move_id, id)) {
    return id;
  }
  throw StateCodecError("GremlinMerc: unknown MoveId: " + std::string(move_id));
}

sts2::ai::EnemyState project_gremlin_merc(const ParsedCreature& cr) {
  const sts2::game::MoveId current_move = cr.intent_present
                                              ? map_move_id(cr.intent.move_id)
                                              : sts2::game::MoveId::kGimmeMove;
  const uint8_t midx = sts2::game::monster_moves::find_move_index(
      sts2::game::MonsterKind::kGremlinMerc, current_move);

  sts2::ai::EnemyStateBuilder builder;
  builder.alive(cr.current_hp > 0)
      .hp(sts2::game::Stat{cr.current_hp})
      .block(sts2::game::Stat{cr.block})
      .strength(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(cr, "Strength"))})
      .weak(sts2::game::Stat{static_cast<int>(parsed_power_stacks(cr, "Weak"))})
      .kind(sts2::game::MonsterKind::kGremlinMerc)
      .current_move(current_move)
      .move_index(midx)
      .performed_first_move(false);

  // SurprisePower: recognized power → project as kSurprise.
  // ThieveryPower: UNRECOGNIZED → silent-drop (Q2-ADR-005 unknown-power
  // infrastructure handles it; no explicit kThievery branch added).
  const int32_t surprise_stacks = parsed_power_stacks(cr, kSurprisePowerWireId);
  if (surprise_stacks > 0) {
    builder.add_power(sts2::game::PowerKind::kSurprise, surprise_stacks);
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
  }
}

}  // namespace

bool is_gremlin_merc_normal(const ParsedCombatState& s) noexcept {
  if (s.enemy_count != 1 || s.enemies.size() != 1U) {
    return false;
  }
  const ParsedCreature& e = s.enemies[0];
  if (e.name != kGremlinMercWireName) {
    return false;
  }
  return e.current_hp > 0;
}

sts2::ai::CompactState project_gremlin_merc_normal(const ParsedCombatState& s) {
  assert(is_gremlin_merc_normal(s));

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

  builder.enemy(0, project_gremlin_merc(s.enemies[0]));

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

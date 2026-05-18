#include "sts2/oracle/adapter/small_slimes_projection.h"

#include <algorithm>
#include <array>
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

// SMALL_SLIMES (SlimesWeak) projection. Wire names pinned by upstream
//   src/Core/Models/Encounters/SlimesWeak.cs:48-59
// 2x NextItem RNG produces 2x2 = 4 spawn variants; unique sorted multi-sets:
//   Leaf-medium: {LeafSlimeM, LeafSlimeS, TwigSlimeS}
//   Twig-medium: {LeafSlimeS, TwigSlimeM, TwigSlimeS}

namespace sts2::oracle::adapter {

namespace {

// Wire Creature.Name strings for the 4 slime kinds in SmallSlimes.
constexpr std::string_view kWireLeafSlimeS = "LeafSlimeS";
constexpr std::string_view kWireLeafSlimeM = "LeafSlimeM";
constexpr std::string_view kWireTwigSlimeS = "TwigSlimeS";
constexpr std::string_view kWireTwigSlimeM = "TwigSlimeM";

// Sorted-alphabetical wire name multi-sets for the two SlimesWeak variants.
// Sorted: LeafSlimeM < LeafSlimeS < TwigSlimeS (Leaf-medium).
//         LeafSlimeS < TwigSlimeM < TwigSlimeS (Twig-medium).
using NameSet3 = std::array<std::string_view, 3>;

constexpr NameSet3 kLeafMediumSet = {
    kWireLeafSlimeM,
    kWireLeafSlimeS,
    kWireTwigSlimeS,
};
constexpr NameSet3 kTwigMediumSet = {
    kWireLeafSlimeS,
    kWireTwigSlimeM,
    kWireTwigSlimeS,
};

// Map wire name to MonsterKind. Throws StateCodecError on unknown.
sts2::game::MonsterKind kind_from_wire_name(std::string_view name) {
  if (name == kWireLeafSlimeS) {
    return sts2::game::MonsterKind::kLeafSlimeS;
  }
  if (name == kWireLeafSlimeM) {
    return sts2::game::MonsterKind::kLeafSlimeM;
  }
  if (name == kWireTwigSlimeS) {
    return sts2::game::MonsterKind::kTwigSlimeS;
  }
  if (name == kWireTwigSlimeM) {
    return sts2::game::MonsterKind::kTwigSlimeM;
  }
  throw StateCodecError("SmallSlimes: unknown slime Creature.Name: " +
                        std::string(name));
}

// Default move per kind when wire intent is absent.
sts2::game::MoveId default_move_for_kind(
    sts2::game::MonsterKind kind) noexcept {
  using sts2::game::MonsterKind;
  using sts2::game::MoveId;
  switch (kind) {
    case MonsterKind::kLeafSlimeS:
    case MonsterKind::kTwigSlimeS:
      return MoveId::kTackleMove;
    case MonsterKind::kLeafSlimeM:
      return MoveId::kClumpShot;
    case MonsterKind::kTwigSlimeM:
      return MoveId::kStickyShot;
    default:
      return MoveId::kTackleMove;
  }
}

// Map wire MoveId string for a slime. Throws on unknown.
sts2::game::MoveId map_slime_move_id(std::string_view move_id) {
  sts2::game::MoveId id = sts2::game::MoveId::kTackleMove;
  if (sts2::game::move_calc::try_move_id_from_wire_id(move_id, id)) {
    return id;
  }
  throw StateCodecError("SmallSlimes: unknown MoveId: " + std::string(move_id));
}

// Map move_index within the slime's move table for the given MoveId.
uint8_t move_index_for(sts2::game::MonsterKind kind,
                       sts2::game::MoveId move) noexcept {
  using sts2::game::MonsterKind;
  using sts2::game::MoveId;
  // Table indices per wave-22.β monster_moves.cc (moves are ordered:
  // LeafSlimeS/TwigSlimeS: [0]=TACKLE, [1]=GOOP/STICKY_SHOT
  // LeafSlimeM:            [0]=CLUMP_SHOT, [1]=STICKY_SHOT
  // TwigSlimeM:            [0]=STICKY_SHOT, [1]=POKEY_POUNCE
  switch (kind) {
    case MonsterKind::kLeafSlimeS:
      return (move == MoveId::kGoopMove) ? 1U : 0U;
    case MonsterKind::kTwigSlimeS:
      return (move == MoveId::kStickyShot) ? 1U : 0U;
    case MonsterKind::kLeafSlimeM:
      return (move == MoveId::kStickyShot) ? 1U : 0U;
    case MonsterKind::kTwigSlimeM:
      return (move == MoveId::kPokeyPounce) ? 1U : 0U;
    default:
      return 0U;
  }
}

sts2::ai::EnemyState project_slime(const ParsedCreature& cr) {
  const sts2::game::MonsterKind kind = kind_from_wire_name(cr.name);
  const sts2::game::MoveId current_move =
      cr.intent_present ? map_slime_move_id(cr.intent.move_id)
                        : default_move_for_kind(kind);
  const uint8_t midx = move_index_for(kind, current_move);

  sts2::ai::EnemyStateBuilder builder;
  builder.alive(cr.current_hp > 0)
      .hp(sts2::game::Stat{cr.current_hp})
      .block(sts2::game::Stat{cr.block})
      .strength(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(cr, "Strength"))})
      .weak(sts2::game::Stat{static_cast<int>(parsed_power_stacks(cr, "Weak"))})
      .kind(kind)
      .current_move(current_move)
      .move_index(midx)
      .performed_first_move(false);

  // Slimes have no spawn powers per upstream (no AfterAddedToRoom power
  // application). Handle Frail for completeness (not expected at initial boot).
  const int32_t wire_frail = parsed_power_stacks(cr, "Frail");
  if (wire_frail > 0) {
    builder.add_power(sts2::game::PowerKind::kFrail,
                      static_cast<int16_t>(wire_frail));
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
    // Unknown card model ids are silently skipped at the adapter level.
  }
}

}  // namespace

bool is_small_slimes(const ParsedCombatState& combat) {
  if (combat.enemy_count != 3 || combat.enemies.size() != 3U) {
    return false;
  }
  // All 3 must be alive.
  if (!std::all_of(combat.enemies.begin(), combat.enemies.end(),
                   [](const ParsedCreature& e) { return e.current_hp > 0; })) {
    return false;
  }
  // Collect sorted names and match against known variants.
  std::array<std::string_view, 3> names = {
      combat.enemies[0].name,
      combat.enemies[1].name,
      combat.enemies[2].name,
  };
  std::sort(names.begin(), names.end());

  return names == kLeafMediumSet || names == kTwigMediumSet;
}

sts2::ai::CompactState project_small_slimes(const ParsedCombatState& combat) {
  assert(is_small_slimes(combat));

  sts2::ai::CompactStateBuilder builder;
  builder.player_hp(sts2::game::Stat{combat.player.current_hp})
      .player_block(sts2::game::Stat{combat.player.block})
      .player_strength(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(combat.player, "Strength"))})
      .player_weak(sts2::game::Stat{
          static_cast<int>(parsed_power_stacks(combat.player, "Weak"))})
      .energy(sts2::game::Stat{combat.energy})
      .round(static_cast<std::uint16_t>(std::max(1, combat.turn_counter)))
      .phase(sts2::ai::Phase::kPlayerActing);

  for (std::size_t i = 0; i < 3U; ++i) {
    builder.enemy(i, project_slime(combat.enemies[i]));
  }

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

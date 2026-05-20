#pragma once

#include <array>
#include <cstdint>
#include <string_view>
#include <utility>

#include "sts2/game/monster_moves.h"
#include "sts2/game/types.h"

// Canonical enemy move + ritual-tick primitives, shared by the production
// engine (src/game/enemies.cc, src/game/powers.cc) and the AI transition
// simulator (src/ai/transition.cc) to prevent silent divergence.

namespace sts2::game::move_calc {

// Primary mapping: every MoveId has exactly one canonical wire name.
// Order MUST match kAllMoveIds (types.h); static_assert below enforces.
// Sources: upstream .cs MoveState name strings per encounter.
inline constexpr std::array<std::pair<MoveId, std::string_view>,
                            kMoveIdCardinality>
    kMoveWireNames = {{
        {MoveId::kIncantation, "INCANTATION_MOVE"},
        {MoveId::kDarkStrike, "DARK_STRIKE_MOVE"},
        // Wave-18: LouseProgenitor moves.
        {MoveId::kWebCannon, "WEB_CANNON_MOVE"},
        {MoveId::kCurlAndGrow, "CURL_AND_GROW_MOVE"},
        {MoveId::kPounce, "POUNCE_MOVE"},
        // Wave-21: slime moves (canonical forward wire names per upstream .cs).
        // kTackleMove: "TACKLE_MOVE" (LeafSlimeS.cs:31, TwigSlimeS.cs:26).
        {MoveId::kTackleMove, "TACKLE_MOVE"},
        // kGoopMove: "GOOP_MOVE" (LeafSlimeS.cs:32).
        {MoveId::kGoopMove, "GOOP_MOVE"},
        // kClumpShot: "CLUMP_SHOT" (LeafSlimeM.cs:33).
        {MoveId::kClumpShot, "CLUMP_SHOT"},
        // kStickyShot: LeafSlimeM emits "STICKY_SHOT" (LeafSlimeM.cs:34);
        // TwigSlimeM emits "STICKY_SHOT_MOVE" (TwigSlimeM.cs:35). Canonical
        // forward = "STICKY_SHOT"; reverse also accepts "STICKY_SHOT_MOVE"
        // via kMoveWireAliases.
        {MoveId::kStickyShot, "STICKY_SHOT"},
        // kPokeyPounce: "POKEY_POUNCE_MOVE" (TwigSlimeM.cs:34).
        {MoveId::kPokeyPounce, "POKEY_POUNCE_MOVE"},
        // Wave-24/K.β: Nibbit moves (Nibbit.cs:71-73 MoveState names).
        {MoveId::kButtMove, "BUTT_MOVE"},
        {MoveId::kSliceMove, "SLICE_MOVE"},
        {MoveId::kHissMove, "HISS_MOVE"},
    }};

static_assert(kMoveWireNames.size() == kMoveIdCardinality,
              "kMoveWireNames size must match kMoveIdCardinality");

// Alias wire names that map TO a MoveId but NOT FROM. Currently only the
// TwigSlimeM-emitted "STICKY_SHOT_MOVE" string, which round-trips to
// MoveId::kStickyShot (kMoveWireNames already gives the canonical
// forward "STICKY_SHOT").
inline constexpr std::array<std::pair<std::string_view, MoveId>, 1>
    kMoveWireAliases = {{
        {"STICKY_SHOT_MOVE", MoveId::kStickyShot},
    }};

[[nodiscard]] constexpr std::string_view move_wire_id(MoveId id) noexcept {
  for (const auto& [m, name] : kMoveWireNames) {
    if (m == id) {
      return name;
    }
  }
  return "";
}

[[nodiscard]] constexpr bool try_move_id_from_wire_id(std::string_view wire_id,
                                                      MoveId& out) noexcept {
  for (const auto& [m, name] : kMoveWireNames) {
    if (name == wire_id) {
      out = m;
      return true;
    }
  }
  for (const auto& [alias, m] : kMoveWireAliases) {
    if (alias == wire_id) {
      out = m;
      return true;
    }
  }
  return false;
}

[[nodiscard]] constexpr MoveId move_id_from_wire_id(
    std::string_view wire_id) noexcept {
  MoveId out = MoveId::kIncantation;
  (void)try_move_id_from_wire_id(wire_id, out);
  return out;
}

// Advance enemy intent through its move sequence. kIncantation -> kDarkStrike;
// other moves are stable. For table-driven enemies, use advance_intent_table
// instead.
[[nodiscard]] inline MoveId next_move(MoveId current) noexcept {
  if (current == MoveId::kIncantation) {
    return MoveId::kDarkStrike;
  }
  return current;
}

// Ritual tick decision. Mutates just_applied to false. Returns true iff this
// tick should grant Strength (the just-applied turn skips the gain).
[[nodiscard]] inline bool ritual_should_grant_strength(
    bool& just_applied) noexcept {
  if (just_applied) {
    just_applied = false;
    return false;
  }
  return true;
}

// Advance the enemy's intent state for the next turn (table-driven).
// Uses follow_up_index from the monster's move table to determine the next
// move index and looks up the corresponding MoveId.
// On the first call (performed_first_move == false) marks performed without
// changing move; on subsequent calls advances via follow_up_index.
inline void advance_intent_table(
    bool& performed_first_move, MoveId& current_move, uint8_t& move_index,
    const monster_moves::MonsterMoveTable& table) noexcept {
  if (!performed_first_move) {
    performed_first_move = true;
    return;
  }
  // Follow up_index into the next move.
  const uint8_t next_idx = table.moves[move_index].follow_up_index;
  move_index = next_idx;
  current_move = table.moves[next_idx].id;
}

// Advance the enemy's intent state for the next turn. On the first call
// (performed_first_move == false) marks the intent as performed without
// changing it; on subsequent calls advances current_move via next_move().
// For cultist-style enemies; table-driven enemies use advance_intent_table.
inline void advance_intent(bool& performed_first_move,
                           MoveId& current_move) noexcept {
  if (!performed_first_move) {
    performed_first_move = true;
    return;
  }
  current_move = next_move(current_move);
}

// Dispatch the enemy's intent to per-effect callables. Sharing this switch
// is the load-bearing T18c guarantee: adding a new MoveId is a one-place
// header change. Each layer supplies lambdas that perform its own effects.
template <typename OnRitual, typename OnDarkStrike>
void act_on_intent(
    MoveId move, OnRitual&& on_ritual,
    OnDarkStrike&& on_dark_strike) noexcept(noexcept(on_ritual()) &&
                                            noexcept(on_dark_strike())) {
  // NOTE: adding a MoveId here is not a compile-time call-site signal —
  // grep act_on_intent users to verify they handle the new case.
  switch (move) {
    case MoveId::kIncantation:
      std::forward<OnRitual>(on_ritual)();
      break;
    case MoveId::kDarkStrike:
      std::forward<OnDarkStrike>(on_dark_strike)();
      break;
    // LouseProgenitor + slime + Nibbit moves are dispatched via
    // act_on_table_move.
    case MoveId::kWebCannon:
    case MoveId::kCurlAndGrow:
    case MoveId::kPounce:
    case MoveId::kTackleMove:
    case MoveId::kGoopMove:
    case MoveId::kClumpShot:
    case MoveId::kStickyShot:
    case MoveId::kPokeyPounce:
    // Wave-24/K.β: Nibbit moves.
    case MoveId::kButtMove:
    case MoveId::kSliceMove:
    case MoveId::kHissMove:
      break;
  }
}

}  // namespace sts2::game::move_calc

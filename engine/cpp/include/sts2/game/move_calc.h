#pragma once

#include <cstdint>
#include <string_view>
#include <utility>

#include "sts2/game/monster_moves.h"
#include "sts2/game/types.h"

// Canonical enemy move + ritual-tick primitives, shared by the production
// engine (src/game/enemies.cc, src/game/powers.cc) and the AI transition
// simulator (src/ai/transition.cc) to prevent silent divergence.

namespace sts2::game::move_calc {

[[nodiscard]] constexpr std::string_view move_wire_id(MoveId id) noexcept {
  switch (id) {
    case MoveId::kIncantation:
      return "INCANTATION_MOVE";
    case MoveId::kDarkStrike:
      return "DARK_STRIKE_MOVE";
    // Wave-18: LouseProgenitor moves.
    case MoveId::kWebCannon:
      return "WEB_CANNON_MOVE";
    case MoveId::kCurlAndGrow:
      return "CURL_AND_GROW_MOVE";
    case MoveId::kPounce:
      return "POUNCE_MOVE";
    // Wave-21: slime moves (canonical forward wire names per upstream .cs).
    // kTackleMove: "TACKLE_MOVE" (LeafSlimeS.cs:31, TwigSlimeS.cs:26).
    case MoveId::kTackleMove:
      return "TACKLE_MOVE";
    // kGoopMove: "GOOP_MOVE" (LeafSlimeS.cs:32).
    case MoveId::kGoopMove:
      return "GOOP_MOVE";
    // kClumpShot: "CLUMP_SHOT" (LeafSlimeM.cs:33).
    case MoveId::kClumpShot:
      return "CLUMP_SHOT";
    // kStickyShot: LeafSlimeM emits "STICKY_SHOT" (LeafSlimeM.cs:34);
    // TwigSlimeM emits "STICKY_SHOT_MOVE" (TwigSlimeM.cs:35). Canonical
    // forward = "STICKY_SHOT"; reverse also accepts "STICKY_SHOT_MOVE".
    case MoveId::kStickyShot:
      return "STICKY_SHOT";
    // kPokeyPounce: "POKEY_POUNCE_MOVE" (TwigSlimeM.cs:34).
    case MoveId::kPokeyPounce:
      return "POKEY_POUNCE_MOVE";
    // Wave-24/K.β: Nibbit moves (Nibbit.cs:71-73 MoveState names).
    case MoveId::kButtMove:
      return "BUTT_MOVE";
    case MoveId::kSliceMove:
      return "SLICE_MOVE";
    case MoveId::kHissMove:
      return "HISS_MOVE";
    // Wave-26/M.α APPEND-ONLY: GremlinMerc moves. Wire-name canonicalization
    // (cross-checked against upstream .cs files) lands in wave-26/M.β; M.α
    // ships placeholder names matching the MoveId identifiers so the switch
    // is total and -Werror=switch passes. M.β replaces these with the
    // upstream canonical names + completes try_move_id_from_wire_id below.
    case MoveId::kGimmeMove:
      return "GIMME_MOVE";
    case MoveId::kDoubleSmashMove:
      return "DOUBLE_SMASH_MOVE";
    case MoveId::kHeheMove:
      return "HEHE_MOVE";
    case MoveId::kSpawnedMove:
      return "SPAWNED_MOVE";
    case MoveId::kFleeMove:
      return "FLEE_MOVE";
  }
  return "";
}

[[nodiscard]] constexpr bool try_move_id_from_wire_id(std::string_view wire_id,
                                                      MoveId& out) noexcept {
  if (wire_id == "INCANTATION_MOVE") {
    out = MoveId::kIncantation;
    return true;
  }
  if (wire_id == "DARK_STRIKE_MOVE") {
    out = MoveId::kDarkStrike;
    return true;
  }
  if (wire_id == "WEB_CANNON_MOVE") {
    out = MoveId::kWebCannon;
    return true;
  }
  if (wire_id == "CURL_AND_GROW_MOVE") {
    out = MoveId::kCurlAndGrow;
    return true;
  }
  if (wire_id == "POUNCE_MOVE") {
    out = MoveId::kPounce;
    return true;
  }
  // Wave-21: slime moves.
  if (wire_id == "TACKLE_MOVE") {
    out = MoveId::kTackleMove;
    return true;
  }
  if (wire_id == "GOOP_MOVE") {
    out = MoveId::kGoopMove;
    return true;
  }
  if (wire_id == "CLUMP_SHOT") {
    out = MoveId::kClumpShot;
    return true;
  }
  // Accept both upstream variants: LeafSlimeM→"STICKY_SHOT",
  // TwigSlimeM→"STICKY_SHOT_MOVE".
  if (wire_id == "STICKY_SHOT" || wire_id == "STICKY_SHOT_MOVE") {
    out = MoveId::kStickyShot;
    return true;
  }
  if (wire_id == "POKEY_POUNCE_MOVE") {
    out = MoveId::kPokeyPounce;
    return true;
  }
  // Wave-24/K.β: Nibbit moves (Nibbit.cs:71-73 MoveState canonical names).
  if (wire_id == "BUTT_MOVE") {
    out = MoveId::kButtMove;
    return true;
  }
  if (wire_id == "SLICE_MOVE") {
    out = MoveId::kSliceMove;
    return true;
  }
  if (wire_id == "HISS_MOVE") {
    out = MoveId::kHissMove;
    return true;
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
    // Wave-26/M.α: GremlinMerc moves — table-driven dispatch via
    // do_enemy_act_slime / act_on_table_move; no cultist hardcoded path.
    case MoveId::kGimmeMove:
    case MoveId::kDoubleSmashMove:
    case MoveId::kHeheMove:
    case MoveId::kSpawnedMove:
    case MoveId::kFleeMove:
      break;
  }
}

}  // namespace sts2::game::move_calc

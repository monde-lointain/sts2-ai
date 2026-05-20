#pragma once

#include "sts2/ai/state.h"
#include "sts2/oracle/adapter/state_blob.h"

// CULTISTS_NORMAL encounter detection + projection onto Q2's CompactState
// (per Q2-ADR-002). The encounter is identified by the wire's Creature.Name
// strings on alive enemies: exactly the (CalcifiedCultist, DampCultist) pair
// in either slot order. Other encounters hit the reject path (T4).

namespace sts2::oracle::adapter {

// Returns true iff the parsed CombatState carries exactly two enemies whose
// Q1-wire Creature.Name strings are the CULTISTS_NORMAL pair in either
// order. Encounter signature only; HP / move / power state is intentionally
// not consulted here (the projection consumes those).
[[nodiscard]] bool is_cultists_normal(const ParsedCombatState& combat);

// Projects a CULTISTS_NORMAL ParsedCombatState onto a CompactState. UB if
// !is_cultists_normal(combat); callers must gate via is_cultists_normal or
// the adapter facade.
//
// Field mapping notes:
//  - Player HP/block come from combat.player Creature; strength/weak read
//    off Creature.powers (none on Silent at smoke fixture boot).
//  - Energy from combat.energy; phase forced to kPlayerActing (Phase-1A
//    starter snapshot); round = max(1, turn_counter) so Q1's pre-first-
//    action turn=1 maps to round=1 (Ring-of-the-Snake 7-card draw).
//  - Enemy slot order preserved from the wire — Q1 emits Calcified first,
//    Damp second per the fixture dump, but we accept either order.
//  - performed_first_move = false; just_applied_ritual set via
//    sts2::ai::powers::set_just_applied_ritual when the wire's Ritual power
//    has just_applied=true; current_move = kIncantation (matches the wire's
//    INCANTATION_MOVE on the first turn).
//  - Card pile projection: ModelId strings -> CardId enum via
//    map_card_model_id. Unknown ModelIds throw StateCodecError (Phase-1A
//    Silent starter deck is enumerable: StrikeSilent, DefendSilent,
//    Neutralize, Survivor).
[[nodiscard]] sts2::ai::CompactState project_cultists_normal(
    const ParsedCombatState& combat);

}  // namespace sts2::oracle::adapter

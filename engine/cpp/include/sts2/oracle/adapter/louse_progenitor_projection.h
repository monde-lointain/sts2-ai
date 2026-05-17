#pragma once

#include "sts2/ai/state.h"
#include "sts2/oracle/adapter/state_blob.h"

// LOUSE_PROGENITOR_NORMAL encounter detection + projection onto Q2's
// CompactState (wave-18). The encounter is identified by a single enemy
// with wire Creature.Name == "LouseProgenitor".
//
// HP range: 134-136 (A0). Initial move: WEB_CANNON (move_index=0).
// Spawn power: CurlUp(14) — synthesized if absent from the wire blob per
// the Q2-ADR-005 silent-drop pattern (Q1 may omit spawn powers at boot).

namespace sts2::oracle::adapter {

// Returns true iff the parsed CombatState carries exactly one enemy whose
// Q1-wire Creature.Name is "LouseProgenitor" and it is alive (HP > 0).
[[nodiscard]] bool is_louse_progenitor_normal(const ParsedCombatState& combat);

// Projects a LOUSE_PROGENITOR_NORMAL ParsedCombatState onto a CompactState.
// UB if !is_louse_progenitor_normal(combat); callers must gate via the
// detection function or the adapter facade.
//
// Field mapping notes:
//  - enemy_count_ = 1 (solo encounter).
//  - kind_ = kLouseProgenitor, initial move kWebCannon at move_index=0.
//  - HP/block from wire; powers from wire via project_powers helper.
//  - CurlUp(14) synthesized if wire blob omits it (silent-drop pattern).
//  - dark_strike_base_ and ritual_amount_ left at default (0); unused for
//    LouseProgenitor whose moves are purely table-driven.
//  - performed_first_move = false (pre-first-action snapshot).
[[nodiscard]] sts2::ai::CompactState project_louse_progenitor_normal(
    const ParsedCombatState& combat);

}  // namespace sts2::oracle::adapter

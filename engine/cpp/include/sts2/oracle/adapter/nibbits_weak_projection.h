#pragma once

#include "sts2/ai/state.h"
#include "sts2/oracle/adapter/state_blob.h"

// NIBBITS_WEAK encounter detection + projection onto Q2's CompactState
// (wave-24/K.γ_setup). The encounter is identified by a single enemy with
// wire Creature.Name == "Nibbit". Initial move: BUTT_MOVE (IsAlone=true per
// Q1 fixture 07, Nibbit.cs initial_move_index=0 default).
//
// HP range: 42-46 (A0). No spawn powers.

namespace sts2::oracle::adapter {

// Returns true iff the parsed CombatState carries exactly one enemy whose
// Q1-wire Creature.Name is "Nibbit" and it is alive (HP > 0).
[[nodiscard]] bool is_nibbits_weak(const ParsedCombatState& s) noexcept;

// Projects a NIBBITS_WEAK ParsedCombatState onto a CompactState.
// HP read directly from wire (NO re-rolling); initial move read from wire
// current_move (Q1 emits BUTT_MOVE per upstream IsAlone=true).
// UB if !is_nibbits_weak(s); callers must gate via is_nibbits_weak or the
// adapter facade.
[[nodiscard]] sts2::ai::CompactState project_nibbits_weak(
    const ParsedCombatState& s);

}  // namespace sts2::oracle::adapter

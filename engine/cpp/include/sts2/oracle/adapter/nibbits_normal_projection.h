#pragma once

#include "sts2/ai/state.h"
#include "sts2/oracle/adapter/state_blob.h"

// NIBBITS_NORMAL encounter detection + projection onto Q2's CompactState
// (wave-24/K.γ_setup). The encounter is identified by exactly two enemies
// both with wire Creature.Name == "Nibbit". Per Q1 fixture 08:
//   slot 0 (front Nibbit): initial move SLICE_MOVE
//   slot 1 (back Nibbit):  initial move HISS_MOVE
//
// HP range per Nibbit: 42-46 (A0). No spawn powers.

namespace sts2::oracle::adapter {

// Returns true iff the parsed CombatState carries exactly two enemies both
// with Q1-wire Creature.Name "Nibbit" and both alive (HP > 0).
[[nodiscard]] bool is_nibbits_normal(const ParsedCombatState& s) noexcept;

// Projects a NIBBITS_NORMAL ParsedCombatState onto a CompactState with
// enemy_count=2. Slot order preserved from wire (slot 0 = front, slot 1 =
// back). HP read per slot from wire. Initial moves read from wire per slot.
// UB if !is_nibbits_normal(s); callers must gate via is_nibbits_normal or
// the adapter facade.
[[nodiscard]] sts2::ai::CompactState project_nibbits_normal(
    const ParsedCombatState& s);

}  // namespace sts2::oracle::adapter

#pragma once

#include "sts2/ai/state.h"
#include "sts2/oracle/adapter/state_blob.h"

// SMALL_SLIMES (SlimesWeak) encounter detection + projection onto Q2's
// CompactState (wave-22.γ). The encounter is identified by a 3-enemy sorted
// multi-set matching one of 2 wire signatures per upstream SlimesWeak.cs:48-59:
//
//   Leaf-medium variant : {LeafSlimeM, LeafSlimeS, TwigSlimeS}
//   Twig-medium variant : {LeafSlimeS, TwigSlimeM, TwigSlimeS}
//
// Wire names are sorted alphabetically; encounter_map holds both entries
// mapping to encounter_id="SmallSlimes".
//
// HP at A0: LeafSlimeS 8-12, LeafSlimeM 25-30, TwigSlimeS 8-12, TwigSlimeM
// 28-32. Spawn powers: none (slimes have no spawn powers per upstream).

namespace sts2::oracle::adapter {

// Returns true iff the parsed CombatState carries exactly 3 enemies whose
// sorted wire-name multi-set matches a SlimesWeak wire signature (either
// medium variant), all alive (HP > 0).
[[nodiscard]] bool is_small_slimes(const ParsedCombatState& combat);

// Projects a SMALL_SLIMES ParsedCombatState onto a CompactState.
// UB if !is_small_slimes(combat); callers must gate via the detection
// function or the adapter facade.
//
// Field mapping notes:
//  - enemy_count = 3.
//  - MonsterKind assigned per wire Creature.Name.
//  - HP/block from wire; current_move from wire intent (table-driven move
//    lookup; defaults to kTackleMove for small slimes if intent absent).
//  - No spawn powers (slimes have no AfterAddedToRoom spawn powers).
//  - Slimed cards in piles: projected via standard tally_pile; should be 0
//    at initial combat state (fixture #6 is pre-first-action).
//  - performed_first_move = false (pre-first-action snapshot).
[[nodiscard]] sts2::ai::CompactState project_small_slimes(
    const ParsedCombatState& combat);

}  // namespace sts2::oracle::adapter

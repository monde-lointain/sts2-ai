#pragma once

#include "sts2/ai/state.h"
#include "sts2/oracle/adapter/state_blob.h"

// GREMLIN_MERC_NORMAL encounter detection + projection onto Q2's CompactState
// (wave-26/M.γ). The encounter is identified by a single enemy with
// wire Creature.Name == "GremlinMerc" and alive == true.
//
// HP range: 47-49 (A0). Initial move: GIMME_MOVE (GremlinMerc.cs:70,
// initial_move_index=0). Spawn powers: SurprisePower(1) — projected as
// kSurprise(1). ThieveryPower(20) is DROPPED at the data layer (Q2
// combat-only; kThievery is UNRECOGNIZED → silent-drop via Q2-ADR-005
// unknown-power infrastructure).
//
// B1 decision: fixture 09 does NOT emit next_spawn_hps; B1 medians used
// (SneakyGremlin HP=12, FatGremlin HP=15) per M.β kSurpriseSpawnTable.

namespace sts2::oracle::adapter {

// Returns true iff the parsed CombatState carries exactly one enemy whose
// Q1-wire Creature.Name is "GremlinMerc" and it is alive (HP > 0).
[[nodiscard]] bool is_gremlin_merc_normal(const ParsedCombatState& s) noexcept;

// Projects a GREMLIN_MERC_NORMAL ParsedCombatState onto a CompactState.
// HP read directly from wire; initial move read from wire current_move
// (Q1 emits GIMME_MOVE per upstream GremlinMerc.cs:70 initial_move_index=0).
// kSurprise(1) projected from SurprisePower wire name; kThievery silent-drop.
// UB if !is_gremlin_merc_normal(s); callers must gate via
// is_gremlin_merc_normal or the adapter facade.
[[nodiscard]] sts2::ai::CompactState project_gremlin_merc_normal(
    const ParsedCombatState& s);

}  // namespace sts2::oracle::adapter

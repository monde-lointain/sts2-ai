#pragma once

#include <cstdint>
#include <optional>
#include <vector>

#include "sts2/ai/state.h"
#include "sts2/game/index_types.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/types.h"

namespace sts2::ai::transition {

enum class ActionKind : uint8_t { kPlayCard, kEndTurn };

struct Action {
  ActionKind kind = ActionKind::kEndTurn;
  sts2::game::CardId card_id = sts2::game::CardId::kNone;
  sts2::game::EnemySlot target_idx = sts2::game::EnemySlot::none();
  sts2::game::CardId survivor_discard_id = sts2::game::CardId::kNone;
  bool operator==(const Action&) const = default;
};

[[nodiscard]] std::vector<Action> legal_actions(const CompactState& state);

[[nodiscard]] std::optional<CompactState> apply_player_action(
    const CompactState& state, const Action& action);

// clang-format off
// Phase transitions for end-of-turn resolution. Sequence to advance state
// across the chance boundary:
//   1. state = *apply_player_action(state, EndTurn)    // T3: phase -> kAtChanceDraw
//   2. state = resolve_end_turn_pre_draw(state)        // T4: enemy phase + start-of-next-turn (no draw)
//   3. enumerate draws from state.get_draw():
//        recurse on apply_draw(state, outcome.hand).
//
// Runs the deterministic part of Combat::end_turn() up to but excluding the
// draw: end_player_turn (hand->discard, tick player powers), enemy_phase
// (zero block, act, tick), round++, roll enemy moves, reset block (round>1),
// refill energy. Leaves phase = kAtChanceDraw. May leave the player dead --
// caller checks is_terminal(state) before drawing.
// clang-format on
[[nodiscard]] CompactState resolve_end_turn_pre_draw(const CompactState& state);

[[nodiscard]] int draw_count(const CompactState& state) noexcept;

// Apply a specific drawn-hand multiset. Drains the multiset from the draw
// pile; if the draw pile lacks any of the requested cards, reshuffles
// discard into draw before draining. After this call,
//   state.get_hand() gains drawn
//   state.get_draw() loses drawn (potentially after a discard->draw reshuffle)
// and state.get_phase() becomes kPlayerActing.
[[nodiscard]] CompactState apply_draw(const CompactState& state,
                                      CardCounts drawn);

[[nodiscard]] bool is_terminal(const CompactState& state) noexcept;

// ---------------------------------------------------------------------------
// Test-only seam (wave-24/K.α). The data-driven MoveEffect dispatch loop in
// do_enemy_act_slime is not directly callable from tests (anonymous
// namespace). Since K.α adds kBuffEnemy + kBlockSelf MoveEffectKinds which
// no existing monster_moves table exercises (Nibbit lands in K.β), tests for
// the new dispatch paths need a seam. This function applies a single
// MoveEffect to (s, e) using the same logic as do_enemy_act_slime's switch.
// NOT for production use; the loop in do_enemy_act_slime is the real path.
// ---------------------------------------------------------------------------
namespace test_internals {
void apply_single_move_effect_for_test(
    CompactState& s, EnemyState& e,
    const sts2::game::monster_moves::MoveEffect& fx) noexcept;

// Drive end-of-enemy-turn block decay path directly (matches the trailing
// `M::set_enemy_block(e, Stat{0})` in do_enemy_act). Tests use this to
// assert decay behavior without needing a monster_moves table entry.
void decay_enemy_block_for_test(EnemyState& e) noexcept;

// Wave-26/M.α — drive apply_damage_to_enemy_with_ondeath_check directly.
// Lets tests verify the OnDeath substrate (alive transition, kSurprise
// removal, do_surprise_spawn dispatch) without routing through a full
// player-action transition. Production path is damage_enemy() in
// transition.cc, called from apply_player_action_in_place for card-sourced
// attacks. dmg is the post-strength/weak-modifier value (caller applies any
// compute_outgoing adjustments).
void apply_damage_to_enemy_with_ondeath_check_for_test(CompactState& s,
                                                       EnemyState& e,
                                                       int dmg) noexcept;

// Wave-26/M.α — drive do_surprise_spawn directly with a caller-provided
// spawn array. Bypasses the kMonsterMoveTables[kind].on_death_spawns lookup
// so tests can verify the spawn mechanic before M.β populates table data.
// The `dead_enemy` is mutated in place: kSurprise PowerInstance removed
// (one-shot enforcement); `s` gains spawn_count new enemies appended at
// indices [enemy_count_, enemy_count_+spawn_count). spawn_count + existing
// enemy_count_ MUST be <= kMaxEnemies (assert).
void apply_surprise_spawn_for_test(
    CompactState& s, EnemyState& dead_enemy,
    const sts2::game::monster_moves::SpawnEntry* spawns,
    uint8_t spawn_count) noexcept;
}  // namespace test_internals

}  // namespace sts2::ai::transition

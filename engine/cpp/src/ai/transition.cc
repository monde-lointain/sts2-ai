#include "sts2/ai/transition.h"

#include <algorithm>
#include <array>
#include <cassert>
#include <cstdint>

#include "sts2/game/card_effects.h"
#include "sts2/game/damage.h"
#include "sts2/game/damage_calc.h"
#include "sts2/game/index_types.h"
#include "sts2/game/monster_moves.h"
#include "sts2/game/move_calc.h"
#include "sts2/game/stat.h"
#include "sts2/game/turn_calc.h"
#include "sts2/game/turn_flow.h"
#include "sts2/game/types.h"

namespace sts2::ai::transition {

namespace detail {

class StateMutator {
 public:
  [[nodiscard]] static sts2::game::Stat& hp(EnemyState& e) noexcept {
    return e.hp_;
  }
  [[nodiscard]] static sts2::game::Stat& block(EnemyState& e) noexcept {
    return e.block_;
  }
  // strength/weak now route through the powers_ array.
  // Wave-23/J.beta: drop the int16_t narrow casts now that PowerInstance.stacks
  // is int32_t (Q2-ADR-014).
  static void add_strength(EnemyState& e, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(e.powers_, e.power_count_,
                      sts2::game::PowerKind::kStrength, delta);
  }
  static void add_weak(EnemyState& e, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(e.powers_, e.power_count_, sts2::game::PowerKind::kWeak,
                      delta);
  }
  static void add_frail_to_enemy(EnemyState& e, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(e.powers_, e.power_count_, sts2::game::PowerKind::kFrail,
                      delta);
  }
  // Generic enemy-power mutator (wave-22.α): data-driven kBuffSelf path uses
  // this for slime move tables. The kStrength / kFrail wrappers above remain
  // for backward-compat with cultist + LouseProgenitor hooks. Wave-24/K.α
  // reuses for kBuffEnemy (Nibbit HISS): GENERIC stack-add, NO Ritual
  // side-effects (just_applied flag untouched; ritual_amount_ unchanged).
  static void add_enemy_power(EnemyState& e, sts2::game::PowerKind kind,
                              int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(e.powers_, e.power_count_, kind, delta);
  }
  // Wave-24/K.α: enemy block accumulation helper for kBlockSelf dispatch.
  // Mirrors player block accumulation (no Frail/dexterity in Phase-1 enemy
  // block; treated as raw add). Block decays at START of each enemy's turn
  // via the existing turn_flow.h::EndTurnOps::reset_enemy_block scaffold
  // (block persists across the player's intervening turn — upstream STS
  // semantics; same path Louse's kCurlAndGrow block uses).
  static void add_enemy_block(EnemyState& e, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    e.block_ += delta;
  }
  // Wave-24/K.α: enemy block assignment helper. Direct setter (no
  // accumulation); test-only seam uses this via
  // test_internals::decay_enemy_block_for_test. The production
  // pre-act decay path is turn_flow.h::EndTurnOps::reset_enemy_block.
  static void set_enemy_block(EnemyState& e, sts2::game::Stat value) noexcept {
    e.block_ = value;
  }
  // Generic remove-power-instance (decrement stacks; remove if <= 0).
  static void decrement_power(EnemyState& e,
                              sts2::game::PowerKind kind) noexcept {
    PowerInstance* p = powers::find_power(e.powers_, e.power_count_, kind);
    if (p == nullptr) {
      return;
    }
    --p->stacks;
    if (p->stacks <= 0) {
      powers::remove_power(e.powers_, e.power_count_, kind);
    }
  }
  static void remove_power(EnemyState& e, sts2::game::PowerKind kind) noexcept {
    powers::remove_power(e.powers_, e.power_count_, kind);
  }
  static void decrement_weak(EnemyState& e) noexcept {
    decrement_power(e, sts2::game::PowerKind::kWeak);
  }
  // Ritual flag manipulation
  [[nodiscard]] static bool get_just_applied_ritual(
      const EnemyState& e) noexcept {
    const PowerInstance* p = powers::find_power(e.powers_, e.power_count_,
                                                sts2::game::PowerKind::kRitual);
    return (p != nullptr) && ((p->flags & 0x01U) != 0);
  }
  static void set_just_applied_ritual(EnemyState& e, bool value) noexcept {
    PowerInstance* p = powers::find_power(e.powers_, e.power_count_,
                                          sts2::game::PowerKind::kRitual);
    if (value) {
      if (p == nullptr) {
        p = &powers::add_power(e.powers_, e.power_count_,
                               sts2::game::PowerKind::kRitual, 0);
      }
      p->flags |= 0x01U;
    } else {
      if (p != nullptr) {
        p->flags &= static_cast<uint8_t>(~0x01U);
      }
    }
  }
  static void clear_just_applied_ritual(EnemyState& e) noexcept {
    // Clear the just_applied flag. If the resulting PowerInstance has stacks=0
    // and no flags set, remove it so from_combat comparison stays consistent
    // (from_combat only inserts kRitual when just_applied=true).
    PowerInstance* p = powers::find_power(e.powers_, e.power_count_,
                                          sts2::game::PowerKind::kRitual);
    if (p == nullptr) {
      return;
    }
    p->flags &= static_cast<uint8_t>(~0x01U);
    if (p->stacks == 0 && p->flags == 0) {
      // Remove the now-empty Ritual entry
      powers::remove_power(e.powers_, e.power_count_,
                           sts2::game::PowerKind::kRitual);
    }
  }

  // CurlUp card-stamp: stored in _pad of the kCurlUp PowerInstance.
  // _pad == 0 means no card stored. CardId enum values are 1..4.
  [[nodiscard]] static uint8_t get_curl_up_stored_card(
      const EnemyState& e) noexcept {
    const PowerInstance* p = powers::find_power(e.powers_, e.power_count_,
                                                sts2::game::PowerKind::kCurlUp);
    return (p != nullptr) ? p->_pad : 0U;
  }
  static void set_curl_up_stored_card(EnemyState& e,
                                      uint8_t card_stamp) noexcept {
    PowerInstance* p = powers::find_power(e.powers_, e.power_count_,
                                          sts2::game::PowerKind::kCurlUp);
    if (p != nullptr) {
      p->_pad = card_stamp;
    }
  }

  [[nodiscard]] static sts2::game::Stat& dark_strike_base(
      EnemyState& e) noexcept {
    return e.dark_strike_base_;
  }
  [[nodiscard]] static sts2::game::Stat& ritual_amount(EnemyState& e) noexcept {
    return e.ritual_amount_;
  }
  [[nodiscard]] static bool& performed_first_move(EnemyState& e) noexcept {
    return e.performed_first_move_;
  }
  [[nodiscard]] static sts2::game::MoveId& current_move(
      EnemyState& e) noexcept {
    return e.current_move_;
  }
  [[nodiscard]] static uint8_t& move_index(EnemyState& e) noexcept {
    return e.move_index_;
  }
  [[nodiscard]] static bool& alive(EnemyState& e) noexcept { return e.alive_; }
  // Wave-26/M.α: expose powers_ + power_count_ refs so production code can
  // invoke powers::remove_power (sibling to add_power) without re-rolling
  // slot-shift logic. Friend-class wraps the private fields uniformly.
  [[nodiscard]] static std::array<PowerInstance, kMaxPowersPerCreature>&
  powers_array(EnemyState& e) noexcept {
    return e.powers_;
  }
  [[nodiscard]] static uint8_t& power_count_ref(EnemyState& e) noexcept {
    return e.power_count_;
  }
  // Wave-26/M.α: set a new EnemyState into a CompactState slot. Used by
  // do_surprise_spawn to append spawned enemies at index >= enemy_count_.
  // Caller MUST bump enemy_count separately (no implicit increment).
  static void set_enemy_at(CompactState& s, std::size_t index,
                           const EnemyState& value) noexcept {
    assert(index < s.enemies_.size());
    s.enemies_[index] = value;
  }

  [[nodiscard]] static sts2::game::Stat& player_hp(CompactState& s) noexcept {
    return s.player_hp_;
  }
  [[nodiscard]] static sts2::game::Stat& player_block(
      CompactState& s) noexcept {
    return s.player_block_;
  }
  // player_strength / player_weak: route through player_powers_
  // Wave-23/J.beta: drop the int16_t narrow casts now that PowerInstance.stacks
  // is int32_t (Q2-ADR-014).
  static void add_player_strength(CompactState& s, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(s.player_powers_, s.player_power_count_,
                      sts2::game::PowerKind::kStrength, delta);
  }
  static void add_player_weak(CompactState& s, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(s.player_powers_, s.player_power_count_,
                      sts2::game::PowerKind::kWeak, delta);
  }
  static void add_player_frail(CompactState& s, int delta) noexcept {
    if (delta == 0) {
      return;
    }
    powers::add_power(s.player_powers_, s.player_power_count_,
                      sts2::game::PowerKind::kFrail, delta);
  }
  static void decrement_player_power(CompactState& s,
                                     sts2::game::PowerKind kind) noexcept {
    PowerInstance* p =
        powers::find_power(s.player_powers_, s.player_power_count_, kind);
    if (p == nullptr) {
      return;
    }
    --p->stacks;
    if (p->stacks <= 0) {
      powers::remove_power(s.player_powers_, s.player_power_count_, kind);
    }
  }
  // Wave-23/J.beta: return widened int16_t → int32_t (Q2-ADR-014).
  [[nodiscard]] static int32_t get_player_frail(
      const CompactState& s) noexcept {
    return powers::stacks_of(s.player_powers_, s.player_power_count_,
                             sts2::game::PowerKind::kFrail);
  }
  [[nodiscard]] static sts2::game::Stat& energy(CompactState& s) noexcept {
    return s.energy_;
  }
  // Wave-23/J.beta: round widened uint16_t → int32_t (Q2-ADR-014).
  [[nodiscard]] static int32_t& round(CompactState& s) noexcept {
    return s.round_;
  }
  [[nodiscard]] static Phase& phase(CompactState& s) noexcept {
    return s.phase_;
  }
  [[nodiscard]] static std::array<EnemyState, kMaxEnemies>& enemies(
      CompactState& s) noexcept {
    return s.enemies_;
  }
  [[nodiscard]] static uint8_t& enemy_count(CompactState& s) noexcept {
    return s.enemy_count_;
  }
  [[nodiscard]] static CardCounts& hand(CompactState& s) noexcept {
    return s.hand_;
  }
  [[nodiscard]] static CardCounts& draw(CompactState& s) noexcept {
    return s.draw_;
  }
  [[nodiscard]] static CardCounts& discard(CompactState& s) noexcept {
    return s.discard_;
  }
};

}  // namespace detail

namespace {

using sts2::game::CardId;
using sts2::game::EnemySlot;
using sts2::game::MonsterKind;
using sts2::game::MoveId;
using sts2::game::PowerKind;
using sts2::game::TargetType;
using sts2::game::card_effects::card_effect_for;
using sts2::game::card_effects::kCountedCardIds;
using sts2::game::monster_moves::kMonsterMoveTables;
using M = detail::StateMutator;

// ---------------------------------------------------------------------------
// Forward declarations (defined later in this anonymous namespace).
// ---------------------------------------------------------------------------
void do_enemy_act(CompactState& s, EnemyState& e);
void do_enemy_tick_powers(CompactState& s, EnemyState& e);
void do_roll_next_move(EnemyState& e);

// ---------------------------------------------------------------------------
// Damage helpers
// ---------------------------------------------------------------------------

void damage_enemy(EnemyState& enemy, int strength, int weak, int base) {
  const int dmg = sts2::damage::compute_outgoing(base, strength, weak);
  const bool was_alive = enemy.get_alive();
  (void)sts2::damage::apply_to_defender(M::hp(enemy), M::block(enemy), dmg);
  if (enemy.get_hp().value() <= 0 && was_alive) {
    M::alive(enemy) = false;
  }
}

// ---------------------------------------------------------------------------
// CurlUp AfterCardPlayed: if enemy has CurlUp with stored card matching
// played_card, enemy gains block (stacks via compute_outgoing_block with
// Unpowered semantics — no Frail tax) and CurlUp is removed.
// ---------------------------------------------------------------------------
void apply_curl_up_after_card(EnemyState& e, CardId played_card) noexcept {
  const uint8_t stored = M::get_curl_up_stored_card(e);
  if (stored == 0) {
    return;
  }
  if (static_cast<uint8_t>(played_card) != stored) {
    return;
  }
  const PowerInstance* p = powers::find_power(
      e.get_powers(), e.get_power_count(), PowerKind::kCurlUp);
  if (p == nullptr) {
    return;
  }
  const int stacks = p->stacks;
  // Upstream AfterCardPlayed: CreatureCmd.GainBlock(base.Owner, base.Amount,
  // ValueProp.Unpowered, null). IsPoweredCardOrMonsterMoveBlock = false
  // → no Frail tax. Enemy dexterity = 0 in Phase-1.
  const int block_gained =
      sts2::damage::compute_outgoing_block(stacks, 0, false, false);
  M::block(e) += block_gained;
  M::remove_power(e, PowerKind::kCurlUp);
}

// ---------------------------------------------------------------------------
// apply_player_action_in_place
// ---------------------------------------------------------------------------
bool apply_player_action_in_place(CompactState& state, const Action& action) {
  assert(state.get_phase() == Phase::kPlayerActing);

  if (action.kind == ActionKind::kEndTurn) {
    M::phase(state) = Phase::kAtChanceDraw;
    return true;
  }

  assert(action.card_id != CardId::kNone);
  const CardId id = action.card_id;
  const auto& fx = card_effect_for(id);
  if (fx.cost > state.get_energy().value()) {
    return false;
  }
  if (state.get_hand()[id] == 0) {
    return false;
  }

  if (fx.target == TargetType::kAnyEnemy) {
    if (!action.target_idx.in_range(state.get_enemies())) {
      return false;
    }
    auto slot = action.target_idx;
    if (!slot.at(state.get_enemies()).get_alive()) {
      return false;
    }
  }

  --M::hand(state)[id];
  M::energy(state) -= fx.cost;

  if (fx.base_damage) {
    EnemyState& e = action.target_idx.at(M::enemies(state));
    damage_enemy(e, state.get_player_strength().value(),
                 state.get_player_weak().value(), fx.base_damage);
    // CurlUp AfterDamageReceived: if the target is still alive and has CurlUp
    // with no card stored yet, store this card id.
    // All card-sourced attacks are powered attacks in the Q2 Phase-1 model
    // (ValueProp.Move set, Unpowered not set → IsPoweredAttack = true).
    if (e.get_alive()) {
      const uint8_t stored = M::get_curl_up_stored_card(e);
      if (stored == 0) {
        const PowerInstance* curl_p = powers::find_power(
            e.get_powers(), e.get_power_count(), PowerKind::kCurlUp);
        if (curl_p != nullptr) {
          M::set_curl_up_stored_card(e, static_cast<uint8_t>(id));
        }
      }
    }
  }
  if (fx.base_block) {
    // Block from a card play: IsPoweredCardOrMonsterMoveBlock = true.
    // Player dexterity = 0 in Phase-1.
    const bool frail = M::get_player_frail(state) > 0;
    const int block =
        sts2::damage::compute_outgoing_block(fx.base_block, 0, frail, true);
    M::player_block(state) += block;
  }
  if (fx.weak_to_target) {
    EnemyState& e = action.target_idx.at(M::enemies(state));
    M::add_weak(e, fx.weak_to_target);
  }
  if (fx.requires_discard) {
    if (state.get_hand().total() == 0) {
      if (action.survivor_discard_id != CardId::kNone) {
        return false;
      }
    } else {
      if (action.survivor_discard_id != CardId::kNone &&
          state.get_hand()[action.survivor_discard_id] == 0) {
        return false;
      }
      if (action.survivor_discard_id != CardId::kNone) {
        --M::hand(state)[action.survivor_discard_id];
        ++M::discard(state)[action.survivor_discard_id];
      }
    }
  }

  // Wave-22.α — Exhaust card semantics: the played card vanishes one-way
  // (skip hand→discard). Upstream: CardKeyword.Exhaust on Slimed.cs:19;
  // engine routes the played card to the Exhaust pile instead of Discard.
  // The Q2 oracle's CompactState has no explicit Exhaust pile (exhausted
  // cards are gone for the rest of the combat); modeling it as a one-way
  // deletion matches play semantics and keeps the state shape unchanged.
  if (!fx.exhaust_on_play) {
    ++M::discard(state)[id];
  }
  // Wave-22.α TODO — draws_on_play OnPlay-draw chance node is NOT wired in
  // C.2-α scope; Slimed's `Draw 1` effect is a deterministic noop here.
  // C.4-δ's SmallSlimes pin verifies that the search policy never derives
  // benefit from playing Slimed (Slimed costs 1 energy with no payoff in
  // this stub, so it is strictly dominated by EndTurn). Full draw-from-deck
  // chance-node integration deferred to a future wave.
  (void)fx.draws_on_play;

  // CurlUp AfterCardPlayed: scan alive enemies for CurlUp with stored card
  // matching this play; if found, enemy gains block and CurlUp is removed.
  for (uint8_t i = 0; i < state.get_enemy_count(); ++i) {
    EnemyState& e = M::enemies(state)[i];
    if (!e.get_alive()) {
      continue;
    }
    apply_curl_up_after_card(e, id);
  }

  return true;
}

// ---------------------------------------------------------------------------
// has_pending_random_move_roll: true iff slot's current move's follow-up
// is a RandomBranch (kRandomBranchCannotRepeat or kWeightedRandomCannotRepeat
// per FollowUpRule). Cultist + LouseProgenitor → all kStrict → always false.
// Wave-22.α: drives the kAtEnemyMoveRng deferred-roll chance node.
// ---------------------------------------------------------------------------
[[nodiscard]] bool has_pending_random_move_roll(const EnemyState& e) noexcept {
  if (!e.get_alive()) {
    return false;
  }
  const auto kind_idx = static_cast<std::size_t>(e.get_kind());
  if (kind_idx >= kMonsterMoveTables.size()) {
    return false;
  }
  const auto& table = kMonsterMoveTables[kind_idx];
  const uint8_t move_idx = e.get_move_index();
  if (move_idx >= table.move_count) {
    return false;
  }
  const auto rule = table.moves[move_idx].follow_up_rule;
  return rule != sts2::game::monster_moves::FollowUpRule::kStrict;
}

// ---------------------------------------------------------------------------
// resolve_end_turn_pre_draw_in_place
//
// Wave-22.α: when any alive enemy's current move has a RandomBranch
// follow-up, the move-roll step is DEFERRED and phase advances to
// kAtEnemyMoveRng (a chance node enumerated in chance.cc). Cultist +
// LouseProgenitor moves are all kStrict → control flow + phase end-state
// is BIT-IDENTICAL to pre-wave-22.
// ---------------------------------------------------------------------------
void resolve_end_turn_pre_draw_in_place(CompactState& state) {
  assert(state.get_phase() == Phase::kAtChanceDraw);

  struct EndTurnOps {
    // NOLINTBEGIN(cppcoreguidelines-avoid-const-or-ref-data-members)
    // Local helper struct, never assigned; reference member is intentional.
    CompactState& state;
    // Per-slot pending-RNG snapshot taken at start of turn_flow. Updated by
    // roll_enemy_next_move() which DEFERS the roll for slots with
    // RandomBranch follow-ups (those slots' move_idx is left unchanged for
    // chance.cc to resolve via enumerate_enemy_move_outcomes).
    bool any_pending_random_roll = false;
    // NOLINTEND(cppcoreguidelines-avoid-const-or-ref-data-members)

    void end_player_turn() {
      // Player power tick is a no-op in v1 (Frail ticks at kAtEnemyTurnEnd
      // side=Enemy in do_enemy_tick_powers below).
      M::discard(state) += state.get_hand();
      M::hand(state) = CardCounts{};
    }
    [[nodiscard]] std::size_t enemy_count() const {
      return state.get_enemy_count();
    }
    [[nodiscard]] bool enemy_alive(std::size_t slot) const {
      return is_alive(state.get_enemy(slot));
    }
    void reset_enemy_block(std::size_t slot) {
      M::block(M::enemies(state)[slot]) = sts2::game::Stat{0};
    }
    void enemy_act(std::size_t slot) {
      do_enemy_act(state, M::enemies(state)[slot]);
    }
    [[nodiscard]] bool terminal() const {
      return state.get_player_hp() == sts2::game::Stat{0};
    }
    void tick_enemy_powers(std::size_t slot) {
      do_enemy_tick_powers(state, M::enemies(state)[slot]);
    }
    void increment_round() { M::round(state) = state.get_round() + 1; }
    [[nodiscard]] int round() const { return state.get_round(); }
    void roll_enemy_next_move(std::size_t slot) {
      EnemyState& e = M::enemies(state)[slot];
      if (has_pending_random_move_roll(e)) {
        // Defer to kAtEnemyMoveRng chance node — leave move_idx unchanged so
        // chance.cc can apply the branch outcome. performed_first_move is
        // advanced here so the chance node only enumerates branches
        // (initial_move_index logic is N/A on deferred rolls).
        M::performed_first_move(e) = true;
        any_pending_random_roll = true;
        return;
      }
      do_roll_next_move(e);
    }
    void reset_player_block() { M::player_block(state) = sts2::game::Stat{0}; }
    void refill_player_energy(int amount) {
      M::energy(state) = sts2::game::Stat{amount};
    }
  };

  EndTurnOps ops{state};
  sts2::game::turn_flow::resolve_end_turn_pre_draw(ops);
  // If any roll was deferred AND the state is not terminal, phase advances
  // to kAtEnemyMoveRng so chance.cc enumerates the move-RNG outcomes.
  // Otherwise phase stays kAtChanceDraw → card-draw chance enumeration.
  if (ops.any_pending_random_roll &&
      state.get_player_hp() != sts2::game::Stat{0}) {
    M::phase(state) = Phase::kAtEnemyMoveRng;
  }
  // Else: phase already kAtChanceDraw; the draw step is the chance node.
}

// ---------------------------------------------------------------------------
// apply_draw_in_place
// ---------------------------------------------------------------------------
void apply_draw_in_place(CompactState& state, CardCounts drawn) {
  assert(state.get_phase() == Phase::kAtChanceDraw);
  assert(drawn.total() <= 10);

  // Reshuffle if the draw pile alone can't satisfy the request. Engine drains
  // pre-reshuffle cards first then post-reshuffle; for multiset purposes the
  // unioned outcome is identical, so a single up-front reshuffle is sound.
  if (!state.get_draw().covers(drawn)) {
    M::draw(state) += state.get_discard();
    M::discard(state) = CardCounts{};
  }

  assert(state.get_draw().covers(drawn));

  M::hand(state) += drawn;
  M::draw(state) -= drawn;

  M::phase(state) = Phase::kPlayerActing;
}

// ---------------------------------------------------------------------------
// do_enemy_act: kind-dispatched enemy action.
// ---------------------------------------------------------------------------
void do_enemy_act_louse_progenitor(CompactState& s, EnemyState& e) {
  using sts2::game::MoveId;
  switch (e.get_current_move()) {
    case MoveId::kWebCannon: {
      // Attack 9 (strength/weak modifiers apply).
      const int dmg = sts2::damage::compute_outgoing(
          9, e.get_strength().value(), e.get_weak().value());
      (void)sts2::damage::apply_to_defender(M::player_hp(s), M::player_block(s),
                                            dmg);
      // Apply 2 Frail to player.
      M::add_player_frail(s, 2);
      break;
    }
    case MoveId::kCurlAndGrow: {
      // Defend 14 block (self). Monster move block: powered (ValueProp.Move
      // set, Unpowered not set) → IsPoweredCardOrMonsterMoveBlock = true. Enemy
      // has no dexterity or Frail in Phase-1.
      const int blk = sts2::damage::compute_outgoing_block(14, 0, false, true);
      M::block(e) += blk;
      // Apply 5 Strength to self.
      M::add_strength(e, 5);
      break;
    }
    case MoveId::kPounce: {
      // Attack 16 (strength/weak modifiers apply).
      const int dmg = sts2::damage::compute_outgoing(
          16, e.get_strength().value(), e.get_weak().value());
      (void)sts2::damage::apply_to_defender(M::player_hp(s), M::player_block(s),
                                            dmg);
      break;
    }
    default:
      break;
  }
}

// ---------------------------------------------------------------------------
// Slime enemy_act path (wave-22.α framework). Data-driven: walks the
// MonsterMove.effects[] array on kMonsterMoveTables[kind].moves[move_idx].
// Slime move tables are zero-filled at C.2-α merge time (C.3-β populates
// real data); the loop is a noop until then. Cultist + LouseProgenitor
// paths bypass this entirely (handcoded above).
// ---------------------------------------------------------------------------
bool kind_is_slime(MonsterKind k) noexcept {
  return k == MonsterKind::kLeafSlimeS || k == MonsterKind::kLeafSlimeM ||
         k == MonsterKind::kTwigSlimeS || k == MonsterKind::kTwigSlimeM;
}

// Returns true for any MonsterKind whose combat behavior is fully described
// by a populated kMonsterMoveTables entry. do_enemy_act routes these through
// do_enemy_act_slime (table-driven dispatch) rather than the cultist default.
bool kind_is_table_driven(MonsterKind k) noexcept {
  return kind_is_slime(k) || k == MonsterKind::kNibbit;
}

void do_enemy_act_slime(CompactState& s, EnemyState& e) {
  const auto kind_idx = static_cast<std::size_t>(e.get_kind());
  if (kind_idx >= kMonsterMoveTables.size()) {
    return;
  }
  const auto& table = kMonsterMoveTables[kind_idx];
  const uint8_t move_idx = e.get_move_index();
  if (move_idx >= table.move_count) {
    // C.2-α framework path: slime tables are zero-filled (move_count=0).
    // Silent noop until C.3-β populates the data.
    return;
  }
  const auto& move = table.moves[move_idx];
  for (uint8_t i = 0; i < move.effect_count; ++i) {
    const auto& fx = move.effects[i];
    switch (fx.kind) {
      case sts2::game::MoveEffectKind::kAttack: {
        const int dmg = sts2::damage::compute_outgoing(
            fx.value, e.get_strength().value(), e.get_weak().value());
        (void)sts2::damage::apply_to_defender(M::player_hp(s),
                                              M::player_block(s), dmg);
        break;
      }
      case sts2::game::MoveEffectKind::kDefend: {
        // Monster-move block is powered (IsPoweredCardOrMonsterMoveBlock=true);
        // no enemy Frail/dexterity in Phase-1.
        const int blk =
            sts2::damage::compute_outgoing_block(fx.value, 0, false, true);
        M::block(e) += blk;
        break;
      }
      case sts2::game::MoveEffectKind::kBuffSelf: {
        // Self-buff: route through StateMutator (no slime moves use this
        // path in upstream LeafSlime / TwigSlime data — included for
        // framework completeness).
        M::add_enemy_power(e, fx.power_kind, fx.value);
        break;
      }
      case sts2::game::MoveEffectKind::kDebuffPlayer: {
        if (fx.power_kind == PowerKind::kFrail) {
          M::add_player_frail(s, fx.value);
        } else if (fx.power_kind == PowerKind::kWeak) {
          M::add_player_weak(s, fx.value);
        } else if (fx.power_kind == PowerKind::kVulnerable) {
          // Phase-1 has no Vulnerable consumers; placeholder for future waves.
        }
        break;
      }
      case sts2::game::MoveEffectKind::kAddStatusCard: {
        // Wave-22.α — slime GOOP / STICKY_SHOT path. Insert N Slimed cards
        // into the player's DISCARD pile (upstream
        // CardPileCmd.AddToCombatAndPreview targets PileType.Discard,
        // CardPileCmd.cc:886-916). fx.value encodes the count (1 or 2).
        // CardId is fixed at kSlimed for all slime status-card emissions.
        const int count = fx.value;
        for (int n = 0; n < count; ++n) {
          if (M::discard(s)[CardId::kSlimed] <
              sts2::game::card_effects::kMaxSlimedAccumulation) {
            ++M::discard(s)[CardId::kSlimed];
          }
          // else: cap reached; additional Slimed drops silently
          // (Q2-ADR-013 Amendment 4 §Slimed-cap)
        }
        break;
      }
      case sts2::game::MoveEffectKind::kBuffEnemy: {
        // Wave-24/K.α — Nibbit HISS-equivalent: applies stacks of
        // fx.power_kind to the acting enemy. Targets SELF (other-enemy buff
        // requires a different MoveEffectKind; not in scope). Uses generic
        // powers::add_power via M::add_enemy_power — no Ritual side-effects
        // (just_applied flag untouched; ritual_amount_ unchanged).
        M::add_enemy_power(e, fx.power_kind, fx.value);
        break;
      }
      case sts2::game::MoveEffectKind::kBlockSelf: {
        // Wave-24/K.α — Nibbit SLICE-equivalent: applies fx.value block to
        // the acting enemy. Block persists across the player's intervening
        // turn and decays at START of the enemy's NEXT turn via the
        // existing turn_flow.h::EndTurnOps::reset_enemy_block scaffold
        // (same path Louse's kCurlAndGrow self-block uses).
        M::add_enemy_block(e, fx.value);
        break;
      }
      case sts2::game::MoveEffectKind::kNone:
        // Sentinel; no effect.
        break;
    }
  }
}

void do_enemy_act(CompactState& s, EnemyState& e) {
  if (e.get_kind() == MonsterKind::kLouseProgenitor) {
    do_enemy_act_louse_progenitor(s, e);
    return;
  }
  if (kind_is_table_driven(e.get_kind())) {
    do_enemy_act_slime(s, e);
    return;
  }
  // Cultist path (kCultistCalcified, kCultistDamp): SEMANTICS UNCHANGED.
  // kSeedC0ffeeExpectedHp / kSeedC0ffeeExpectedRounds bit-identical invariant.
  sts2::game::move_calc::act_on_intent(
      e.get_current_move(),
      [&]() {
        // Mirrors powers::apply for kRitual: amount accumulates on the Power,
        // but in v1 Ritual is applied once -> Power.amount stays at
        // ritual_amount. We model the dynamic Ritual state purely via
        // just_applied flag on the kRitual PowerInstance.
        M::set_just_applied_ritual(e, true);
      },
      [&]() {
        const int dmg = sts2::damage::compute_outgoing(
            e.get_dark_strike_base().value(), e.get_strength().value(),
            e.get_weak().value());
        (void)sts2::damage::apply_to_defender(M::player_hp(s),
                                              M::player_block(s), dmg);
      });
}

// ---------------------------------------------------------------------------
// do_enemy_tick_powers: generic hook dispatch at kAtEnemyTurnEnd.
// Fires for each active power on the enemy and for player's Frail.
// Cultist Ritual semantics are PRESERVED.
// ---------------------------------------------------------------------------
void do_enemy_tick_powers(CompactState& s, EnemyState& e) {
  // Ritual: driven by ritual_amount_ scalar (not by kRitual being in the
  // powers array) so strength is granted even after the just_applied
  // PowerInstance is removed. This preserves cultist semantics exactly:
  //   spawn turn → just_applied set by Incantation → tick clears flag (no
  //   strength gain); subsequent turns → ritual_amount_ > 0 but no kRitual
  //   entry → grants strength each turn.
  if (e.get_ritual_amount().value() > 0) {
    const bool just_applied = M::get_just_applied_ritual(e);
    if (just_applied) {
      M::clear_just_applied_ritual(e);
    } else {
      M::add_strength(e, e.get_ritual_amount().value());
    }
  }

  // Snapshot power kinds for the remaining hooks so we iterate a stable set
  // even if powers are removed during iteration (e.g. Frail reaching 0).
  // Each PowerKind appears at most once.
  const uint8_t snap_count = e.get_power_count();
  std::array<PowerKind, kMaxPowersPerCreature> snap{};
  for (uint8_t i = 0; i < snap_count; ++i) {
    snap[i] = e.get_powers()[i].kind;
  }

  for (uint8_t i = 0; i < snap_count; ++i) {
    switch (snap[i]) {
      case PowerKind::kRitual:
        // Handled above via ritual_amount_ scalar; skip here.
        break;
      case PowerKind::kFrail:
        // Frail on enemy ticks down at kAtEnemyTurnEnd (side=Enemy).
        M::decrement_power(e, PowerKind::kFrail);
        break;
      case PowerKind::kWeak:
        // Weak on enemy ticks down.
        M::decrement_weak(e);
        break;
      case PowerKind::kCurlUp:
        // No turn-end behavior for CurlUp.
        break;
      case PowerKind::kStrength:
      case PowerKind::kVulnerable:
        // No tick-down at turn end in Phase-1.
        break;
    }
  }

  // Player's Frail ticks down at kAtEnemyTurnEnd (side=Enemy) per upstream
  // FrailPower.cs AfterTurnEnd(side=Enemy → PowerCmd.TickDownDuration).
  if (M::get_player_frail(s) > 0) {
    M::decrement_player_power(s, PowerKind::kFrail);
  }
}

// ---------------------------------------------------------------------------
// do_roll_next_move: advance enemy intent to the next move.
//
// Wave-23-prep: all kinds (cultist, Louse, slime) dispatch through the
// table-driven advance_intent_table. This keeps move_index_ in sync with
// current_move_ so from_combat parity holds and slimes can advance their
// move chains. Cultist semantics are unchanged: cultist table moves[0]
// (Incantation) has follow_up_index=1 (DarkStrike), moves[1] (DarkStrike)
// has follow_up_index=1 (self-loop) — identical to the legacy
// advance_intent path for current_move, but now also updates move_index_
// so AI<->engine state parity holds.
// ---------------------------------------------------------------------------
void do_roll_next_move(EnemyState& e) {
  const auto kind_idx = static_cast<std::size_t>(e.get_kind());
  if (kind_idx >= kMonsterMoveTables.size()) {
    return;
  }
  const auto& table = kMonsterMoveTables[kind_idx];
  sts2::game::move_calc::advance_intent_table(
      M::performed_first_move(e), M::current_move(e), M::move_index(e), table);
}

}  // namespace

// ---------------------------------------------------------------------------
// Public interface
// ---------------------------------------------------------------------------
std::vector<Action> legal_actions(const CompactState& state) {
  assert(state.get_phase() == Phase::kPlayerActing);

  std::vector<Action> actions;

  for (CardId id : kCountedCardIds) {
    if (state.get_hand()[id] == 0) {
      continue;
    }
    const auto& fx = card_effect_for(id);
    if (fx.cost > state.get_energy().value()) {
      continue;
    }

    const TargetType tgt = fx.target;
    if (tgt == TargetType::kAnyEnemy) {
      for (uint8_t i = 0; i < state.get_enemy_count(); ++i) {
        if (!state.get_enemy(i).get_alive()) {
          continue;
        }
        Action a;
        a.kind = ActionKind::kPlayCard;
        a.card_id = id;
        a.target_idx = EnemySlot{static_cast<int>(i)};
        actions.push_back(a);
      }
    } else if (fx.requires_discard) {
      CardCounts post = state.get_hand();
      assert(post[CardId::kSurvivor] > 0);
      --post[CardId::kSurvivor];
      if (post.total() == 0) {
        Action a;
        a.kind = ActionKind::kPlayCard;
        a.card_id = CardId::kSurvivor;
        a.target_idx = EnemySlot::none();
        a.survivor_discard_id = CardId::kNone;
        actions.push_back(a);
      } else {
        for (CardId other : kCountedCardIds) {
          if (other == CardId::kSurvivor) {
            continue;
          }
          if (post[other] == 0) {
            continue;
          }
          Action a;
          a.kind = ActionKind::kPlayCard;
          a.card_id = CardId::kSurvivor;
          a.target_idx = EnemySlot::none();
          a.survivor_discard_id = other;
          actions.push_back(a);
        }
      }
    } else {
      Action a;
      a.kind = ActionKind::kPlayCard;
      a.card_id = id;
      a.target_idx = EnemySlot::none();
      actions.push_back(a);
    }
  }

  Action end;
  end.kind = ActionKind::kEndTurn;
  actions.push_back(end);
  return actions;
}

std::optional<CompactState> apply_player_action(const CompactState& state,
                                                const Action& action) {
  CompactState next = state;
  if (!apply_player_action_in_place(next, action)) {
    return std::nullopt;
  }
  return next;
}

bool is_terminal(const CompactState& s) noexcept {
  if (s.get_player_hp() == sts2::game::Stat{0}) {
    return true;
  }
  const auto& enemies = s.get_enemies();
  const uint8_t count = s.get_enemy_count();
  for (uint8_t i = 0; i < count; ++i) {
    if (enemies[i].get_alive()) {
      return false;
    }
  }
  return true;
}

int draw_count(const CompactState& s) noexcept {
  return sts2::game::turn_calc::hand_draw_size(s.get_round());
}

CompactState resolve_end_turn_pre_draw(const CompactState& state) {
  CompactState next = state;
  resolve_end_turn_pre_draw_in_place(next);
  return next;
}

CompactState apply_draw(const CompactState& state, CardCounts drawn) {
  CompactState next = state;
  apply_draw_in_place(next, drawn);
  return next;
}

// ---------------------------------------------------------------------------
// Test-only seam (wave-24/K.α). Mirrors the do_enemy_act_slime MoveEffect
// dispatch switch — kept in lockstep manually (small surface). Used by
// test_transition.cc to exercise kBuffEnemy / kBlockSelf paths without
// requiring a monster_moves table entry (Nibbit lands in K.β).
// ---------------------------------------------------------------------------
namespace test_internals {

void apply_single_move_effect_for_test(
    CompactState& s, EnemyState& e,
    const sts2::game::monster_moves::MoveEffect& fx) noexcept {
  switch (fx.kind) {
    case sts2::game::MoveEffectKind::kAttack: {
      const int dmg = sts2::damage::compute_outgoing(
          fx.value, e.get_strength().value(), e.get_weak().value());
      (void)sts2::damage::apply_to_defender(M::player_hp(s), M::player_block(s),
                                            dmg);
      break;
    }
    case sts2::game::MoveEffectKind::kDefend: {
      const int blk =
          sts2::damage::compute_outgoing_block(fx.value, 0, false, true);
      M::block(e) += blk;
      break;
    }
    case sts2::game::MoveEffectKind::kBuffSelf: {
      M::add_enemy_power(e, fx.power_kind, fx.value);
      break;
    }
    case sts2::game::MoveEffectKind::kDebuffPlayer: {
      if (fx.power_kind == sts2::game::PowerKind::kFrail) {
        M::add_player_frail(s, fx.value);
      } else if (fx.power_kind == sts2::game::PowerKind::kWeak) {
        M::add_player_weak(s, fx.value);
      }
      break;
    }
    case sts2::game::MoveEffectKind::kAddStatusCard: {
      const int count = fx.value;
      for (int n = 0; n < count; ++n) {
        if (M::discard(s)[CardId::kSlimed] <
            sts2::game::card_effects::kMaxSlimedAccumulation) {
          ++M::discard(s)[CardId::kSlimed];
        }
      }
      break;
    }
    case sts2::game::MoveEffectKind::kBuffEnemy: {
      // Wave-24/K.α: generic stack-add via powers::add_power; NO Ritual
      // side-effects.
      M::add_enemy_power(e, fx.power_kind, fx.value);
      break;
    }
    case sts2::game::MoveEffectKind::kBlockSelf: {
      // Wave-24/K.α: accumulate enemy block. Decay at end of enemy turn (see
      // decay_enemy_block_for_test).
      M::add_enemy_block(e, fx.value);
      break;
    }
    case sts2::game::MoveEffectKind::kNone:
      break;
  }
}

void decay_enemy_block_for_test(EnemyState& e) noexcept {
  M::set_enemy_block(e, sts2::game::Stat{0});
}

}  // namespace test_internals

}  // namespace sts2::ai::transition

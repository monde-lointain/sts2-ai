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
#include "sts2/game/move_effect_dispatch.h"
#include "sts2/game/stat.h"
#include "sts2/game/turn_calc.h"
#include "sts2/game/turn_flow.h"
#include "sts2/game/types.h"

namespace sts2::ai::transition {

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

// ---------------------------------------------------------------------------
// Cultist dsb/ritual lookups via the kMonsterMoveTables data path.
// Returns 0 for non-cultist kinds (find_move_index → 0xFF) so
// do_enemy_tick_powers can call cultist_ritual_amount on ALL enemy kinds
// without special-casing (slimes/Louse/Nibbit return 0, gate skipped).
[[nodiscard]] int32_t cultist_dark_strike_base(
    sts2::game::MonsterKind k) noexcept {
  const uint8_t idx = sts2::game::monster_moves::find_move_index(
      k, sts2::game::MoveId::kDarkStrike);
  if (idx == 0xFF) return 0;
  const auto& fx =
      sts2::game::monster_moves::kMonsterMoveTables[static_cast<std::size_t>(k)]
          .moves[idx]
          .effects[0];
  assert(fx.kind == sts2::game::MoveEffectKind::kAttack &&
         "cultist DarkStrike effect shape changed");
  return fx.value;
}

[[nodiscard]] int32_t cultist_ritual_amount(
    sts2::game::MonsterKind k) noexcept {
  const uint8_t idx = sts2::game::monster_moves::find_move_index(
      k, sts2::game::MoveId::kIncantation);
  if (idx == 0xFF) return 0;
  const auto& fx =
      sts2::game::monster_moves::kMonsterMoveTables[static_cast<std::size_t>(k)]
          .moves[idx]
          .effects[0];
  assert(fx.kind == sts2::game::MoveEffectKind::kBuffSelf &&
         fx.power_kind == sts2::game::PowerKind::kRitual &&
         "cultist Incantation effect shape changed");
  return fx.value;
}

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
  (void)sts2::damage::apply_to_defender(enemy.hp_mut(), enemy.block_mut(), dmg);
  if (enemy.get_hp().value() <= 0 && was_alive) {
    enemy.set_alive(false);
  }
}

// ---------------------------------------------------------------------------
// CurlUp AfterCardPlayed: if enemy has CurlUp with stored card matching
// played_card, enemy gains block (stacks via compute_outgoing_block with
// Unpowered semantics — no Frail tax) and CurlUp is removed.
// ---------------------------------------------------------------------------
void apply_curl_up_after_card(EnemyState& e, CardId played_card) noexcept {
  const CardId stored = sts2::ai::powers::curl_up_card(e.powers());
  if (stored == CardId::kNone) {
    return;
  }
  if (played_card != stored) {
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
  e.add_block_amount(block_gained);
  e.remove_power(PowerKind::kCurlUp);
}

// ---------------------------------------------------------------------------
// apply_player_action_in_place
// ---------------------------------------------------------------------------
bool apply_player_action_in_place(CompactState& state, const Action& action) {
  assert(state.get_phase() == Phase::kPlayerActing);

  if (action.kind == ActionKind::kEndTurn) {
    state.set_phase(Phase::kAtChanceDraw);
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

  state.remove_one_from_hand(id);
  state.sub_energy(sts2::game::Stat{fx.cost});

  if (fx.base_damage) {
    EnemyState& e =
        state.get_enemy_mut(static_cast<std::size_t>(action.target_idx.raw()));
    damage_enemy(e, state.get_player_strength().value(),
                 state.get_player_weak().value(), fx.base_damage);
    // CurlUp AfterDamageReceived: if the target is still alive and has CurlUp
    // with no card stored yet, store this card id.
    // All card-sourced attacks are powered attacks in the Q2 Phase-1 model
    // (ValueProp.Move set, Unpowered not set → IsPoweredAttack = true).
    if (e.get_alive()) {
      const CardId stored = sts2::ai::powers::curl_up_card(e.powers());
      if (stored == CardId::kNone) {
        const PowerInstance* curl_p = powers::find_power(
            e.get_powers(), e.get_power_count(), PowerKind::kCurlUp);
        if (curl_p != nullptr) {
          sts2::ai::powers::set_curl_up_card(e.powers_mut(), id);
        }
      }
    }
  }
  if (fx.base_block) {
    // Block from a card play: IsPoweredCardOrMonsterMoveBlock = true.
    // Player dexterity = 0 in Phase-1.
    const bool frail = state.get_player_frail() > 0;
    const int block =
        sts2::damage::compute_outgoing_block(fx.base_block, 0, frail, true);
    state.add_player_block(sts2::game::Stat{block});
  }
  if (fx.weak_to_target) {
    EnemyState& e =
        state.get_enemy_mut(static_cast<std::size_t>(action.target_idx.raw()));
    e.add_power(PowerKind::kWeak, fx.weak_to_target);
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
        state.remove_one_from_hand(action.survivor_discard_id);
        state.add_one_to_discard(action.survivor_discard_id);
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
    state.add_one_to_discard(id);
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
    EnemyState& e = state.get_enemy_mut(i);
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
      // Player power tick is a no-op in v1 (Frail ticks at enemy turn-end
      // side=Enemy in do_enemy_tick_powers below).
      state.move_hand_to_discard();
    }
    [[nodiscard]] std::size_t enemy_count() const {
      return state.get_enemy_count();
    }
    [[nodiscard]] bool enemy_alive(std::size_t slot) const {
      return is_alive(state.get_enemy(slot));
    }
    void reset_enemy_block(std::size_t slot) {
      state.get_enemy_mut(slot).set_block(sts2::game::Stat{0});
    }
    void enemy_act(std::size_t slot) {
      do_enemy_act(state, state.get_enemy_mut(slot));
    }
    [[nodiscard]] bool terminal() const {
      return state.get_player_hp() == sts2::game::Stat{0};
    }
    void tick_enemy_powers(std::size_t slot) {
      do_enemy_tick_powers(state, state.get_enemy_mut(slot));
    }
    void increment_round() { state.set_round(state.get_round() + 1); }
    [[nodiscard]] int round() const { return state.get_round(); }
    void roll_enemy_next_move(std::size_t slot) {
      EnemyState& e = state.get_enemy_mut(slot);
      if (has_pending_random_move_roll(e)) {
        // Defer to kAtEnemyMoveRng chance node — leave move_idx unchanged so
        // chance.cc can apply the branch outcome. performed_first_move is
        // advanced here so the chance node only enumerates branches
        // (initial_move_index logic is N/A on deferred rolls).
        e.set_performed_first_move(true);
        any_pending_random_roll = true;
        return;
      }
      do_roll_next_move(e);
    }
    void reset_player_block() { state.set_player_block(sts2::game::Stat{0}); }
    void refill_player_energy(int amount) {
      state.set_energy(sts2::game::Stat{amount});
    }
  };

  EndTurnOps ops{state};
  sts2::game::turn_flow::resolve_end_turn_pre_draw(ops);
  // If any roll was deferred AND the state is not terminal, phase advances
  // to kAtEnemyMoveRng so chance.cc enumerates the move-RNG outcomes.
  // Otherwise phase stays kAtChanceDraw → card-draw chance enumeration.
  if (ops.any_pending_random_roll &&
      state.get_player_hp() != sts2::game::Stat{0}) {
    state.set_phase(Phase::kAtEnemyMoveRng);
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
    state.reshuffle_discard_into_draw();
  }

  assert(state.get_draw().covers(drawn));

  state.apply_draw_from_pile(drawn);

  state.set_phase(Phase::kPlayerActing);
}

// ---------------------------------------------------------------------------
// OracleTarget — MoveEffectTarget adapter for the AI transition oracle.
// Bridges apply_move_effect<> (game namespace, stateless) to the concrete
// CompactState + EnemyState mutation API. Wave-28/C.1.
// ---------------------------------------------------------------------------
struct OracleTarget {
  CompactState& s;
  EnemyState& e;

  bool attack_player(int32_t base) noexcept {
    s.apply_to_player(sts2::damage::compute_outgoing(
        base, e.get_strength().value(), e.get_weak().value()));
    return true;  // oracle never short-circuits mid-multi-effect
  }
  void gain_self_block(int32_t base) noexcept {
    // Monster-move block is powered (IsPoweredCardOrMonsterMoveBlock=true);
    // no enemy Frail/dexterity in Phase-1.
    e.add_block_amount(
        sts2::damage::compute_outgoing_block(base, 0, false, true));
  }
  void add_self_power(sts2::game::PowerKind kind, int32_t v) noexcept {
    e.add_power(kind, v);
  }
  void add_player_frail(int32_t v) noexcept { s.add_player_frail(v); }
  void add_player_weak(int32_t v) noexcept { s.add_player_weak(v); }
  void add_player_vulnerable(int32_t v) noexcept { s.add_player_vulnerable(v); }
  void add_player_discard_slimed(int32_t v) noexcept {
    s.add_player_discard_slimed(v);
  }
  void unsupported(sts2::game::MoveEffectKind kind) noexcept {
    // wave-32/C.1-α: hardened from silent no-op so monster_moves table typos
    // (effects with no OracleTarget handler) surface loudly. Cultist uses
    // act_on_intent, not OracleTarget, so this can't fire on the cultist hot
    // loop.
    assert(false &&
           "OracleTarget: unhandled MoveEffectKind — table typo or new effect "
           "kind unmapped");
    (void)kind;
  }
};

// ---------------------------------------------------------------------------
// Table-driven enemy_act path (wave-22.α framework). Data-driven: walks the
// MonsterMove.effects[] array on kMonsterMoveTables[kind].moves[move_idx].
// Covers slime, Nibbit, and LouseProgenitor. Cultist bypasses this entirely
// (act_on_intent path below).
// ---------------------------------------------------------------------------
bool kind_is_slime(MonsterKind k) noexcept {
  return k == MonsterKind::kLeafSlimeS || k == MonsterKind::kLeafSlimeM ||
         k == MonsterKind::kTwigSlimeS || k == MonsterKind::kTwigSlimeM;
}

// Returns true for any MonsterKind whose combat behavior is fully described
// by a populated kMonsterMoveTables entry. do_enemy_act routes these through
// do_enemy_act_table_driven rather than the cultist default.
bool kind_is_table_driven(MonsterKind k) noexcept {
  return k == MonsterKind::kLouseProgenitor || kind_is_slime(k) ||
         k == MonsterKind::kNibbit;
}

void do_enemy_act_table_driven(CompactState& s, EnemyState& e) {
  const auto kind_idx = static_cast<std::size_t>(e.get_kind());
  if (kind_idx >= kMonsterMoveTables.size()) {
    return;
  }
  const auto& table = kMonsterMoveTables[kind_idx];
  const uint8_t move_idx = e.get_move_index();
  if (move_idx >= table.move_count) {
    return;
  }
  const auto& move = table.moves[move_idx];
  OracleTarget target{s, e};
  for (uint8_t i = 0; i < move.effect_count; ++i) {
    sts2::game::apply_move_effect(move.effects[i], target);
  }
}

void do_enemy_act(CompactState& s, EnemyState& e) {
  if (kind_is_table_driven(e.get_kind())) {
    do_enemy_act_table_driven(s, e);
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
        sts2::ai::powers::set_just_applied_ritual(e.powers_mut(), true);
      },
      [&]() {
        const int dmg = sts2::damage::compute_outgoing(
            cultist_dark_strike_base(e.get_kind()), e.get_strength().value(),
            e.get_weak().value());
        (void)sts2::damage::apply_to_defender(s.player_hp_mut(),
                                              s.player_block_mut(), dmg);
      });
}

// ---------------------------------------------------------------------------
// do_enemy_tick_powers: generic hook dispatch at enemy turn end.
// Fires for each active power on the enemy and for player's Frail.
// Cultist Ritual semantics are PRESERVED.
// ---------------------------------------------------------------------------
void do_enemy_tick_powers(CompactState& s, EnemyState& e) {
  // Ritual: sourced from kMonsterMoveTables[kind] via cultist_ritual_amount
  // (wave-35/B.2-β; ADR-031). Returns 0 for non-cultist kinds so this gate
  // safely fires for all enemy kinds without special-casing.
  // Cultist semantics preserved exactly:
  //   spawn turn → just_applied set by Incantation → tick clears flag (no
  //   strength gain); subsequent turns → ritual > 0, no kRitual entry →
  //   grants strength each turn.
  if (cultist_ritual_amount(e.get_kind()) > 0) {
    const bool just_applied = sts2::ai::powers::just_applied_ritual(e.powers());
    if (just_applied) {
      sts2::ai::powers::clear_just_applied_ritual(e.powers_mut());
    } else {
      e.add_power(PowerKind::kStrength, cultist_ritual_amount(e.get_kind()));
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
        // Handled above via cultist_ritual_amount(kind) helper (ADR-031); skip.
        break;
      case PowerKind::kFrail:
        // Frail on enemy ticks down at enemy turn end (side=Enemy).
        e.decrement_power(PowerKind::kFrail);
        break;
      case PowerKind::kWeak:
        // Weak on enemy ticks down.
        e.decrement_power(PowerKind::kWeak);
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

  // Player's Frail ticks down at enemy turn end (side=Enemy) per upstream
  // FrailPower.cs AfterTurnEnd(side=Enemy → PowerCmd.TickDownDuration).
  if (s.get_player_frail() > 0) {
    s.decrement_player_power(PowerKind::kFrail);
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
  e.advance_intent(table);
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
  return !std::any_of(enemies.begin(), enemies.begin() + count,
                      [](const EnemyState& e) { return e.get_alive(); });
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
// Test-only seam (wave-24/K.α). Trampoline into apply_move_effect<OracleTarget>
// (wave-28/C.1). The 7 callsites in test_transition.cc are unchanged.
// ---------------------------------------------------------------------------
namespace test_internals {

void apply_single_move_effect_for_test(
    CompactState& s, EnemyState& e,
    const sts2::game::monster_moves::MoveEffect& fx) noexcept {
  OracleTarget target{s, e};
  sts2::game::apply_move_effect(fx, target);
}

void decay_enemy_block_for_test(EnemyState& e) noexcept {
  e.set_block(sts2::game::Stat{0});
}

}  // namespace test_internals

}  // namespace sts2::ai::transition

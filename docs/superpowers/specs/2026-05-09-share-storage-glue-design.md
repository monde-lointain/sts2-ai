# Refactor: Share Storage Glue Between Engine and AI

**Date:** 2026-05-09
**Source:** Smell-audit finding #1 — *Alternative Classes with Different Interfaces: parallel `Combat` ↔ `CompactState` worlds*.

## Goal

Reduce duplication and scar-tissue comments between `src/game/` (engine) and `src/ai/` (search) by:

- Promoting engine numeric fields to `Stat` so the canonical damage primitives in `damage_calc.h` accept both layers' types directly. This kills the `apply_damage` adapter at `src/ai/transition.cc:24-32` whose only purpose is to bridge `Stat` ⇄ `int&`.
- Adding a shared `for_each_alive_enemy(container, fn)` helper to dedupe the 8 `for (auto& e : ...) if (alive) { ... }` loops that currently exist in both `combat.cc` and `transition.cc`.

After this refactor, the AI's `CompactState` remains a parallel type, but the *storage glue* (numeric types, iteration, defender update) is shared. Future work can extend the shared layer further (smell #2 — Shotgun Surgery on enums).

## Non-goals

The following are explicitly out of scope:

- `Power.just_applied` temporary field (smell #2 — Object-Orientation Abusers).
- `Combat::combat_over_` flag refactor (honourable mention from the audit).
- Eliminating `CompactState` and making `Combat` cheaply copyable (the maximal scope option, rejected).
- Promoting `Player.energy`, `Combat::round_`, or `Hand::kMaxSize` to `Stat`.
- Removing the `std::function` discard callback.

## Changes

### 1. `Stat` ergonomics

`include/sts2/game/stat.h` — add `operator<<(std::ostream&, Stat)` so the render layer's existing `out << vitals.hp` call sites keep working without `.value()` sprinkled everywhere.

### 2. Promote `Vitals` to `Stat`

`include/sts2/game/vitals.h:9-15` — change:

```
struct Vitals {
  int hp = 0;
  int max_hp = 0;
  int block = 0;
  std::vector<Power> powers;
};
```

to:

```
struct Vitals {
  Stat hp;
  Stat max_hp;
  Stat block;
  std::vector<Power> powers;
};
```

`include/sts2/game/damage_calc.h` — add a `Stat&` overload of `apply_to_defender`:

```
[[nodiscard]] inline int apply_to_defender(Stat& hp, Stat& block, int incoming) noexcept;
```

Implementation forwards to the canonical block-then-hp absorption logic; updates `hp` and `block` via `Stat`'s `-=`. The existing `int&` overload stays (still used in `tests/game/test_damage.cc` and as the underlying primitive).

`src/game/damage.cc:19-21` — `apply_to_defender(Vitals&, int)` becomes a forwarder to the new `Stat&` overload.

### 3. Promote `Enemy` numeric fields to `Stat`

`include/sts2/game/enemy.h:19-20` — change:

```
int dark_strike_base = 0;
int ritual_amount = 0;
```

to:

```
Stat dark_strike_base;
Stat ritual_amount;
```

This makes `Enemy` symmetric with `ai::EnemyState`, which already uses `Stat` for these fields.

### 4. Delete the adapter

`src/ai/transition.cc:24-32` — delete `apply_damage`. `transition::damage_enemy` (currently lines 34-40) becomes:

```
void damage_enemy(EnemyState& enemy, int strength, int weak, int base) {
  const int dmg = sts2::damage::compute_outgoing(base, strength, weak);
  sts2::damage::apply_to_defender(enemy.hp, enemy.block, dmg);
  if (enemy.hp == sts2::game::Stat{0}) {
    enemy.alive = false;
  }
}
```

The single other caller of `apply_damage` — `transition::enemy_act` at line 164 — switches to call `apply_to_defender(s.player_hp, s.player_block, dmg)` directly.

### 5. Shared `for_each_alive_enemy` helper

`include/sts2/game/enemy.h` — add a free helper templated on the container type:

```
template <typename C, typename F>
void for_each_alive_enemy(C&& enemies, F&& fn) {
  for (auto& e : enemies) {
    if (is_alive(e)) fn(e);
  }
}
```

`is_alive` already has an overload for `Enemy` (`enemy.h:23-25`). The AI side uses `EnemyState` (different type), so we add a sibling overload `is_alive(const ai::EnemyState&)` in `include/sts2/ai/state.h`. ADL then routes correctly from the templated helper (no need to pass an explicit predicate).

**Decision:** add only the no-early-exit variant. Two of the eight target loops have mid-loop early-exit conditions and stay as raw `for`-loops with `is_alive` checks — the helper covers the other six. No `for_each_alive_enemy_until` variant unless a third call site appears.

### 6. Apply the helper

Replace these loops with `for_each_alive_enemy`:

- `src/game/combat.cc:31-35` (start_player_turn — roll moves)
- `src/game/combat.cc:52-55` (enemy_phase — zero block)
- `src/game/combat.cc:66-70` (enemy_phase — tick powers)
- `src/ai/transition.cc:204-205` (resolve_end_turn_pre_draw — zero block)
- `src/ai/transition.cc:213-215` (resolve_end_turn_pre_draw — tick powers)
- `src/ai/transition.cc:219-221` (resolve_end_turn_pre_draw — roll next move)

Leave these two as raw `for`-loops (mid-loop early exit); just rewrite the alive check to call `is_alive(e)` instead of touching `e.vitals.hp` / `e.alive` directly:

- `src/game/combat.cc:57-65` (enemy_phase — act, with `combat_over_` early exit)
- `src/ai/transition.cc:207-211` (resolve_end_turn_pre_draw — act, with player-death early exit)

## Test strategy

Pure structural refactor — no behavior changes. The full ctest run (`ctest --preset ninja-debug`, 252 active tests) must stay green at the end of each of the three logical steps.

`Stat` already implements `==`, `+=`, `-=`, comparison, and (after step 1) `operator<<`, so most call-site updates are:

- Wrapping `int` constructors at boundaries (e.g., `e.vitals.max_hp = Stat{rng.uniform_int(38, 41)}`).
- No change for arithmetic that uses `+=` / `-=` (Stat supports `int` rhs).
- No change for `if (e.vitals.hp > 0)` — Stat has `<=>` against `Stat`, but not `int`. Resolution: compare against `Stat{0}` (e.g., `if (e.vitals.hp > Stat{0})`) or call `.value() > 0`. Pick `Stat{0}` for consistency with existing AI code (`transition.cc:37,185,210`).
- Render `out << x` keeps working via the new `operator<<`.

Test files affected (estimated, to be confirmed during execution):

- `tests/game/test_damage.cc` — uses `Vitals{}` aggregates and `apply_to_defender(Vitals&, int)`. Needs `Stat{...}` wrapping at literal sites.
- `tests/game/test_combat.cc`, `test_enemies.cc`, `test_powers.cc` — same kind of literal wrapping.
- `tests/render/test_render.cc` — checks rendered text; should be unchanged after `operator<<`.
- `tests/ai/test_state.cc`, `test_state_parity.cc` — already use `Stat`; should need minimal/no change.

## Subagent decomposition

Three sequential logical units. Each touches overlapping files (combat.cc, transition.cc, multiple tests), so they're done one at a time. Each runs as a subagent in its own git worktree to keep the main context clean and let me review the diff before merging.

- **W1 — Vitals → Stat.** Add `Stat::operator<<`. Promote `Vitals.{hp,max_hp,block}`. Add `apply_to_defender(Stat&, Stat&, int)` overload; rewire `apply_to_defender(Vitals&, int)`. Update all call sites + tests. Commit. Verify `ctest` green.
- **W2 — Enemy numeric → Stat; delete adapter.** Promote `Enemy.{dark_strike_base, ritual_amount}`. Delete `transition.cc::apply_damage` adapter; simplify `damage_enemy` and `enemy_act` to call `damage::apply_to_defender` directly. Update call sites + tests. Commit. Verify `ctest` green. (Depends on W1 because `Enemy::vitals` is now `Stat`-valued.)
- **W3 — `for_each_alive_enemy` helper.** Add the helper to `enemy.h` and `is_alive(const ai::EnemyState&)` to `state.h`. Replace 6 of 8 alive-enemy loops (the two with mid-loop early-exits stay as raw loops with `is_alive` checks). Commit. Verify `ctest` green. (Depends on W2 only by chronology — could in principle run in parallel with W2, but file overlap on combat.cc/transition.cc makes a clean rebase non-trivial.)

After all three commits land, merge each worktree branch back to main in order.

## Acceptance criteria

- All three commits pushed to dedicated branches in worktrees.
- `ctest --preset ninja-debug` reports the same 252 active / 3 skipped after each commit.
- `src/ai/transition.cc::apply_damage` no longer exists.
- The 4-occurrence pattern `for (auto& e : enemies_) if (e.vitals.hp > 0)` no longer appears in `combat.cc` (some loops keep `is_alive` checks but no longer touch `e.vitals.hp` directly).
- The scar-tissue adapter comment at `transition.cc:24-26` is gone.

## Open questions

None — all design decisions resolved during brainstorming.

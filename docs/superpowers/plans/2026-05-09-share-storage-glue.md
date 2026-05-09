# Share-Storage-Glue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the `apply_damage` adapter at `src/ai/transition.cc:24-32` and dedupe alive-enemy iteration loops by promoting engine numeric fields to `Stat` and adding a shared iteration helper.

**Architecture:** Three sequential worktree-isolated tasks (W1 → W2 → W3). W1 promotes `Vitals` fields to `Stat`. W2 promotes `Enemy::{dark_strike_base, ritual_amount}` and deletes the adapter. W3 adds `for_each_alive_enemy` and applies it to 6 of 8 alive-enemy loops. Each worktree must build clean and pass all 252 ctest cases before merge.

**Tech Stack:** C++20, CMake 3.28+, Ninja, GoogleTest. Build via `cmake --preset ninja-debug && cmake --build --preset ninja-debug`. Test via `ctest --preset ninja-debug`.

**Spec:** `docs/superpowers/specs/2026-05-09-share-storage-glue-design.md`.

---

## Conventions used throughout

- **`Stat{N}` literal wrapping** at comparison sites. Existing convention (`tests/ai/test_state.cc:37` etc., `src/ai/transition.cc:37,185,210`). Tests doing `EXPECT_EQ(e.vitals.hp, 70)` become `EXPECT_EQ(e.vitals.hp, Stat{70})`. Same for `>`, `<=`, etc.
- **`out << stat`** in render code keeps working because Task W1 adds `operator<<(std::ostream&, Stat)`.
- **No `int` ↔ `Stat` implicit conversion.** Always explicit `Stat{n}` or `.value()`.
- **Build verification command:** `cmake --build --preset ninja-debug 2>&1 | tail -40`.
- **Test verification command:** `ctest --preset ninja-debug --output-on-failure 2>&1 | tail -20`. Expected: `100% tests passed, 0 tests failed out of N` where N matches pre-refactor count (252 active + 3 skipped).

---

## Task W0: Establish baseline

**Files:** none modified.

- [ ] **Step 1: Confirm current ctest baseline.**

Run from repo root:
```bash
cmake --preset ninja-debug 2>&1 | tail -5
cmake --build --preset ninja-debug 2>&1 | tail -5
ctest --preset ninja-debug 2>&1 | tail -5
```

Expected: build clean, ctest reports `100% tests passed, 0 tests failed` with a known total count (record it; the spec said 252 active + 3 skipped). If anything fails here, STOP and surface to the user — refactor cannot proceed on broken baseline.

- [ ] **Step 2: Record baseline test count.**

Note the exact "X tests passed" number from the previous step. Report it back. This is the invariant that every subsequent worktree must preserve.

---

## Task W1: Promote `Vitals` to `Stat`

**Worktree branch:** `refactor/T20a-vitals-stat`.

**Files:**
- Modify: `include/sts2/game/stat.h` (add `operator<<`)
- Modify: `include/sts2/game/vitals.h` (3 fields → `Stat`)
- Modify: `include/sts2/game/damage_calc.h` (add `Stat&` overload)
- Modify: `src/game/damage.cc` (rewrite `apply_to_defender(Vitals&, int)`)
- Modify: `src/game/combat.cc` (alive checks, block reset)
- Modify: `src/game/enemies.cc` (cultist HP construction)
- Modify: `src/render/render.cc` (vitals print sites)
- Modify: `src/render/ai_recommendation.cc` if it touches `vitals.hp`/etc. (verify; spec says no, but check)
- Modify: `src/ai/state.cc` (`from_combat` reads `p.vitals.hp` etc., extracts to `Stat{}`)
- Modify: `tests/game/test_helpers.h:85` (`Vitals{hp, hp, 0, {}}` → `Vitals{Stat{hp}, Stat{hp}, Stat{0}, {}}`)
- Modify: `tests/game/test_helpers.h:122` (`vitals.hp > 0` → `vitals.hp > Stat{0}`)
- Modify: All test files comparing `vitals.{hp,max_hp,block}` to int literals — wrap with `Stat{N}`. Inventory:
  - `tests/game/test_enemies.cc` (~12 sites)
  - `tests/game/test_combat.cc` (~12 sites)
  - `tests/game/test_cards.cc` (~7 sites)
  - `tests/render/test_render.cc:115` (Vitals literal)
  - `tests/render/test_render_internal.cc` (~6 sites: lines 174, 188, 210, 213, 216, 227, 230 — Vitals literals + `dark_strike_base = 9` if applicable)
  - `tests/render/test_ai_recommendation.cc:81-82` (`ASSERT_LE`/`ASSERT_GT` against 0)
  - `tests/ai/test_outcome_calibration.cc:50, 63, 170` (Vitals literal + reading `vitals.hp` into `int starting_hp` / accumulating `sum_final_hp += static_cast<double>(vitals.hp)`)
- Modify: `tools/seed-pinner/pin_seeds.cc:50, 117, 124` (uses `e.vitals.hp == target_hp` and `int = e.vitals.hp`)

- [ ] **Step W1.1: Create the worktree.**

Run from repo root:
```bash
git worktree add -b refactor/T20a-vitals-stat .worktrees/T20a-vitals-stat main
cd .worktrees/T20a-vitals-stat
```

Expected: new branch + worktree at `.worktrees/T20a-vitals-stat`. From here, all paths are relative to that worktree.

- [ ] **Step W1.2: Add `operator<<` to `Stat`.**

Edit `include/sts2/game/stat.h`. Add `#include <ostream>` near the top alongside the existing includes, then add the inline free-function operator at the end of the namespace (before the closing `}`):

```cpp
inline std::ostream& operator<<(std::ostream& os, Stat s) {
  return os << s.value();
}
```

- [ ] **Step W1.3: Promote `Vitals` to `Stat`.**

Edit `include/sts2/game/vitals.h`. Replace the existing struct with:

```cpp
#pragma once

#include <vector>

#include "sts2/game/power.h"
#include "sts2/game/stat.h"

namespace sts2::game {

struct Vitals {
  Stat hp;
  Stat max_hp;
  Stat block;
  std::vector<Power> powers;
};

}  // namespace sts2::game
```

Note: removed the `= 0` defaults — `Stat` default-constructs to 0.

- [ ] **Step W1.4: Add `Stat&` overload to `damage_calc.h`.**

Edit `include/sts2/game/damage_calc.h`. After the existing `apply_to_defender(int& hp, int& block, int incoming)`, add:

```cpp
[[nodiscard]] inline int apply_to_defender(Stat& hp, Stat& block, int incoming) noexcept {
  if (incoming <= block.value()) {
    block -= incoming;
    return 0;
  }
  incoming -= block.value();
  block = Stat{0};
  const int hp_loss = incoming < hp.value() ? incoming : hp.value();
  hp -= hp_loss;
  return hp_loss;
}
```

This requires `#include "sts2/game/stat.h"` at the top of the header — add it.

- [ ] **Step W1.5: Rewire `apply_to_defender(Vitals&, int)`.**

Edit `src/game/damage.cc:19-21`. After Vitals fields are `Stat`, the function becomes a forwarder to the new overload:

```cpp
int apply_to_defender(sts2::game::Vitals& target, int incoming) {
  return apply_to_defender(target.hp, target.block, incoming);
}
```

(Body is identical to before — change is implicit through the type swap. Verify no further edits needed beyond what the type system forces.)

- [ ] **Step W1.6: Update production call sites.**

Search-and-replace across `src/`. For each file, fix the type-mismatch errors the compiler raises:

In `src/game/combat.cc`:
- Line 32: `if (e.vitals.hp > 0)` → `if (e.vitals.hp > sts2::game::Stat{0})`
- Line 38: `player_.vitals.block = 0;` → `player_.vitals.block = sts2::game::Stat{0};`
- Line 53: `if (e.vitals.hp > 0)` → `if (e.vitals.hp > sts2::game::Stat{0})`
- Line 54: `e.vitals.block = 0;` → `e.vitals.block = sts2::game::Stat{0};`
- Line 58: `if (e.vitals.hp <= 0)` → `if (e.vitals.hp <= sts2::game::Stat{0})`
- Line 67: `if (e.vitals.hp > 0)` → `if (e.vitals.hp > sts2::game::Stat{0})`
- Line 124: `return player_.vitals.hp <= 0;` → `return player_.vitals.hp <= sts2::game::Stat{0};`
- Line 159: `void Combat::gain_player_block(int amt) { player_.vitals.block += amt; }` — `Stat::operator+=(int)` exists, so `+= amt` still works. No change needed.

In `src/game/enemies.cc:13-18`, `:24-29`:
- `int hp = rng.uniform_int(38, 41);` stays
- `e.vitals.max_hp = hp;` → `e.vitals.max_hp = sts2::game::Stat{hp};`
- `e.vitals.hp = hp;` → `e.vitals.hp = sts2::game::Stat{hp};`
- (Same pattern for damp cultist with hp = uniform_int(51, 53).)

In `include/sts2/game/enemy.h:23-30`:
- `e.vitals.hp > 0` → `e.vitals.hp > Stat{0}` (note: this header is in `sts2::game` namespace, so `Stat{0}` resolves without prefix).

In `src/render/render.cc`:
- Line 78: `if (e.vitals.hp > 0 && ...)` → `if (e.vitals.hp > sts2::game::Stat{0} && ...)`
- Lines 118-122, 145-147: `out << c.player().vitals.hp` and friends — *no change needed* thanks to W1.2 `operator<<`.
- Line 121: `if (c.player().vitals.block > 0)` → `if (c.player().vitals.block > sts2::game::Stat{0})`
- Line 146: `if (e.vitals.block > 0)` → `if (e.vitals.block > sts2::game::Stat{0})`

In `src/ai/state.cc`:
- Line 29: `s.alive = e.vitals.hp > 0;` → `s.alive = e.vitals.hp > sts2::game::Stat{0};`
- Line 30-31: `s.hp = sts2::game::Stat{e.vitals.hp};` — `e.vitals.hp` is now Stat, so this becomes `s.hp = e.vitals.hp;`. Same for `block`.
- Line 86-87: `s.player_hp = sts2::game::Stat{p.vitals.hp};` → `s.player_hp = p.vitals.hp;`. Same for player_block (line 87).

In `src/render/ai_recommendation.cc`: spot-check — should be unaffected (it doesn't read vitals.hp directly, only passes through Combat queries). Verify after the build.

- [ ] **Step W1.7: Update `tools/seed-pinner/pin_seeds.cc`.**

Edit lines 50, 117, 124:
- Line 50: `if (e.vitals.hp == target_hp) { return seed; }` → `if (e.vitals.hp == sts2::game::Stat{target_hp}) { return seed; }`
- Line 117: `calcified_hp_seed42 = sts2::enemies::make_calcified_cultist(rng).vitals.hp;` — the LHS is `int`, the RHS is now `Stat`. Change to `... .vitals.hp.value();`.
- Line 124: same pattern for `damp_hp_seed42`.

- [ ] **Step W1.8: Update test files.**

Apply the wrapping convention to every test site touching `vitals.{hp, max_hp, block}` against int literals. The mechanical rule: `EXPECT_EQ(x.vitals.hp, N)` → `EXPECT_EQ(x.vitals.hp, sts2::game::Stat{N})` (or `Stat{N}` if a `using` is in scope).

Specific files (use `grep -n vitals.hp tests/...` to find each site, then wrap):

`tests/game/test_helpers.h`:
- Line 85: `e.vitals = sts2::game::Vitals{hp, hp, 0, {}};` → `e.vitals = sts2::game::Vitals{sts2::game::Stat{hp}, sts2::game::Stat{hp}, sts2::game::Stat{0}, {}};`
- Line 122: `if (c.enemies()[i].vitals.hp > 0)` → `if (c.enemies()[i].vitals.hp > sts2::game::Stat{0})`

`tests/game/test_enemies.cc`: wrap every int literal compared with `e.vitals.{hp,max_hp,block}`. Lines 58, 59, 60, 61, 66, 77, 82, 87, 92, 106, 107, 108, 109, 114, 123, 128, 133, 230, 245, 259.

`tests/game/test_combat.cc`: wrap lines 88, 89, 168, 170, 198, 209, 226, 227, 238, 251, 261, 393. Also `attacker.vitals = Vitals{1, 1, 0, {}};` (line 195) → `Vitals{Stat{1}, Stat{1}, Stat{0}, {}}`. Same line 218. Line 393: the `int hp_before = c.enemies()[0].vitals.hp;` — change to `.value()`.

`tests/game/test_cards.cc`: wrap lines 68, 82, 112, 126, 159, 211, 229.

`tests/render/test_render.cc:115`: `e.vitals = Vitals{20, 20, 3, {}};` → `Vitals{Stat{20}, Stat{20}, Stat{3}, {}}`.

`tests/render/test_render_internal.cc:210, 213, 216, 227, 230`: same `Vitals{...}` literal-wrap pattern.

`tests/render/test_ai_recommendation.cc:81-82`: `ASSERT_LE(c.enemies()[0].vitals.hp, 0)` → `ASSERT_LE(c.enemies()[0].vitals.hp, Stat{0})`. (Add `using sts2::game::Stat;` if not present at top of file's anonymous namespace.)

`tests/ai/test_outcome_calibration.cc`:
- Line 50: `e.vitals = Vitals{6, 6, 0, {}};` → `Vitals{Stat{6}, Stat{6}, Stat{0}, {}}`.
- Line 63: `const int starting_hp = combat.player().vitals.hp;` → `... .vitals.hp.value();`.
- Line 170: `sum_final_hp += static_cast<double>(combat.player().vitals.hp);` → `... .vitals.hp.value();`.

- [ ] **Step W1.9: Build.**

Run from worktree root:
```bash
cmake --preset ninja-debug 2>&1 | tail -5
cmake --build --preset ninja-debug 2>&1 | tail -40
```

Expected: clean build. If errors remain, the most likely culprits are spots missed in step W1.6/W1.8. Compiler errors will name the file:line — fix and rebuild iteratively.

- [ ] **Step W1.10: Run tests.**

```bash
ctest --preset ninja-debug --output-on-failure 2>&1 | tail -20
```

Expected: same passed/failed/skipped counts as the W0 baseline. If any test fails, do not proceed — investigate (most likely the wrapping was missed at one site, or `Vitals{a,b,c,{}}` order got transposed).

- [ ] **Step W1.11: Commit.**

```bash
git add -A
git commit -m "refactor: T20a promote Vitals fields to Stat

Removes the int/Stat divergence between engine and AI types. Vitals.hp,
max_hp, block now use Stat. Adds apply_to_defender(Stat&, Stat&, int) in
damage_calc.h and operator<<(ostream&, Stat) in stat.h. Test sites wrap
int literals with Stat{...} per existing convention."
```

- [ ] **Step W1.12: Report back.**

Subagent reports: "W1 complete on `refactor/T20a-vitals-stat`. Build clean. ctest: <count> passed, <count> failed (expect 0), <count> skipped (expect 3 baseline)."

---

## Task W2: Promote `Enemy` numeric fields to `Stat`; delete adapter

**Worktree branch:** `refactor/T20b-enemy-stat-adapter`.

**Prerequisite:** W1 merged to `main` (or branched off W1).

**Files:**
- Modify: `include/sts2/game/enemy.h` (`dark_strike_base`, `ritual_amount` → `Stat`)
- Modify: `src/game/enemies.cc` (cultist factory + `act` lambda)
- Modify: `src/game/powers.cc` if it references these fields (verify; it doesn't directly)
- Modify: `src/game/combat.cc:153-154` if `enemy_attack_player` takes int — it does, called with `e.dark_strike_base` which is now Stat. Pass `.value()`.
- Modify: `src/render/render.cc:68` (`compute_outgoing(... e.dark_strike_base)` — pass `.value()`)
- Modify: `src/ai/state.cc:36-37` (`Stat{e.dark_strike_base}` → `e.dark_strike_base` since both sides are now Stat)
- Modify: `src/ai/transition.cc` — delete `apply_damage` adapter, simplify `damage_enemy`, simplify `enemy_act` to call `damage::apply_to_defender(s.player_hp, s.player_block, dmg)` directly
- Modify: tests touching `e.dark_strike_base = 9;` and `e.ritual_amount = 2;` patterns:
  - `tests/render/test_render_internal.cc:174, 188`
  - `tests/game/test_enemies.cc:212, 225, 239, 253` (`e.ritual_amount = 2;` etc., already comparing with int — wrap RHS as `Stat{2}`)
  - `tests/game/test_enemies.cc:62, 63, 110, 111` (assertion sites — wrap RHS)
  - `tests/ai/test_recommend_legality.cc:51`
  - `tests/ai/test_outcome_calibration.cc:51`

- [ ] **Step W2.1: Create worktree.**

```bash
# from repo main
git worktree add -b refactor/T20b-enemy-stat-adapter .worktrees/T20b-enemy-stat-adapter main
cd .worktrees/T20b-enemy-stat-adapter
```

(If W1 was just merged, this branches off the latest main.)

- [ ] **Step W2.2: Promote `Enemy` numeric fields to `Stat`.**

Edit `include/sts2/game/enemy.h:19-20`:

```cpp
Stat dark_strike_base;
Stat ritual_amount;
```

Add `#include "sts2/game/stat.h"` to that header.

- [ ] **Step W2.3: Update `src/game/enemies.cc`.**

Lines 16-17: `e.dark_strike_base = 9;` → `e.dark_strike_base = sts2::game::Stat{9};`. Same for `e.ritual_amount = 2;` → `sts2::game::Stat{2}`.

Lines 27-28 (Damp cultist): same pattern with values 1 and 5.

Lines 40-44 (the `act` lambdas): `sts2::powers::apply(e.vitals.powers, sts2::game::PowerKind::kRitual, e.ritual_amount);` — `apply` takes `int`, so pass `.value()`. Same on line 44: `combat.enemy_attack_player(e, e.dark_strike_base);` → `... e.dark_strike_base.value());`.

- [ ] **Step W2.4: Update other production sites.**

`src/render/render.cc:68`: `sts2::damage::compute_outgoing(e.vitals.powers, e.dark_strike_base)` — `compute_outgoing` takes `int`, so → `... e.dark_strike_base.value())`.

`src/ai/state.cc:36-37`:
- Before: `s.dark_strike_base = sts2::game::Stat{e.dark_strike_base};`
- After: `s.dark_strike_base = e.dark_strike_base;` (same type now)
- Same for `s.ritual_amount = e.ritual_amount;`

- [ ] **Step W2.5: Delete the adapter.**

Edit `src/ai/transition.cc:24-32`. Delete the `apply_damage` function and its 2-line comment.

Edit `src/ai/transition.cc:34-40` (`damage_enemy`). Replace with:

```cpp
void damage_enemy(EnemyState& enemy, int strength, int weak, int base) {
  const int dmg = sts2::damage::compute_outgoing(base, strength, weak);
  (void)sts2::damage::apply_to_defender(enemy.hp, enemy.block, dmg);
  if (enemy.hp == sts2::game::Stat{0}) {
    enemy.alive = false;
  }
}
```

(`apply_to_defender` is `[[nodiscard]]`; the `(void)` cast suppresses the warning. The build has `-Werror` enabled.)

Edit `src/ai/transition.cc:152-166` (`enemy_act`). Inside the `on_dark_strike` lambda, currently:

```cpp
[&]() {
  const int dmg = sts2::damage::compute_outgoing(
      e.dark_strike_base.value(), e.strength.value(), e.weak.value());
  apply_damage(s.player_hp, s.player_block, dmg);
}
```

Change `apply_damage(...)` → `(void)sts2::damage::apply_to_defender(s.player_hp, s.player_block, dmg);`. Drop the `apply_damage` call site entirely.

- [ ] **Step W2.6: Update tests.**

For each test file touching `e.dark_strike_base` or `e.ritual_amount`, wrap int literals with `Stat{...}`. Specific changes:

`tests/render/test_render_internal.cc:174, 188`: `e.dark_strike_base = 9;` → `e.dark_strike_base = sts2::game::Stat{9};`.

`tests/game/test_enemies.cc`:
- Line 62-63: `EXPECT_EQ(e.dark_strike_base, 9);` → `EXPECT_EQ(e.dark_strike_base, sts2::game::Stat{9});`. Same for ritual_amount = 2.
- Lines 110-111: same with values 1, 5.
- Lines 212, 225, 239, 253: `e.ritual_amount = 2;` → `Stat{2}`; `e.dark_strike_base = 9;` → `Stat{9}`. (Use `Stat` short form if `using sts2::game::Stat;` is already in the file's anonymous namespace; otherwise fully qualify.)

`tests/ai/test_recommend_legality.cc:51`: `e.dark_strike_base = 9;` → `Stat{9}`.

`tests/ai/test_outcome_calibration.cc:51`: same pattern.

`tests/ai/test_search_known.cc` and `tests/ai/test_state.cc` and `tests/ai/test_transition.cc`: already use `Stat{}` per inspection — no changes.

- [ ] **Step W2.7: Build.**

```bash
cmake --preset ninja-debug 2>&1 | tail -5
cmake --build --preset ninja-debug 2>&1 | tail -40
```

Expected: clean. Iterate on errors.

- [ ] **Step W2.8: Test.**

```bash
ctest --preset ninja-debug --output-on-failure 2>&1 | tail -20
```

Expected: same baseline counts as W0/W1.

- [ ] **Step W2.9: Verify the adapter is gone.**

```bash
grep -n "apply_damage" src/ai/transition.cc
```

Expected: no matches. If any remain, fix.

- [ ] **Step W2.10: Commit.**

```bash
git add -A
git commit -m "refactor: T20b promote Enemy numeric fields to Stat; drop apply_damage adapter

Enemy.dark_strike_base and Enemy.ritual_amount now use Stat, symmetric
with ai::EnemyState. The transition.cc::apply_damage adapter (bridging
Stat fields to the int& damage_calc primitive) is gone — both sides now
call damage::apply_to_defender directly."
```

- [ ] **Step W2.11: Report back.**

Subagent reports: "W2 complete on `refactor/T20b-enemy-stat-adapter`. Build clean. ctest: <count> passed. apply_damage adapter removed (verified via grep)."

---

## Task W3: Add `for_each_alive_enemy` helper and apply

**Worktree branch:** `refactor/T20c-alive-enemy-helper`.

**Prerequisite:** W2 merged to `main` (or branched off W2).

**Files:**
- Modify: `include/sts2/game/enemy.h` (add helper template)
- Modify: `include/sts2/ai/state.h` (add `is_alive(EnemyState)`)
- Modify: `src/game/combat.cc` (3 loops using helper, 1 stays as raw)
- Modify: `src/ai/transition.cc` (3 loops using helper, 1 stays as raw)

- [ ] **Step W3.1: Create worktree.**

```bash
git worktree add -b refactor/T20c-alive-enemy-helper .worktrees/T20c-alive-enemy-helper main
cd .worktrees/T20c-alive-enemy-helper
```

- [ ] **Step W3.2: Add the generic helper to `enemy.h`.**

Edit `include/sts2/game/enemy.h`. After the existing `is_alive` overloads, add:

```cpp
template <typename C, typename F>
void for_each_alive_enemy(C&& enemies, F&& fn) {
  for (auto& e : enemies) {
    if (is_alive(e)) fn(e);
  }
}
```

This sits in `namespace sts2::game`. ADL will find `is_alive` for any type that has one in its own namespace.

- [ ] **Step W3.3: Add `is_alive(const ai::EnemyState&)`.**

Edit `include/sts2/ai/state.h`. After the `EnemyState` struct definition, add:

```cpp
[[nodiscard]] inline bool is_alive(const EnemyState& e) noexcept {
  return e.alive;
}
```

This sits in `namespace sts2::ai`. ADL routing: when `for_each_alive_enemy` (in `sts2::game`) iterates `std::array<EnemyState, 2>`, ADL looks at `EnemyState`'s namespace (`sts2::ai`) and finds this overload.

- [ ] **Step W3.4: Apply helper in `src/game/combat.cc`.**

Replace these three loops with `sts2::game::for_each_alive_enemy(enemies_, [&](Enemy& e) { ... });`. (You're already in `namespace sts2::game`, so the unqualified call works.)

Lines 31-35 (`start_player_turn`):
```cpp
for_each_alive_enemy(enemies_, [&](Enemy& e) {
  sts2::enemies::roll_next_move(e);
});
```

Lines 52-55 (`enemy_phase`, zero block):
```cpp
for_each_alive_enemy(enemies_, [](Enemy& e) {
  e.vitals.block = Stat{0};
});
```

Lines 66-70 (`enemy_phase`, tick powers):
```cpp
for_each_alive_enemy(enemies_, [](Enemy& e) {
  sts2::powers::tick_at_turn_end(e.vitals.powers);
});
```

Leave lines 57-65 (`enemy_phase`, act with `combat_over_` early exit) as a raw for-loop. Just replace the alive check `if (e.vitals.hp <= 0)` with `if (!is_alive(e))`:

```cpp
for (auto& e : enemies_) {
  if (!is_alive(e)) {
    continue;
  }
  sts2::enemies::act(e, *this);
  if (combat_over_) {
    return;
  }
}
```

- [ ] **Step W3.5: Apply helper in `src/ai/transition.cc`.**

The transition file is in `namespace sts2::ai::transition` and uses `sts2::game::Stat`. Use `sts2::game::for_each_alive_enemy` qualified.

Lines 204-205 (`resolve_end_turn_pre_draw`, zero block):
```cpp
sts2::game::for_each_alive_enemy(state.enemies, [](EnemyState& e) {
  e.block = sts2::game::Stat{0};
});
```

Lines 213-215 (tick powers):
```cpp
sts2::game::for_each_alive_enemy(state.enemies, [](EnemyState& e) {
  enemy_tick_powers(e);
});
```

Lines 219-221 (roll next move):
```cpp
sts2::game::for_each_alive_enemy(state.enemies, [](EnemyState& e) {
  roll_next_move(e);
});
```

Leave lines 207-211 (`enemy_act` with player-death early exit) as a raw for-loop. Replace `if (!e.alive) continue;` with `if (!is_alive(e)) continue;`:

```cpp
for (auto& e : state.enemies) {
  if (!is_alive(e)) continue;
  enemy_act(state, e);
  if (state.player_hp == sts2::game::Stat{0}) return;
}
```

(`is_alive` here resolves to the new `sts2::ai::is_alive(EnemyState)` via ADL — since we're in `sts2::ai::transition`, unqualified lookup hits the parent namespace `sts2::ai`.)

- [ ] **Step W3.6: Build.**

```bash
cmake --preset ninja-debug 2>&1 | tail -5
cmake --build --preset ninja-debug 2>&1 | tail -40
```

Expected: clean.

- [ ] **Step W3.7: Test.**

```bash
ctest --preset ninja-debug --output-on-failure 2>&1 | tail -20
```

Expected: same baseline counts.

- [ ] **Step W3.8: Verify pattern reduction.**

```bash
grep -cE "for \(auto& e : enemies_\)" src/game/combat.cc
grep -cE "for \(auto& e : state\.enemies\)" src/ai/transition.cc
```

Expected: each prints `1` (only the early-exit raw loop remains in each file). If higher, a loop wasn't replaced.

- [ ] **Step W3.9: Commit.**

```bash
git add -A
git commit -m "refactor: T20c add for_each_alive_enemy helper, dedupe iteration

Adds a templated helper in include/sts2/game/enemy.h and is_alive
overload for ai::EnemyState. Replaces 6 of 8 alive-enemy loops in
combat.cc and transition.cc; the two with mid-loop early-exit
conditions stay as raw loops with is_alive checks."
```

- [ ] **Step W3.10: Report back.**

Subagent reports: "W3 complete on `refactor/T20c-alive-enemy-helper`. Build clean. ctest: <count> passed. Loop count: combat.cc=1 raw, transition.cc=1 raw (both early-exit). Helper applied 6 times."

---

## Task M: Merge worktree branches to main

**Files:** none modified directly; merging only.

- [ ] **Step M.1: Merge W1.**

From repo main (not the worktree):
```bash
git checkout main
git merge --no-ff refactor/T20a-vitals-stat -m "Merge branch 'refactor/T20a-vitals-stat'"
```

- [ ] **Step M.2: Merge W2.**

```bash
git merge --no-ff refactor/T20b-enemy-stat-adapter -m "Merge branch 'refactor/T20b-enemy-stat-adapter'"
```

If W2 was branched off main *before* W1 merged, expect conflicts in tests and combat.cc — resolve by taking both edits (the W1 wrap + W2 wrap should compose). Better: branch W2 off W1 to avoid this.

- [ ] **Step M.3: Merge W3.**

```bash
git merge --no-ff refactor/T20c-alive-enemy-helper -m "Merge branch 'refactor/T20c-alive-enemy-helper'"
```

- [ ] **Step M.4: Final verification on main.**

```bash
cmake --build --preset ninja-debug 2>&1 | tail -5
ctest --preset ninja-debug 2>&1 | tail -5
```

Expected: clean build, baseline test count.

- [ ] **Step M.5: Clean up worktrees.**

```bash
git worktree remove .worktrees/T20a-vitals-stat
git worktree remove .worktrees/T20b-enemy-stat-adapter
git worktree remove .worktrees/T20c-alive-enemy-helper
```

- [ ] **Step M.6: Report.**

Final summary to user: 3 commits merged. `apply_damage` adapter removed. `for_each_alive_enemy` helper deduped 6 loops. `Vitals` and `Enemy` numeric fields now `Stat`. Test count preserved.

---

## Acceptance criteria (post-merge)

- `git log --oneline -5` shows the three refactor commits + merge commits.
- `grep -n apply_damage src/ai/transition.cc` returns no matches.
- `grep -cE "for \(auto& e : enemies_\)" src/game/combat.cc` = 1.
- `grep -cE "for \(auto& e : state\.enemies\)" src/ai/transition.cc` = 1.
- `ctest --preset ninja-debug` passes with the W0 baseline counts.
- `Vitals` definition shows three `Stat` fields, no `int`.
- `Enemy::dark_strike_base` and `Enemy::ritual_amount` are `Stat`.

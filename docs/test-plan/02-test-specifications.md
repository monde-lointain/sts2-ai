# Part 2 — Test Case Specifications

**Companion to:** `docs/test-plan/01-ast-analysis.md`.
**Coverage target:** 100 % branch coverage, ≥ 70 % statement coverage on `src/`.
**Framework:** GoogleTest 1.14+ (`gtest`/`gmock`).
**Coverage tool:** `clang -fprofile-instr-generate -fcoverage-mapping` + `llvm-profdata` + `llvm-cov show --branch-coverage` (Clang/MSVC clang-cl).
**Constraint:** public API only — `friend class CombatTestAccess` (declared at `src/game/Combat.h:56`) is **not** used; that hook is left in place but unexercised by this plan. No process-level (`spawn sts2_fight.exe`) tests.

---

## 1. Methodology mapping

| Strategy | What it produces here | Floor count |
|---|---|---|
| **Structured Basis Testing** | One test per linearly independent path through each function's CFG. Drives **path coverage** at the basis level. | Σ cyclomatic complexity = **198** paths |
| **Branch Coverage (decision coverage)** | Both outcomes of every binary decision, plus each `case`/`default` of every `switch`, plus each operand of `&&`/`||`. | **253** branches |
| **Data-Flow Testing** | For every (def, use) pair recorded in §3 of Part 1, a path that establishes the def, then reaches the use without redefinition. All-defs ∪ all-uses targets. | One per non-trivial (def, use) pair |
| **Equivalence Partitioning** | One representative from each input partition class for each public entry point. | 1 per partition |
| **Boundary Value Analysis** | Just-inside, on, just-outside, and arithmetic min/max for every numeric input — and `empty`, `size==1`, `size==N` for every container. | 3-4 per numeric/container input |
| **Error Guessing** | Known traps: int overflow, off-by-one, empty container with `back()`-style access, negative index, EOF on `getline`, dead-on-arrival enemies, simultaneous death of both sides, etc. | targeted |

Tests are tagged with the strategies that motivate them. Many tests satisfy multiple strategies simultaneously; the tag list reflects intent and traceability, not exclusivity.

### Strategy abbreviations used in IDs

`BP` basis path · `BR` branch coverage · `DF` data flow · `EP` equivalence partition · `BV` boundary · `EG` error guessing.

### Test ID scheme

`T-<MOD>-<NNN>` where `MOD` ∈ {`RNG`, `PWR`, `DMG`, `CRD`, `ENM`, `CMB`, `RND`, `INP`, `CON`, `MAIN`}. Numbers are gap-friendly (5-step) so insertions don't reflow.

---

## 2. Refactoring preconditions

The plan assumes the following non-behavioural source changes. None alters runtime semantics; they only widen the public test surface as the user authorised.

### 2.1 Hoist TU-local helpers

| Current location | New header | Functions to hoist |
|---|---|---|
| `src/render/Render.cpp` (anon) | `src/render/Render_internal.h` | `repeat_utf8`, `spaces`, `power_color`, `power_name`, `format_powers`, `format_intent`, `max_enemy_name_len`, `total_deck_size` |
| `src/input/Input.cpp` (anon) | `src/input/Input_internal.h` | `trim`, `parse_nonneg_int` |
| `src/main.cpp` (anon) | `src/app/Args.h` + `src/app/Prompts.h` | `parse_uint64`, `parse_args`, `random_seed`, `prompt_index`, `prompt_target`, `prompt_discard` |

The internal headers stay outside the public include path users actually depend on; they exist solely to give tests a link target.

### 2.2 Stream injection in `main.cpp` prompts

`prompt_target` and `prompt_discard` currently bind to `std::cin`/`std::cout` directly. Add `std::istream& in, std::ostream& out` parameters and have `main()` pass `std::cin`, `std::cout`. `prompt_index` already takes streams. After this change all three are testable with `std::stringstream`.

### 2.3 No other code changes

`Combat`, `Rng`, all `game/` namespaces, `render/Bar`, and `input::*` already have the public surface needed. The renderer is tested by passing a `std::ostringstream` — no changes to `render::render_combat`.

---

## 3. Shared test fixtures and helpers

| Fixture | Purpose | Construction |
|---|---|---|
| `RngFixture` | Deterministic Rng | `Rng rng_{0xDEADBEEFCAFEULL};` |
| `EmptyVitalsFixture` | Default-constructed `Vitals` | `Vitals v_{};` |
| `PoweredVitalsFixture` | `Vitals` with attached powers per test | builder helpers `with_power(kind, amt)` |
| `BareCombatFixture` | `Combat` with a known seed and **no** enemies, **no** deck | `Combat c_{0x1234ULL};` |
| `StarterCombatFixture` | `Combat` seeded, starter deck loaded, two cultists added, `start()` called | wraps `make_silent_starter_deck`, `make_calcified_cultist`, `make_damp_cultist` (each with their own `Rng` so HP rolls are predictable) |
| `WoundedCombatFixture` | `StarterCombatFixture` then mutate via public API only: deal damage to bring player to 1 HP / one enemy to 1 HP | sequences of `deal_damage_to_enemy` etc. |
| `OneEnemyCombatFixture` | Same as `Bare` plus a single calcified cultist via `add_enemy` | for single-target tests |
| `CapturedSink` | `std::ostringstream` for renderer | direct |
| `ScriptedSource` | `std::istringstream` seeded with line-buffered keystrokes | `ScriptedSource{"3\nq\n"}` |

**Fixture policy:** Fixtures are used when they encapsulate multi-step setup that more than one test needs (the `StarterCombatFixture` is the canonical case). Single-line construction (`Rng r{seed};`) is preferred over a fixture that wraps a single member. The `RngFixture` and `EmptyVitalsFixture` entries below are reserved for tests that need shared mutable state across multiple `TEST_F` blocks; if a single test instantiates the object inline that's also acceptable.

**Helper matchers (gtest):**

- `HasAnsi(code)` — substring check on rendered output for ANSI escape (`"\x1b[91m"` etc.).
- `HasGlyph(utf8)` — substring check on a UTF-8 box-drawing/bullet glyph from `Glyphs.h`.
- `HpBarMatches(filled, total)` — derives the expected mix of `kFullBlock`/`kEmptyBlock` glyphs and asserts substring.

**Determinism note:** every test that involves `Rng` or `Combat` takes a hard-coded 64-bit seed. Tests that observe shuffled order do so against the *known* permutation produced by `std::mt19937_64` for that seed (precomputed in a one-time scratch run and pinned as expected values).

---

## 4. Coverage planning conventions

For each function below:

1. **CFG recap** — branches/decisions cribbed from Part 1, with a one-line note on the path structure.
2. **Equivalence partitions** — table of input partition classes for each parameter or relevant state slice.
3. **Boundary values** — ordered list of edge points to test.
4. **Error guesses** — domain-specific traps.
5. **Test cases** — numbered specs; each tagged with strategy abbreviations and the decisions it covers.
6. **Coverage tally** — at the end of the function block, the set of decisions exercised TRUE/FALSE and the def-use pairs touched. The roll-up in §13 verifies completeness.

### Common assertion legend

- `EXPECT_EQ(a, b)` — value equality.
- `EXPECT_TRUE(p)` / `EXPECT_FALSE(p)` — boolean.
- `EXPECT_THAT(s, HasSubstr(x))` — gmock matcher for substrings (renderer tests).
- `EXPECT_GE/_LE` — numeric ordering.

---

## 5. Module: `Rng` (`src/game/Rng.h`, `Rng.cpp`)

### 5.1 `Rng::Rng(uint64_t seed)` — constructor — CC=1

**CFG recap:** straight-line — `engine_(seed)` member init.

**Equivalence partitions:** seed is 64-bit unsigned; trivially 1 partition.

#### Tests

- **T-RNG-005 — BP, EP** — Deterministic seeding.
  - **Strategy:** Structured basis (single path); equivalence representative.
  - **Setup:** none.
  - **Input:** `seed = 0xDEADBEEFCAFEULL`.
  - **Action:** Construct `Rng a{seed}; Rng b{seed};`. Call `a.uniform_int(0, 1'000'000)` ten times; same for `b`.
  - **Expected:** Both 10-element sequences are identical.
  - **Covers:** `Rng::Rng` path 1; def-use `seed → engine_(seed)`.

- **T-RNG-010 — EG** — Differentiated seeds yield different sequences.
  - **Strategy:** Error guessing (regression that constructor isn't ignoring argument).
  - **Setup:** none.
  - **Input:** seeds `0` and `1`.
  - **Action:** Construct two `Rng` instances; call `uniform_int(0, 100)` 50 times each.
  - **Expected:** The two 50-element sequences differ in at least one position.
  - **Covers:** smoke that `seed` parameter actually wired to `engine_`.

### 5.2 `int Rng::uniform_int(int lo, int hi)` — CC=1

**CFG recap:** straight-line — constructs `std::uniform_int_distribution<int>`, calls `dist(engine_)`.

**Equivalence partitions for `(lo, hi)`:**

| Class | Example | Notes |
|---|---|---|
| `lo == hi` | (5, 5) | distribution returns `lo` always |
| `lo < hi`, both non-negative | (0, 9) | typical |
| `lo < hi`, both negative | (-10, -1) | sign-only flip |
| `lo < hi`, straddles 0 | (-5, 5) | typical mixed-sign |

`lo > hi` is undefined behaviour for `std::uniform_int_distribution`; not tested.

**Boundary values:** `lo = INT_MIN`, `hi = INT_MAX` (separately, with the other near 0, to avoid overflow in the distribution arithmetic).

#### Tests

- **T-RNG-015 — BP, EP** — Singleton range returns the value.
  - **Strategy:** Basis path; equivalence (`lo == hi`).
  - **Setup:** `RngFixture`.
  - **Input:** `(lo, hi) = (5, 5)`.
  - **Action:** Call `r.uniform_int(5, 5)` 100 times.
  - **Expected:** Every call returns 5.
  - **Covers:** uniform_int basis path.

- **T-RNG-020 — EP** — Non-negative range.
  - **Setup:** `RngFixture` (seed 1).
  - **Input:** `(0, 9)` × 1000 calls.
  - **Expected:** All results in `[0, 9]`; the set of distinct values returned ⊇ `{0, 9}` (both endpoints observed in 1000 samples).
  - **Covers:** range respect; output domain.

- **T-RNG-025 — EP** — Negative range.
  - **Input:** `(-10, -1)` × 1000 calls.
  - **Expected:** All in `[-10, -1]`; both endpoints observed.

- **T-RNG-030 — EP** — Mixed-sign range.
  - **Input:** `(-5, 5)` × 1000 calls.
  - **Expected:** All in `[-5, 5]`; `0` observed.

- **T-RNG-035 — BV** — Maximum-width positive range.
  - **Input:** `(0, INT_MAX)` × 100 calls.
  - **Expected:** All ≥ 0 and ≤ INT_MAX (no negative wrap-around). Statistical sanity: at least one sample > INT_MAX/2.

- **T-RNG-040 — BV** — Maximum-width negative bound.
  - **Input:** `(INT_MIN, 0)` × 100 calls.
  - **Expected:** All ≤ 0 and ≥ INT_MIN.

### 5.3 `template<T> void Rng::shuffle(std::vector<T>&)` — CC=3, branches=4

**CFG recap:**

- D1 (line 14, `if`): `v.size() < 2` → early return.
- D2 (line 15, `for`): outer Fisher-Yates loop; runs `size-1` times when entered.

Path summary: P1 (early return), P2 (loop body, all iterations through `swap`).

**Equivalence partitions for `v.size()`:** `0`, `1`, `2`, `n` (where `n ≥ 3`).

#### Tests (instantiated with `T = int`)

- **T-RNG-045 — BP, BV** — Empty vector — D1 TRUE.
  - **Setup:** `RngFixture`.
  - **Input:** `std::vector<int> v;`.
  - **Action:** `r.shuffle(v);`.
  - **Expected:** `v` remains empty; no crash.
  - **Covers:** D1 TRUE branch; basis path P1.

- **T-RNG-050 — BV** — Single element — D1 TRUE on the boundary.
  - **Input:** `std::vector<int> v{42};`.
  - **Expected:** `v == {42}`.
  - **Covers:** D1 TRUE; boundary `size == 1`.

- **T-RNG-055 — BP, BV** — Two elements — D1 FALSE, single loop iteration.
  - **Input:** `v{1, 2}`.
  - **Action:** `r.shuffle(v);` once.
  - **Expected:** `v` is a permutation of `{1, 2}` (multiset-equality). The exact order depends on seed; it is *the* permutation produced by `mt19937_64{0xDEADBEEFCAFEULL}` after burning a `uniform_int_distribution<size_t>(0, 1)` sample. Pin that as the expected value.
  - **Covers:** D1 FALSE; D2 single iteration; basis path P2 with minimal trip count.

- **T-RNG-060 — DF** — N elements — preservation and permutation.
  - **Input:** `v = {0, 1, 2, …, 9}`.
  - **Action:** `r.shuffle(v);`.
  - **Expected:** `std::is_permutation(v.begin(), v.end(), original.begin())` is TRUE; expected concrete order pinned for the seed.
  - **Covers:** D1 FALSE; D2 multiple iterations; def-use `i` (init, dec, use), `dist` (init→use), `j` (init→use→swap operand).

- **T-RNG-065 — EG** — Determinism across two calls with the same `Rng`.
  - **Strategy:** Error guessing — guard against accidentally reseeding within shuffle.
  - **Setup:** `Rng a{seed}; Rng b{seed};`.
  - **Input:** `v_a = v_b = {0..9};`.
  - **Action:** `a.shuffle(v_a); b.shuffle(v_b);`.
  - **Expected:** `v_a == v_b`.

- **T-RNG-070 — EG** — Successive shuffles consume RNG state.
  - **Setup:** `Rng a{seed};`.
  - **Input:** `v1 = v2 = {0..9};`.
  - **Action:** `a.shuffle(v1); a.shuffle(v2);`.
  - **Expected:** `v1 != v2` (probabilistically certain for 10 elements; assert as documentation that shuffle isn't snapshotting state).

### 5.4 Coverage tally — `Rng`

| Function | Decisions | TRUE covered by | FALSE covered by | Basis paths covered |
|---|---|---|---|---|
| `Rng::Rng` | — | — | — | T-RNG-005 |
| `Rng::uniform_int` | — | — | — | T-RNG-015 |
| `Rng::shuffle` | D1 | T-RNG-045, 050 | T-RNG-055, 060 | both via 045 + 060 |
| `Rng::shuffle` | D2 | T-RNG-055 (1 iter), T-RNG-060 (≥9 iters) | T-RNG-045, 050 (loop never entered) | — |

All `Rng` branches covered.

---

## 6. Module: `powers` (`src/game/Powers.h`, `Powers.cpp`)

### 6.1 `powers::find` (mutable + const overloads) — CC=3 each, branches=4 each

**CFG recap:**

- D1 (`for` range-for over `powers`): iterates each power.
- D2 (`if p.kind == kind`): match found → early return.

Path summary: P1 (empty container → fall through, return nullptr), P2 (iterate, match on first), P3 (iterate, match on later index), P4 (iterate, no match).

**Equivalence partitions for `(powers vector, kind)`:**

| Class | Example | Decision outcomes |
|---|---|---|
| Empty vector | `[]`, kind=Weak | D1 false on entry → return nullptr |
| Match at index 0 | `[Weak(2)]`, kind=Weak | D1 true; D2 true on first |
| Match at later index | `[Strength(1), Weak(3)]`, kind=Weak | D1 true; D2 false then true |
| No match (non-empty) | `[Strength(1)]`, kind=Weak | D1 true repeatedly; D2 false; loop exits → nullptr |
| Multiple of same kind | `[Weak(1), Weak(2)]`, kind=Weak | finds first |

#### Tests (mutable overload — `powers::find(std::vector<Power>&, PowerKind)`)

- **T-PWR-005 — BP, EP, BV** — Empty.
  - **Input:** `powers = {}`, `kind = PowerKind::Weak`.
  - **Expected:** returns `nullptr`.
  - **Covers:** path P1; D1 immediately FALSE.

- **T-PWR-010 — BP, EP** — Match at first index.
  - **Input:** `powers = { {Weak, 2, false} }`, `kind = Weak`.
  - **Expected:** returns pointer where `p->amount == 2 && p->kind == Weak`.
  - **Covers:** P2; D1 TRUE, D2 TRUE on iter 1.

- **T-PWR-015 — BP, EP** — Match at non-first index.
  - **Input:** `powers = { {Strength,1}, {Weak,3} }`, `kind = Weak`.
  - **Expected:** returns pointer to second element.
  - **Covers:** P3; D2 FALSE then TRUE.

- **T-PWR-020 — BP, EP** — No match.
  - **Input:** `powers = { {Strength,1} }`, `kind = Weak`.
  - **Expected:** returns `nullptr`.
  - **Covers:** P4; D1 TRUE, D2 FALSE, loop exits.

- **T-PWR-025 — EG** — First-match semantics with duplicates.
  - **Input:** `powers = { {Weak,1}, {Weak,2} }`, `kind = Weak`.
  - **Expected:** returns pointer to **first** element (`amount == 1`).
  - **Rationale:** documents and locks down the linear-search "first hit" contract that `powers::apply` depends on (see T-PWR-050).

- **T-PWR-030 — DF** — Mutability through returned pointer.
  - **Input:** `powers = { {Weak, 2} }`, `kind = Weak`.
  - **Action:** `auto* p = powers::find(v, Weak); p->amount = 99;`.
  - **Expected:** `v[0].amount == 99`.
  - **Covers:** def-use chain `find → caller assigns through pointer → underlying vector mutated`.

#### Tests (const overload — same structure)

- **T-PWR-035 — BP** — Empty (const) → `nullptr`.
- **T-PWR-040 — BP** — Match (const) — pointer's `kind`/`amount` reflect element.
- **T-PWR-045 — BP** — No-match (const) → `nullptr`.

(Const overload shares CFG with mutable; three tests is the structured-basis floor.)

### 6.2 `powers::amount(const std::vector<Power>&, PowerKind)` — CC=2

**CFG recap:** call `find`; ternary `p ? p->amount : 0`.

#### Tests

- **T-PWR-050 — BP** — Power not present → 0.
  - **Input:** `[]`, kind=Strength.
  - **Expected:** returns `0`.
  - **Covers:** ternary FALSE branch.

- **T-PWR-055 — BP, EP** — Power present → its amount.
  - **Input:** `[ {Strength, 4} ]`, kind=Strength.
  - **Expected:** returns `4`.
  - **Covers:** ternary TRUE branch.

- **T-PWR-060 — BV** — Negative amount returned literally.
  - **Input:** `[ {Strength, -2} ]`, kind=Strength.
  - **Expected:** returns `-2`.
  - **Rationale:** `amount` is a thin accessor — guarantees no clamping at this layer; clamping is in `damage::compute_outgoing`.

### 6.3 `powers::apply(std::vector<Power>&, PowerKind, int)` — CC=3, branches=4

**CFG recap:**

- D1 (line 25, `if existing = find(...)`): power already present.
- D2 (line 27, `if kind == Ritual`): set `just_applied`.
- D3 (line 30, `kind == Ritual` ternary inside `Power{...}`): the just_applied flag in the new struct's initializer for the *not-existing* path.

Path summary: P1 (existing, Ritual), P2 (existing, non-Ritual), P3 (new, Ritual), P4 (new, non-Ritual).

#### Tests

- **T-PWR-065 — BP, EP** — New non-Ritual power → push_back.
  - **Input:** `[]`, kind=Weak, amt=2.
  - **Expected:** vector becomes `[ {Weak, 2, false} ]`.
  - **Covers:** D1 FALSE → P4; ternary in initializer FALSE.

- **T-PWR-070 — BP, EP** — New Ritual power → push_back with `just_applied=true`.
  - **Input:** `[]`, kind=Ritual, amt=3.
  - **Expected:** vector becomes `[ {Ritual, 3, true} ]`.
  - **Covers:** P3; ternary in initializer TRUE.

- **T-PWR-075 — BP** — Existing non-Ritual power → amount accumulates.
  - **Input:** `[ {Weak, 2, false} ]`, kind=Weak, amt=1.
  - **Expected:** vector becomes `[ {Weak, 3, false} ]`. `just_applied` unchanged.
  - **Covers:** D1 TRUE; D2 FALSE → P2.

- **T-PWR-080 — BP** — Existing Ritual power → amount accumulates and `just_applied=true`.
  - **Input:** `[ {Ritual, 2, false} ]`, kind=Ritual, amt=1.
  - **Expected:** `[ {Ritual, 3, true} ]`.
  - **Covers:** D1 TRUE; D2 TRUE → P1.

- **T-PWR-085 — EG, BV** — Apply zero amount.
  - **Input:** `[]`, kind=Weak, amt=0.
  - **Expected:** vector becomes `[ {Weak, 0, false} ]` (no special handling — documents the contract that `apply(_, _, 0)` still creates the entry).
  - **Rationale:** locks behaviour because `tick_at_turn_end` may erase a Weak that drops to 0; an apply(0) creates a corpse-on-arrival.

- **T-PWR-090 — EG** — Apply negative amount.
  - **Input:** `[ {Strength, 2} ]`, kind=Strength, amt=-3.
  - **Expected:** `[ {Strength, -1, false} ]`.
  - **Rationale:** locks accumulation arithmetic; the `damage` layer handles the negative-Strength implication.

### 6.4 `powers::tick_at_turn_end(std::vector<Power>&)` — CC=6, branches=10

**CFG recap (Part 1, line 1058):**

- D1 (line 35, `if Power* ritual = find(... Ritual)`): Ritual present.
- D2 (line 36, `if ritual->just_applied`): suppress this turn's gain.
- D3 (line 43, `for it = begin; it != end;`): iteration over the powers list with manual increment / erase.
- D4 (line 44, `if it->kind == Weak`): only Weak ticks.
- D5 (line 46, `if it->amount <= 0`): erase when expired.

Important interaction: `apply(powers, Strength, gain)` may push_back into `powers` while a `for it != end()` iterator is held — but it isn't; the `apply` happens *before* the `for` loop, so iterators aren't invalidated mid-loop. Locking that ordering is itself a test.

**Equivalence partitions:**

| Class | Description |
|---|---|
| `R0`: no Ritual, no Weak | tick is a no-op |
| `R1`: Ritual just_applied, no Weak | clears just_applied; no Strength gain |
| `R2`: Ritual not just_applied, no Weak | adds Strength = ritual.amount |
| `R3`: Ritual not just_applied, no existing Strength | apply creates new Strength entry |
| `R4`: Ritual not just_applied, existing Strength | strength accumulates |
| `W0`: Weak amount > 1 | decrements |
| `W1`: Weak amount == 1 | decrements then erases |
| `W2`: Weak amount <= 0 (corpse) | erases first iteration |

#### Tests

- **T-PWR-100 — BP, EP** — `R0`/no Weak — empty no-op.
  - **Input:** `powers = []`.
  - **Expected:** still `[]`.
  - **Covers:** D1 FALSE; D3 immediate exit (no body).

- **T-PWR-105 — BP** — `R1` — Ritual just-applied → clears flag.
  - **Input:** `powers = [ {Ritual, 2, just_applied=true} ]`.
  - **Expected:** `[ {Ritual, 2, false} ]`. No Strength entry created.
  - **Covers:** D1 TRUE; D2 TRUE.

- **T-PWR-110 — BP** — `R3` — Ritual normal → new Strength.
  - **Input:** `powers = [ {Ritual, 2, false} ]`.
  - **Expected:** `[ {Ritual, 2, false}, {Strength, 2, false} ]` (order: existing then push_back).
  - **Covers:** D1 TRUE; D2 FALSE; entry into `apply(..., Strength, gain)` push_back path.

- **T-PWR-115 — BP** — `R4` — Ritual normal with existing Strength → accumulates.
  - **Input:** `powers = [ {Ritual, 3, false}, {Strength, 1, false} ]`.
  - **Expected:** `[ {Ritual, 3, false}, {Strength, 4, false} ]`.
  - **Covers:** D1 TRUE; D2 FALSE; `apply` D1 TRUE branch.

- **T-PWR-120 — BP, EP** — `W0` — Weak amount > 1 ticks down.
  - **Input:** `[ {Weak, 3} ]`.
  - **Expected:** `[ {Weak, 2} ]`.
  - **Covers:** D3 enters; D4 TRUE; D5 FALSE; `++it` increment branch.

- **T-PWR-125 — BP, BV** — `W1` — Weak amount == 1 → erase.
  - **Input:** `[ {Weak, 1} ]`.
  - **Expected:** `[]`.
  - **Covers:** D3 enters; D4 TRUE; D5 TRUE → erase + continue.

- **T-PWR-130 — EG** — `W2` — Weak amount == 0 corpse → erase.
  - **Input:** `[ {Weak, 0} ]`.
  - **Expected:** `[]`.

- **T-PWR-135 — EG** — `W2` negative — Weak amount == -1 corpse → erase (decrement to -2 then erase).
  - **Input:** `[ {Weak, -1} ]`.
  - **Expected:** `[]`.
  - **Rationale:** `<= 0` includes negatives; locks the comparison operator.

- **T-PWR-140 — DF** — Mixed list ordering preserved.
  - **Input:** `[ {Strength,2}, {Weak,2}, {Ritual,1, false} ]`.
  - **Action:** `tick_at_turn_end(v)`.
  - **Expected:** Ritual handler runs first (adds Strength → existing Strength becomes 3); then Weak loop ticks Weak from 2→1.
  - **Final:** `[ {Strength,3}, {Weak,1}, {Ritual,1, false} ]` (note: order matches insertion; Ritual handler doesn't move entries).
  - **Covers:** Ritual → Weak ordering invariant; D1, D2 FALSE, D3 multi-iter, D4 FALSE then TRUE, D5 FALSE.

- **T-PWR-145 — EG** — Two Weaks: first ticks to 0 and erases mid-iteration, second still processed.
  - **Input:** `[ {Weak, 1}, {Weak, 3} ]`.
  - **Expected:** After tick → `[ {Weak, 2} ]` (first erased, second decremented).
  - **Covers:** D3 with `it = erase(it)` continue path; verifies iterator safety post-erase.

- **T-PWR-150 — EG** — Weak first, Strength second — Strength preserved.
  - **Input:** `[ {Weak, 2}, {Strength, 4} ]`.
  - **Expected:** `[ {Weak, 1}, {Strength, 4} ]`.
  - **Covers:** D4 FALSE branch (`++it` past Strength).

### 6.5 Coverage tally — `powers`

| Function | Decisions | TRUE | FALSE |
|---|---|---|---|
| `find` (each overload) | D1, D2 | T-PWR-010/015/025 | T-PWR-005/020 |
| `amount` | ternary | T-PWR-055/060 | T-PWR-050 |
| `apply` | D1, D2 | T-PWR-075/080 | T-PWR-065/070 |
| `apply` (ternary in initializer) | — | T-PWR-070 | T-PWR-065 |
| `tick_at_turn_end` D1 | — | 105/110/115/140 | 100/120/125/130 |
| `tick_at_turn_end` D2 | — | 105 | 110/115/140 |
| `tick_at_turn_end` D3 | enter | 120/125/130/140/145/150 | 100/105 |
| `tick_at_turn_end` D4 | — | 120/125/130/135/145/150 | 140/150 |
| `tick_at_turn_end` D5 | — | 125/130/135/145 | 120/140 |

All `powers` branches covered.

---

## 7. Module: `damage` (`src/game/Damage.h`, `Damage.cpp`)

### 7.1 `damage::compute_outgoing(const std::vector<Power>& attacker_powers, int base_damage)` — CC=3, branches=4

**CFG recap:**

- D1 (line 11, `if powers::amount(... Weak) > 0`): apply 0.75 multiplier.
- D2 (line 14, ternary `d < 0 ? 0 : d`): clamp negative to zero.

**Equivalence partitions:**

| Class | base | Strength | Weak | Expected (formula) |
|---|---|---|---|---|
| Plain | 6 | none | none | 6 |
| Strength positive | 6 | +2 | none | 8 |
| Strength negative | 6 | -3 | none | 3 |
| Weak only | 6 | none | 1 | floor(6 * 0.75) = 4 |
| Both | 6 | +4 | 1 | floor((6+4)*0.75) = 7 |
| Negative result clamped | 0 | -10 | none | 0 |
| Negative result + Weak | 0 | -10 | 1 | floor(-10 * 0.75) = -7 → clamped to 0 |

**Boundary values for `base_damage`:** 0, 1, INT_MAX (overflow guard sanity).

#### Tests

- **T-DMG-005 — BP, EP** — Plain attack — D1 FALSE, D2 FALSE.
  - **Input:** `attacker_powers = []`, `base_damage = 6`.
  - **Expected:** returns 6.

- **T-DMG-010 — EP, DF** — Strength adds.
  - **Input:** `[ {Strength, 2} ]`, base 6.
  - **Expected:** 8.

- **T-DMG-015 — EP** — Negative Strength subtracts.
  - **Input:** `[ {Strength, -3} ]`, base 6.
  - **Expected:** 3.

- **T-DMG-020 — BP** — Weak applies multiplier — D1 TRUE.
  - **Input:** `[ {Weak, 1} ]`, base 6.
  - **Expected:** 4 (`int(6 * 0.75) = 4`).

- **T-DMG-025 — EP** — Strength + Weak.
  - **Input:** `[ {Strength, 4}, {Weak, 1} ]`, base 6.
  - **Expected:** 7 (`int(10 * 0.75) = 7`).

- **T-DMG-030 — BP, BV** — Negative result clamped — D2 TRUE.
  - **Input:** `[ {Strength, -10} ]`, base 0.
  - **Expected:** 0.

- **T-DMG-035 — EG** — Weak ignored when amount is exactly 0.
  - **Input:** `[ {Weak, 0} ]`, base 6.
  - **Expected:** 6 (D1 FALSE because the predicate is `> 0`, not `>= 0`).
  - **Covers:** boundary on the `> 0` predicate.

- **T-DMG-040 — BV** — `base_damage = 0`, no powers.
  - **Expected:** 0.

- **T-DMG-045 — EG** — Truncation direction is integer (no rounding).
  - **Input:** `[ {Weak, 1} ]`, base 7.
  - **Expected:** 5 (`7 * 0.75 = 5.25 → 5`).
  - **Rationale:** locks `static_cast<int>(d * 0.75)` truncation behaviour.

- **T-DMG-050 — EG** — Multiple Weak entries (degenerate state) — only first is consulted via `amount`.
  - **Input:** `[ {Weak, 1}, {Weak, 5} ]`, base 6.
  - **Expected:** 4. (`amount` returns first match's value; both `> 0` so D1 still TRUE; multiplier still 0.75 once.)
  - **Rationale:** locks `amount`'s "first-match" interaction with `compute_outgoing`'s "any Weak triggers debuff" intent.

### 7.2 `damage::apply_to_defender(Vitals& target, int incoming)` — CC=3, branches=4

**CFG recap:**

- D1 (line 18, `if incoming <= target.block`): block fully absorbs.
- D2 (line 24, ternary `incoming < target.hp ? incoming : target.hp`): clamp HP loss.

**Equivalence partitions:**

| Class | block | hp | incoming | Expected hp_loss |
|---|---|---|---|---|
| Block fully absorbs | 5 | 10 | 3 | 0 |
| Block exactly absorbs | 5 | 10 | 5 | 0 |
| Bleed-through partial | 3 | 10 | 5 | 2 |
| HP cap (overkill) | 0 | 4 | 9 | 4 |
| HP cap with block | 2 | 4 | 9 | 4 |
| Zero damage | 0 | 10 | 0 | 0 |

#### Tests

- **T-DMG-055 — BP, BV** — Block fully absorbs — D1 TRUE.
  - **Setup:** `Vitals v{ hp=10, max_hp=10, block=5 }`.
  - **Input:** `incoming = 3`.
  - **Expected:** returns 0; `v.block == 2`; `v.hp == 10`.

- **T-DMG-060 — BV** — Block exactly absorbs.
  - **Setup:** `Vitals v{10, 10, 5}`.
  - **Input:** 5.
  - **Expected:** returns 0; `v.block == 0`; `v.hp == 10`.

- **T-DMG-065 — BP, BV** — Bleed-through — D1 FALSE, D2 FALSE (incoming < hp).
  - **Setup:** `Vitals v{10, 10, 3}`.
  - **Input:** 5.
  - **Expected:** returns 2; `v.block == 0`; `v.hp == 8`.

- **T-DMG-070 — BP, BV** — Overkill clamps to hp — D2 TRUE.
  - **Setup:** `Vitals v{4, 10, 0}`.
  - **Input:** 9.
  - **Expected:** returns 4; `v.block == 0`; `v.hp == 0`.

- **T-DMG-075 — BV** — Overkill with block — block first, then hp clamp.
  - **Setup:** `Vitals v{4, 10, 2}`.
  - **Input:** 9.
  - **Expected:** returns 4 (`9 - 2 = 7 > 4 → loss == 4`); `v.block == 0`; `v.hp == 0`.

- **T-DMG-080 — BV** — Zero incoming, zero block.
  - **Setup:** `Vitals v{10, 10, 0}`.
  - **Input:** 0.
  - **Expected:** D1 TRUE (0 <= 0); returns 0; `v.block == 0`; `v.hp == 10`.

- **T-DMG-085 — EG** — Negative incoming.
  - **Setup:** `Vitals v{10, 10, 0}`.
  - **Input:** -3.
  - **Expected:** D1 TRUE (-3 <= 0); returns 0; `v.block == 3` (`block -= -3`).
  - **Rationale:** locks the surprising arithmetic — callers (`Combat::deal_damage_to_enemy`) must never pass negatives. This test pins the current behaviour and documents the contract.

- **T-DMG-090 — EG** — `incoming` exactly equals hp (lethal-but-not-overkill).
  - **Setup:** `Vitals v{4, 10, 0}`.
  - **Input:** 4.
  - **Expected:** D1 FALSE (4 > 0); D2 FALSE (4 < 4 is FALSE → uses `target.hp` branch); returns 4; `v.hp == 0`.
  - **Covers:** D2 FALSE with equality boundary.

### 7.3 Coverage tally — `damage`

| Function | Decisions | TRUE | FALSE |
|---|---|---|---|
| `compute_outgoing` D1 | — | 020/025/035 | 005/010/015/030/045 |
| `compute_outgoing` D2 (ternary) | — | 030 | 005/010/015/020/025/045 |
| `apply_to_defender` D1 | — | 055/060/080/085 | 065/070/075/090 |
| `apply_to_defender` D2 (ternary) | — | 070/075 | 065/090 |

All `damage` branches covered.

---

## 8. Module: `cards` (`src/game/Cards.h`, `Cards.cpp`)

The four `make_*` functions are factories: CC=1 each, no decisions. Test their static fields and then exercise the `on_play` lambda through a `BareCombatFixture` to cover the closure body — that's where the per-card branches live (and where def-use of `base` is closed via the lambda capture).

### 8.1 `cards::make_strike()` — CC=1

#### Tests

- **T-CRD-005 — BP** — Static fields.
  - **Action:** `Card c = cards::make_strike();`.
  - **Expected:**
    - `c.id == CardId::Strike`
    - `c.name == "Strike"`
    - `c.cost == 1`
    - `c.type == CardType::Attack`
    - `c.target == TargetType::AnyEnemy`
    - `c.base_damage == 6`
    - `c.short_stats == "6dmg"`
    - `c.description == {"Deal 6 damage."}`
    - `static_cast<bool>(c.on_play) == true`

- **T-CRD-010 — DF** — `on_play` deals `base_damage` to the targeted enemy.
  - **Setup:** `OneEnemyCombatFixture` (cultist with hp=40 set by `Vitals v{40,40,0}` — bypass the random-roll factory by adding a hand-built Enemy via `add_enemy`).
  - **Action:** `c.on_play(combat, /*target_idx=*/0);`.
  - **Expected:** `combat.enemies()[0].vitals.hp == 34` (40 - 6).
  - **Covers:** lambda body; capture `base = 6` is the def, the `deal_damage_to_enemy(0, base)` is the use.

- **T-CRD-015 — EG** — Lambda is value-captured (later mutation of card field doesn't affect closure).
  - **Setup:** as above.
  - **Action:** `Card c = make_strike(); c.base_damage = 999; c.on_play(combat, 0);`.
  - **Expected:** Damage is **6**, not 999. Locks the `[base = c.base_damage]` capture-by-copy contract.

### 8.2 `cards::make_defend()` — CC=1

- **T-CRD-020 — BP** — Static fields. (id=Defend, cost=1, type=Skill, target=Self, base_block=5, "5blk").
- **T-CRD-025 — DF** — `on_play` adds 5 block via `gain_player_block`.
  - **Setup:** `BareCombatFixture` (player block starts at 0).
  - **Action:** `c.on_play(combat, /*target_idx=*/-1);`.
  - **Expected:** `combat.player().vitals.block == 5`.
- **T-CRD-030 — EG** — Capture-by-copy (mirror of T-CRD-015).

### 8.3 `cards::make_neutralize()` — CC=1

- **T-CRD-035 — BP** — Static fields. (id=Neutralize, cost=0, base_damage=3, target=AnyEnemy, description has 2 lines).
- **T-CRD-040 — DF** — `on_play` deals 3 damage AND applies Weak 1.
  - **Setup:** `OneEnemyCombatFixture` (hp=40, no powers).
  - **Action:** `c.on_play(combat, 0);`.
  - **Expected:** `combat.enemies()[0].vitals.hp == 37`; Weak power present with amount 1.
  - **Covers:** lambda body order — damage **before** Weak (matters because incoming-Weak doesn't reduce *this* attack since we're dealing from player to enemy and reading **player** Strength/Weak; but locks call ordering for any future source-Weak interaction).

### 8.4 `cards::make_survivor()` — CC=1

- **T-CRD-045 — BP** — Static fields. (id=Survivor, cost=1, base_block=8, "8blk", 2-line description).
- **T-CRD-050 — DF** — `on_play` gains 8 block AND triggers `discard_chosen_from_hand`.
  - **Setup:** `BareCombatFixture`. Manually add 2 cards to player's hand via the public start path: actually, since `BareCombatFixture` exposes no private mutator, set up via `StarterCombatFixture` after `start()` so the hand has cards; then push a sentinel card via the deck before start and ensure pick-discard callback returns 0.
  - **Refined setup:** `Combat c{seed}; c.set_pick_discard_callback([](const Combat&){ return 0; }); c.start({make_strike(), make_defend(), make_strike()});` (small deck → starting hand of 3).
  - **Action:** `auto card = make_survivor(); card.on_play(c, -1);`.
  - **Expected:** `c.player().vitals.block == 8`; `c.player().hand.size() == 2`; `c.player().discard_pile.size() == 1`.
  - **Covers:** lambda body (block + discard).

- **T-CRD-055 — EG** — `on_play` invoked with empty hand is a no-op for the discard side.
  - **Setup:** `BareCombatFixture` with empty hand.
  - **Action:** `make_survivor().on_play(c, -1);`.
  - **Expected:** block becomes 8; no crash; hand still empty; discard still empty.
  - **Covers:** delegated `Combat::discard_chosen_from_hand` early-out path (D1 TRUE).

### 8.5 `cards::make_silent_starter_deck()` — CC=3, branches=4

**CFG recap:** two for-loops over `[0..5)`. Decisions D1 = first for, D2 = second for.

#### Tests

- **T-CRD-060 — BP** — Deck size and contents.
  - **Action:** `auto deck = make_silent_starter_deck();`.
  - **Expected:**
    - `deck.size() == 12`
    - count of `id == Strike` is 5
    - count of `id == Defend` is 5
    - count of `id == Neutralize` is 1
    - count of `id == Survivor` is 1
  - **Covers:** D1 enters, D2 enters, both fall through; basis path.

- **T-CRD-065 — DF** — Order of construction.
  - **Expected:** the first 5 cards are `Strike`, the next 5 are `Defend`, then `Neutralize`, then `Survivor`. (Pre-shuffle order matters for tests that pin a specific seed's draw sequence.)

### 8.6 Coverage tally — `cards`

| Function | Branch coverage source |
|---|---|
| `make_strike` | T-CRD-005, 010, 015 |
| `make_defend` | T-CRD-020, 025, 030 |
| `make_neutralize` | T-CRD-035, 040 |
| `make_survivor` | T-CRD-045, 050, 055 |
| `make_silent_starter_deck` D1, D2 | T-CRD-060 |

All `cards` branches covered.

---

## 9. Module: `enemies` (`src/game/Enemies.h`, `Enemies.cpp`)

### 9.1 `enemies::make_calcified_cultist(Rng&)` — CC=1

#### Tests

- **T-ENM-005 — BP, BV** — HP rolled in `[38, 41]` and stable for fixed seed.
  - **Setup:** `Rng r{0x42ULL};`.
  - **Action:** `Enemy e = make_calcified_cultist(r);`.
  - **Expected:**
    - `e.name == "Calcified Cultist"`
    - `e.vitals.max_hp == e.vitals.hp`
    - `e.vitals.hp ∈ [38, 41]`
    - `e.dark_strike_base == 9`
    - `e.ritual_amount == 2`
    - `e.current_move == MoveId::Incantation`
    - `e.performed_first_move == false`
    - `e.vitals.block == 0`
  - **Covers:** factory invariants; def-use `hp` (init from rng → assigned to both `max_hp` and `hp`).

- **T-ENM-010 — EG, BV** — All four HP roll outcomes are reachable.
  - **Strategy:** Exhaustively pick four seeds (precomputed) such that `r.uniform_int(38, 41)` returns 38, 39, 40, 41 respectively.
  - **Expected:** `e.vitals.hp` matches each.
  - **Rationale:** locks the closed-interval contract on `uniform_int` and that the cultist constructor doesn't accidentally clamp.

### 9.2 `enemies::make_damp_cultist(Rng&)` — CC=1

- **T-ENM-015 — BP, BV** — HP rolled in `[51, 53]`; constants `dark_strike_base=1, ritual_amount=5`.
- **T-ENM-020 — EG, BV** — All three HP roll outcomes reachable (51, 52, 53).

### 9.3 `enemies::roll_next_move(Enemy&)` — CC=3, branches=4

**CFG recap:**

- D1 (line 33, `if !e.performed_first_move`): on first call.
- D2 (line 37, `if e.current_move == Incantation`): switch to DarkStrike.

Path summary: P1 (first-move latch), P2 (Incantation → DarkStrike), P3 (already DarkStrike → no-op).

#### Tests

- **T-ENM-025 — BP, EP** — First call latches `performed_first_move`.
  - **Setup:** `Enemy e{}; e.current_move = MoveId::Incantation; e.performed_first_move = false;`.
  - **Action:** `roll_next_move(e);`.
  - **Expected:** `e.performed_first_move == true`; `e.current_move == Incantation` (unchanged).
  - **Covers:** D1 TRUE → P1.

- **T-ENM-030 — BP, EP** — Incantation → DarkStrike.
  - **Setup:** `e.current_move = Incantation; e.performed_first_move = true;`.
  - **Action:** `roll_next_move(e);`.
  - **Expected:** `e.current_move == DarkStrike`.
  - **Covers:** D1 FALSE; D2 TRUE → P2.

- **T-ENM-035 — BP** — DarkStrike stays.
  - **Setup:** `e.current_move = DarkStrike; e.performed_first_move = true;`.
  - **Action:** `roll_next_move(e);`.
  - **Expected:** `e.current_move == DarkStrike` (unchanged).
  - **Covers:** D1 FALSE; D2 FALSE → P3.

- **T-ENM-040 — DF** — Move sequence over four calls.
  - **Setup:** fresh enemy (Incantation, false).
  - **Action:** call `roll_next_move` 4 times, recording `current_move` after each.
  - **Expected:** sequence is `[Incantation, DarkStrike, DarkStrike, DarkStrike]`.
  - **Covers:** P1 then P2 then P3 twice.

### 9.4 `enemies::act(Enemy&, Combat&)` — CC=3, branches=2

**CFG recap:** switch with two cases (Incantation, DarkStrike), no default.

#### Tests

- **T-ENM-045 — BP, EP** — Incantation applies Ritual to self.
  - **Setup:** `BareCombatFixture` (`Combat c{seed}`); manually built `Enemy e{}` with `ritual_amount = 2`, `current_move = Incantation`.
  - **Action:** `enemies::act(e, c);`.
  - **Expected:** `e.vitals.powers` contains `{Ritual, 2, just_applied=true}`.
  - **Covers:** case Incantation; def-use `ritual_amount → apply_power_to_enemy_self`.

- **T-ENM-050 — BP, EP** — DarkStrike attacks player.
  - **Setup:** `BareCombatFixture`; `Enemy e{}` with `dark_strike_base = 9`, `current_move = DarkStrike`. (Player has full HP via Player default `Vitals{70,70,0}`.)
  - **Action:** `enemies::act(e, c);`.
  - **Expected:** `c.player().vitals.hp == 61` (70 - 9; Strength/Weak source is the enemy, who has none, so raw 9).
  - **Covers:** case DarkStrike.

- **T-ENM-055 — DF** — DarkStrike with Strength on enemy adds.
  - **Setup:** as above, but `e.vitals.powers = [ {Strength, 2} ]`.
  - **Expected:** `c.player().vitals.hp == 70 - 11 == 59`.
  - **Covers:** integration with `damage::compute_outgoing`.

- **T-ENM-060 — DF** — DarkStrike with Weak on enemy halves (rounds down).
  - **Setup:** `e.vitals.powers = [ {Weak, 1} ]`, base 9.
  - **Expected:** `9 * 0.75 = 6.75 → 6`. Player hp drops by 6.

### 9.5 Coverage tally — `enemies`

| Function | Decisions | Covered by |
|---|---|---|
| `make_calcified_cultist` | — | 005, 010 |
| `make_damp_cultist` | — | 015, 020 |
| `roll_next_move` D1, D2 | — | 025/030/035 + 040 |
| `act` switch cases | both | 045, 050 |

All `enemies` branches covered.

---

## 10. Module: `Combat` (`src/game/Combat.h`, `Combat.cpp`)

The largest unit. 25 method definitions; ΣCC ≈ 50; Σbranches = 60. Public API is wide and the state machine has implicit ordering invariants ("Ritual ticks before Weak", "block resets only after round 1", "enemy phase tick happens last", etc.) that drive most of the tests below.

All tests use a fixed seed (`0xC0FFEEULL` unless stated). Concrete expected card orders, HP rolls, and shuffle outcomes are pinned to that seed via a one-time scratch run.

### 10.1 Accessors and trivial mutators (CC=1)

#### `Combat::Combat(uint64_t seed)`, `start(...)`, `start` triggers `start_player_turn`

- **T-CMB-005 — BP** — Constructor leaves `combat_over_=false`, `round_=1`, no enemies, empty player piles.
  - **Action:** `Combat c{seed};`.
  - **Expected:**
    - `c.combat_over() == false`
    - `c.round() == 1`
    - `c.player().energy == 0` (energy is set in `start_player_turn`)
    - `c.player().vitals.hp == 70 && c.player().vitals.max_hp == 70`
    - `c.enemies().empty()`
    - `c.player().draw_pile.empty() && c.player().hand.empty() && c.player().discard_pile.empty()`

- **T-CMB-010 — BP, DF** — `start(deck)` shuffles, resets round, calls `start_player_turn` (drawing 5+2).
  - **Setup:** `Combat c{seed};` add the two cultists; small fixed deck `{Strike, Defend, Strike, Defend, Strike, Defend, Strike}` (size 7).
  - **Action:** `c.start(small_deck);`.
  - **Expected:**
    - `c.round() == 1`
    - `c.combat_over() == false`
    - `c.player().hand.size() == 7` *or* clamped to deck size (deck = 7, draw target = 7, so all moved into hand)
    - `c.player().draw_pile.empty()`
    - `c.player().discard_pile.empty()`
    - `c.player().energy == 3` (set in `start_player_turn`)
  - **Covers:** `start` body; transitively `start_player_turn` round=1 path.

- **T-CMB-015 — DF** — Starter deck of 12 with Ring-of-the-Snake bonus draws 7 cards on turn 1.
  - **Setup:** `Combat c{seed};` add cultists; `c.start(make_silent_starter_deck());`.
  - **Expected:** `c.player().hand.size() == 7`; `c.player().draw_pile.size() == 5`.

#### `add_enemy`, `set_pick_discard_callback`

- **T-CMB-020 — BP** — `add_enemy` appends; multiple appends preserve order.
  - **Action:** `c.add_enemy(make_calcified_cultist(rng)); c.add_enemy(make_damp_cultist(rng));`.
  - **Expected:** `c.enemies().size() == 2`; names are "Calcified Cultist" then "Damp Cultist".

- **T-CMB-025 — BP, DF** — `set_pick_discard_callback` is consulted by `discard_chosen_from_hand`.
  - **Setup:** small fixed deck so hand has known cards; install callback that always returns 1.
  - **Action:** call `c.discard_chosen_from_hand()`.
  - **Expected:** the card at hand index 1 (pre-call) is moved to discard; index 0 and 2 remain.
  - **Covers:** `set_pick_discard_callback` storage; `discard_chosen_from_hand` D2 FALSE path (idx valid).

#### Pure delegators

- **T-CMB-030 — BP** — `gain_player_block(5)` adds 5 to `player_.vitals.block`. Successive calls accumulate.
- **T-CMB-035 — BP** — `apply_power_to_enemy(0, Weak, 1)` delegates to `powers::apply` on enemy 0's powers vector. Re-apply accumulates.
- **T-CMB-040 — BP** — `apply_power_to_enemy_self(e, Ritual, 2)` delegates correctly (verified by reading the passed-in `Enemy`'s powers afterwards).
- **T-CMB-045 — BP** — `is_player_dead()` returns TRUE when `player.vitals.hp <= 0`.
  - Setup hp=0 by dealing exactly 70 damage with no block; FALSE when hp > 0.

#### `deal_damage_to_enemy`, `enemy_attack_player`

- **T-CMB-050 — BP, DF** — `deal_damage_to_enemy(0, base)` reads player's powers, computes via `damage::compute_outgoing`, applies via `damage::apply_to_defender`, calls `check_win_or_lose`.
  - **Setup:** `BareCombatFixture` + add enemy with `vitals = {40, 40, 0}` no powers. Player has no powers.
  - **Action:** `c.deal_damage_to_enemy(0, 6);`.
  - **Expected:** `c.enemies()[0].vitals.hp == 34`; `c.combat_over() == false`.

- **T-CMB-055 — EG** — Lethal `deal_damage_to_enemy` doesn't trip `combat_over` while another enemy lives.
  - **Setup:** two enemies, one at 1 hp, one at 40 hp.
  - **Action:** `c.deal_damage_to_enemy(0, 99);`.
  - **Expected:** enemy 0 hp=0; enemy 1 hp=40; `combat_over() == false` (the other enemy is still alive).

- **T-CMB-060 — EG** — Lethal `deal_damage_to_enemy` to the **last** enemy trips `combat_over` (via `all_enemies_dead`).
  - **Setup:** single enemy at 1 hp.
  - **Action:** `c.deal_damage_to_enemy(0, 99);`.
  - **Expected:** `combat_over() == true`.

- **T-CMB-065 — BP, DF** — `enemy_attack_player(e, base)` reads `e.vitals.powers` (not the player's), applies to player.
  - **Setup:** `BareCombatFixture`; build `Enemy e` with `vitals.powers = [ {Strength, 2} ]`.
  - **Action:** `c.enemy_attack_player(e, 9);`.
  - **Expected:** `c.player().vitals.hp == 70 - 11 == 59`.

- **T-CMB-070 — EG** — Lethal `enemy_attack_player` trips `combat_over`.
  - **Setup:** player hp=1.
  - **Action:** `c.enemy_attack_player(e, 5);`.
  - **Expected:** `combat_over() == true`; `is_player_dead() == true`.

### 10.2 `Combat::can_play(int hand_idx) const` — CC=3, branches=4

**CFG recap:**

- D1: `hand_idx < 0 || hand_idx >= hand.size()` (short-circuit `||`).
- D2: `card.cost <= player_.energy`.

**Equivalence partitions for `(hand_idx, hand size, card cost vs energy)`:**

| Class | Setup | Expected |
|---|---|---|
| Negative idx | hand=[*,*,*], idx=-1 | false |
| Idx out of range | hand=[*,*,*], idx=3 | false |
| In range, affordable | hand=[Strike(1)], idx=0, energy=1 | true |
| In range, unaffordable | hand=[Strike(1)], idx=0, energy=0 | false |
| Boundary cost == energy | hand=[Strike(1)], idx=0, energy=1 | true |
| Cost 0 with 0 energy | hand=[Neutralize(0)], idx=0, energy=0 | true |

#### Tests

- **T-CMB-075 — BP, BV** — `idx == -1` → false (left operand of `||` short-circuits).
  - **Covers:** D1 TRUE via left operand; `||` short-circuit.

- **T-CMB-080 — BP, BV** — `idx == hand.size()` (just past) → false.
  - **Covers:** D1 TRUE via right operand; `||` evaluated through.

- **T-CMB-085 — BP, BV** — `idx == hand.size() - 1` (last valid) and cost > energy → false.
  - **Setup:** hand has 3 cards (last is Strike cost 1), energy = 0.
  - **Action:** `can_play(2)`.
  - **Expected:** false.
  - **Covers:** D1 FALSE; D2 FALSE.

- **T-CMB-090 — BP** — Valid idx, cost equal to energy → true.
  - **Setup:** hand `[Strike]`, energy 1.
  - **Action:** `can_play(0)`.
  - **Expected:** true.
  - **Covers:** D1 FALSE; D2 TRUE (boundary `cost == energy`).

- **T-CMB-095 — EG** — Valid idx, cost 0 with 0 energy → true (boundary `0 <= 0`).
  - **Setup:** hand `[Neutralize]`, energy 0.
  - **Action:** `can_play(0)`.
  - **Expected:** true.

- **T-CMB-100 — EG** — Empty hand any idx → false.
  - **Setup:** hand empty.
  - **Action:** `can_play(0)`.
  - **Expected:** false (D1 right operand fires).

### 10.3 `Combat::play_card(int hand_idx, int target_idx)` — CC=3, branches=4

**CFG recap:** D1 `!can_play(hand_idx)` early return; D2 `card.on_play` truthy.

#### Tests

- **T-CMB-105 — BP** — Unplayable returns false, no state change.
  - **Setup:** hand has `Strike`, energy 0.
  - **Action:** `bool ok = c.play_card(0, 0);`.
  - **Expected:** `ok == false`; hand still has Strike; energy still 0; enemy hp unchanged; discard empty.

- **T-CMB-110 — BP, DF** — Playable card without `on_play` (defensive — none in current factory set, but the AST check is real). Construct a manual card with `on_play = {}` and inject via reflection — **but** only public API is allowed. Equivalent: rely on the production factories (all set `on_play`) and verify D2 TRUE via T-CMB-115; document that D2 FALSE is unreachable through public API and is verified by inspection only. ⚠ **Unreachable branch** — see §14.3.

- **T-CMB-115 — BP, DF** — Strike: hand→discard, energy spent, damage dealt, win-check called.
  - **Setup:** `BareCombatFixture` + add enemy hp=40; small deck; hand has just `[Strike]`; energy = 3.
  - **Action:** `play_card(0, 0);`.
  - **Expected:**
    - returns true
    - hand size 0
    - discard size 1, top is Strike
    - energy == 2
    - enemy hp == 34
    - `combat_over() == false`
  - **Covers:** D1 FALSE; D2 TRUE (on_play truthy); discard append; check_win_or_lose call (FALSE outcome).

- **T-CMB-120 — DF** — Survivor: gain block AND discard via callback.
  - **Setup:** as above; hand `[Survivor, Strike, Defend]`; pick-discard callback returns 0 (discards the 0th *remaining* card after Survivor is removed — i.e. Strike).
  - **Action:** `play_card(0, -1);`.
  - **Expected:**
    - hand becomes `[Defend]`
    - discard contains `[Strike, Survivor]` (Strike pushed by `discard_chosen_from_hand` first, then Survivor by play_card itself)
    - block == 8
  - **Covers:** lambda-driven nested mutation order.

- **T-CMB-125 — EG** — Lethal-on-play: dealing damage that kills the last enemy trips `combat_over`.
  - **Setup:** one enemy hp=4; deck of just `[Strike]`; energy=1.
  - **Action:** `play_card(0, 0);`.
  - **Expected:** returns true; enemy hp == 0; `combat_over() == true`.
  - **Covers:** check_win_or_lose D1 TRUE branch via play_card.

### 10.4 `Combat::draw(int n)` — CC=5, branches=8

**CFG recap (line 92–100):**

- D1: `for i = 0; i < n; ++i` — outer loop.
- D2: `if hand.size() >= kMaxHandSize` (10) → return.
- D3: `if draw_pile.empty()` → reshuffle.
- D4: `if draw_pile.empty()` (post-reshuffle still empty) → return.

**Equivalence partitions:**

| Class | n | hand size | draw size | discard size | Expected |
|---|---|---|---|---|---|
| n=0 | 0 | any | any | any | no-op |
| Plenty | 3 | 0 | 10 | 0 | hand=3, draw=7 |
| Hand cap mid-draw | 5 | 8 | 10 | 0 | hand=10 (cap), draw=8 |
| Reshuffle path | 3 | 0 | 0 | 5 | reshuffle, hand=3, draw=2 |
| Empty + empty | 3 | 0 | 0 | 0 | hand=0 (early return on D4) |

#### Tests

- **T-CMB-130 — BP, BV** — `draw(0)` is a no-op.
  - **Expected:** hand and draw unchanged.
  - **Covers:** D1 FALSE on entry.

- **T-CMB-135 — BP, EP** — `draw(3)` from a populated draw pile, empty hand.
  - **Setup:** small deck of 10 cards via `start`. After start, hand is 7 (rng-bonus draw). Discard top 3 to get hand=4 — **public API doesn't expose mutating hand directly**; instead, set up via `start` with a deck that produces hand=7 then call `draw(0)` is uninformative. **Refined:** use a 5-card deck so `start` draws 5, then `draw(3)` — but draw will be empty. **Refined again:** put 12 cards in the deck → start draws 7, leaving 5 in draw; assert pre-draw state; then `draw(3)`.
  - **Action:** `c.draw(3);`.
  - **Expected:** hand size grows by 3; draw decreases by 3; discard unchanged.
  - **Covers:** D1 TRUE→TRUE→TRUE; D2 FALSE three times; D3 FALSE three times.

- **T-CMB-140 — BV** — Hand cap clamp.
  - **Setup:** craft state with hand at 8 by playing then ending turn cycles to bring hand to a known size. (Difficult through public API alone without scripting many turns; alternatively start with a deck where `start` draws exactly 7, then re-draw 3 more via direct call — that brings hand to 10 = kMaxHandSize, then attempting `draw(2)` should be a no-op.)
  - **Action:** `c.draw(2);` from hand-size 10.
  - **Expected:** hand still 10; draw pile unchanged.
  - **Covers:** D2 TRUE.

- **T-CMB-145 — BP, EG** — Reshuffle when draw empties mid-loop.
  - **Setup:** deck of 8 cards; `start` draws 7 leaving 1 in draw; play and discard 1 card to get discard=1 and draw=1 hand=6; then call `draw(2)`.
  - **Action:** `c.draw(2);`.
  - **Expected:** first iteration draws the last card (hand=7, draw=0), second iteration triggers reshuffle (D3 TRUE), then draws (hand=8, draw=0).
  - **Covers:** D3 TRUE; D4 FALSE (after reshuffle, draw is non-empty).

- **T-CMB-150 — EG** — Empty draw + empty discard short-circuits.
  - **Setup:** deck of exactly the starter draw count (7) with no Ring bonus would normally not happen — instead use a 7-card deck so `start` draws all 7, leaving draw=0, discard=0, hand=7; play 0 cards; `c.draw(3)`.
  - **Action:** `c.draw(3);`.
  - **Expected:** hand still 7; no exception.
  - **Covers:** D3 TRUE; D4 TRUE → return on iteration 1.

### 10.5 `Combat::reshuffle()` — CC=2, branches=2

**CFG recap:** `while (!discard_pile.empty())` move-back; then `rng_.shuffle(draw_pile)`.

#### Tests

- **T-CMB-155 — BP, BV** — Empty discard → no-op (D1 FALSE on entry).
  - **Setup:** deck small enough that `start` empties discard.
  - **Action:** `c.reshuffle();`.
  - **Expected:** state unchanged.

- **T-CMB-160 — BP, DF** — Non-empty discard → moved to draw and shuffled.
  - **Setup:** play several cards via `play_card` to populate discard; remember the multiset.
  - **Action:** record draw_pile contents (sorted); record discard contents (sorted); call `reshuffle()`.
  - **Expected:** discard empty; draw_pile multiset equals (pre-discard ∪ pre-draw) sorted; order is the seeded shuffle output.
  - **Covers:** D1 TRUE branch; the loop drains; shuffle invoked.

### 10.6 `Combat::end_player_turn()` — CC=2, branches=2

**CFG recap:** `while (!hand.empty()) discard_pile.push_back(move(hand.back())); hand.pop_back();` then `powers::tick_at_turn_end(player_.vitals.powers)`.

#### Tests

- **T-CMB-165 — BP, BV** — Empty hand — D1 FALSE on entry; only ticks powers.
  - **Setup:** hand empty; player has Weak 1.
  - **Action:** `c.end_player_turn();`.
  - **Expected:** hand still empty; discard unchanged; Weak vanishes (tick erases at 0).

- **T-CMB-170 — BP** — Non-empty hand — discarded LIFO.
  - **Setup:** hand `[Strike, Defend, Neutralize]`.
  - **Action:** `c.end_player_turn();`.
  - **Expected:** hand empty; discard ends as `[Neutralize, Defend, Strike]` (push_back of `back()` then pop_back is reverse order).
  - **Covers:** D1 TRUE; def-use of `hand.back()` move into discard.

### 10.7 `Combat::start_player_turn()` — CC=5, branches=8

**CFG recap (line 29):**

- D1: `for e in enemies` — roll move loop.
- D2: `if e.vitals.hp > 0` inside loop.
- D3: `if round_ > 1` — block reset.
- D4 (ternary): `round_ == 1 ? kRingOfTheSnakeBonus : 0`.

#### Tests

- **T-CMB-175 — BP, BV** — Round 1 — block NOT reset, draws 7.
  - **Setup:** `BareCombatFixture` with 12-card starter deck and 2 cultists; player block manually 4 (set via `gain_player_block(4)` before `start`).
  - **Action:** `c.start(deck);` (which calls `start_player_turn`).
  - **Expected:**
    - `c.round() == 1`
    - `c.player().vitals.block == 4` (D3 FALSE)
    - hand size 7
    - energy 3
    - both enemies have `performed_first_move == true` (D2 TRUE for both)
  - **Covers:** D2 TRUE×2; D3 FALSE; D4 ternary TRUE.

- **T-CMB-180 — BP** — Round 2 — block reset, draws 5.
  - **Setup:** as `StarterCombatFixture` then `c.end_turn();` to advance to round 2.
  - **Pre-action expectation:** `c.round() == 2`.
  - **Expected after the round-2 start:**
    - `c.player().vitals.block == 0`
    - hand size 5 (not 7)
    - both enemies still alive → moves rolled to DarkStrike (since they performed_first_move on R1)
  - **Covers:** D3 TRUE; D4 ternary FALSE.

- **T-CMB-185 — EG** — One enemy already dead — its move is not rolled.
  - **Setup:** `StarterCombatFixture`; deal 99 damage to enemy 0 to kill it; advance to round 2 via `end_turn`.
  - **Expected:** enemy 0's `current_move` and `performed_first_move` are unchanged from when it died (D2 FALSE skips it).
  - **Covers:** D2 FALSE branch.

### 10.8 `Combat::enemy_phase()` — CC=8, branches=14

**CFG recap (line 50, three for-loops):**

- D1: for-loop 1; D2: `if hp > 0` (block reset).
- D3: for-loop 2; D4: `if hp <= 0 continue`; D5: `if combat_over_ return`.
- D6: for-loop 3; D7: `if hp > 0` (tick).

This is the most stateful method; it interacts with `Damage`, `Powers`, and Ritual ordering.

#### Tests

- **T-CMB-190 — BP, BV** — No enemies — three loops fall through, no-op.
  - **Setup:** `BareCombatFixture` (no enemies).
  - **Action:** `c.enemy_phase();`.
  - **Expected:** no state change; no crash.
  - **Covers:** D1, D3, D6 immediate exit; basis path.

- **T-CMB-195 — BP** — Two alive enemies — happy path full sweep.
  - **Setup:** `StarterCombatFixture`; both alive at full hp; both have `performed_first_move=true` (advanced via a previous `start_player_turn`); enemy 0 has `current_move=DarkStrike`, dark_strike_base=9 (calcified); enemy 1 has `current_move=Incantation`.
  - **Action:** give both enemies 4 block first via direct manipulation? **Public API only** — block is reset *before* act, so we need to test that block was reset. Pre-block them via the prior turn cycle. Concretely: `start` → end first round so enemies acted Incantation (gained Ritual just_applied) → second `start_player_turn` rolls them to DarkStrike → block them by having them act this round. Better: just observe that after `enemy_phase`, alive enemies have block 0.
  - **Refined assertion plan:** start → first turn → end_turn (which calls enemy_phase). After that:
    - both enemies `current_move == DarkStrike` (their move rolled at start of player turn)? No — `roll_next_move` is called in `start_player_turn`, **not** in `enemy_phase`. The first roll latches `performed_first_move`; subsequent rolls move to DarkStrike.
    - On round 1 enemy_phase, `current_move == Incantation` — they Buff (apply Ritual to self with just_applied=true).
    - Player hp unchanged.
    - Enemy block reset to 0 (was already 0).
    - Each enemy ticks: Ritual.just_applied → cleared (D5 of `tick_at_turn_end`'s D2 TRUE).
  - **Expected after R1 enemy_phase:**
    - enemy 0 has Ritual {amount=2, just_applied=false}
    - enemy 1 has Ritual {amount=5, just_applied=false}
    - player.hp unchanged
  - **Covers:** D1 enters, D2 TRUE×2; D3 enters, D4 FALSE×2; D5 FALSE; D6 enters, D7 TRUE×2.

- **T-CMB-200 — DF** — Round 2 enemy_phase: both DarkStrike, Strength gained from Ritual.
  - **Setup:** advance through R1 end_turn (R1 enemy_phase Incantation → both have Ritual just_applied=false coming out of end_player_turn? wait — tick_at_turn_end happens in end_player_turn for the player's powers, and within enemy_phase for each alive enemy at the *bottom* of the loop. So enemy ticks happen at the end of enemy_phase, which clears just_applied for each Ritual. Then on R2 start_player_turn: enemies roll Incantation → DarkStrike. R2 enemy_phase:
    - block reset (still 0)
    - enemy 0 acts DarkStrike: dark_strike_base=9 with Strength=0 currently (Ritual gain happens at *tick* end); damage = 9 → player.hp -= 9 (after damage::compute_outgoing with no Strength yet).
    - enemy 1 acts DarkStrike: 1 damage.
    - tick: Ritual.just_applied is FALSE (was cleared in R1 end), so `apply(Strength, ritual.amount)` runs. Enemy 0 gets Strength 2. Enemy 1 gets Strength 5.
  - **Expected post-R2 enemy_phase:**
    - player.hp == 70 - 9 - 1 == 60
    - enemy 0 has [Ritual{2,false}, Strength{2}]
    - enemy 1 has [Ritual{5,false}, Strength{5}]
  - **Covers:** Ritual → Strength gain ordering; the ✦ key invariant of the encounter spec.

- **T-CMB-205 — EG** — One dead enemy in the middle — others still phase correctly.
  - **Setup:** as starter; deal 99 to enemy 0 (hp=0); advance.
  - **Expected:** enemy 0 doesn't reset block, doesn't act, doesn't tick. Enemy 1 does all three.
  - **Covers:** D2 FALSE (skip block reset for dead); D4 TRUE (continue past dead in act loop); D7 FALSE (skip tick for dead).

- **T-CMB-210 — EG** — Player dies mid-phase short-circuits the act loop.
  - **Setup:** `WoundedCombatFixture` with player.hp=1 entering R2 enemy_phase; enemy 0 DarkStrike base 9; enemy 1 DarkStrike base 1.
  - **Action:** `c.end_turn();` to drive into the phase.
  - **Expected:** Enemy 0 acts and kills the player. `combat_over_` becomes TRUE. The for-loop's `if combat_over_ return;` (D5) short-circuits; enemy 1 does not act this turn; the third for-loop (tick) is NOT executed.
  - **Covers:** D5 TRUE; documents that on-death the tick loop is skipped.

### 10.9 `Combat::end_turn()` — CC=3, branches=4

**CFG recap:** D1 `if combat_over_ return`; D2 `if combat_over_ return` (post-enemy_phase).

#### Tests

- **T-CMB-215 — BP** — Combat already over — early return.
  - **Setup:** `WoundedCombatFixture`; deal lethal damage to player → `combat_over_=true`.
  - **Action:** `c.end_turn();`.
  - **Expected:** no state change; `round()` unchanged.
  - **Covers:** D1 TRUE.

- **T-CMB-220 — BP** — Player dies during enemy_phase — early return after enemy_phase.
  - **Setup:** as T-CMB-210.
  - **Expected:** round NOT incremented; `start_player_turn` NOT called (hand not redrawn for next round).
  - **Covers:** D2 TRUE.

- **T-CMB-225 — BP** — Normal round transition.
  - **Setup:** `StarterCombatFixture` post-start.
  - **Action:** `c.end_turn();`.
  - **Expected:** `round() == 2`; hand size 5 (R2 draw); energy 3; player block 0.
  - **Covers:** D1 FALSE; D2 FALSE; full path through `end_player_turn → enemy_phase → round_++ → start_player_turn → check_win_or_lose`.

### 10.10 `Combat::is_player_dead()` and `Combat::all_enemies_dead()` and `Combat::check_win_or_lose()`

#### `is_player_dead` — CC=1

- **T-CMB-230 — BP, BV** — TRUE at hp=0 and hp<0.
- **T-CMB-235 — BP** — FALSE at hp=1, hp=70.

#### `all_enemies_dead` — CC=3, branches=4

**CFG recap:** D1 `for e in enemies if e.vitals.hp > 0 return false`; D2 `return !enemies_.empty()`.

- **T-CMB-240 — BP, BV** — Empty enemies vector → FALSE (`!empty == false`).
  - **Covers:** D1 immediate exit; D2 FALSE.

- **T-CMB-245 — BP** — All dead → TRUE.
  - **Setup:** add 2 enemies, kill both via `deal_damage_to_enemy`.
  - **Covers:** D1 fully traversed without return; D2 TRUE.

- **T-CMB-250 — BP** — One alive in middle → FALSE.
  - **Setup:** 3 enemies, kill 0 and 2.
  - **Action:** `all_enemies_dead()`.
  - **Expected:** false.
  - **Covers:** D1 finds alive at index 1 → returns false.

#### `check_win_or_lose` — CC=3, branches=4

**CFG recap:** `if (is_player_dead() || all_enemies_dead()) combat_over_ = true;`. Short-circuit `||`.

- **T-CMB-255 — BP** — Player dead → combat_over true (left operand TRUE, short-circuits).
- **T-CMB-260 — BP** — Player alive, all enemies dead → combat_over true (left FALSE, right TRUE).
- **T-CMB-265 — BP** — Both alive → combat_over FALSE (no change).
- **T-CMB-270 — EG** — Both sides simultaneously dead → combat_over true.
  - **Setup:** craft simultaneous death by playing a card whose lambda first kills the last enemy and then somehow kills the player. Not reachable in the production card set, but `enemy_phase` could end with player at 0 and an enemy already at 0 from earlier — verify `combat_over` flips on the first true predicate.

### 10.11 `Combat::discard_chosen_from_hand()` — CC=4, branches=6

**CFG recap:**

- D1: `if hand.empty()` early return.
- D2: `if idx < 0 || (size_t)idx >= hand.size()` (short-circuit `||`) early return.

#### Tests

- **T-CMB-275 — BP, BV** — Empty hand — no-op.
  - **Setup:** hand empty; install callback that returns 0.
  - **Action:** `discard_chosen_from_hand();`.
  - **Expected:** state unchanged; callback NOT invoked (D1 TRUE returns before consult).
  - **Covers:** D1 TRUE.

- **T-CMB-280 — BP** — Callback returns -1 → no-op.
  - **Setup:** hand has 1 card; callback returns -1.
  - **Expected:** hand unchanged; discard unchanged.
  - **Covers:** D2 TRUE via left operand.

- **T-CMB-285 — BP** — Callback returns out-of-range index → no-op.
  - **Setup:** hand has 2 cards; callback returns 5.
  - **Expected:** hand unchanged.
  - **Covers:** D2 TRUE via right operand of `||`.

- **T-CMB-290 — BP, DF** — Callback returns valid index → moves to discard.
  - **Setup:** hand has 3 cards; callback returns 1.
  - **Expected:** hand size 2; discard top is the original index-1 card; remaining hand preserves indices 0 and 2.
  - **Covers:** D2 FALSE; mutate path.

### 10.12 Coverage tally — `Combat`

| Method | Branches covered by |
|---|---|
| `Combat`, `start`, `add_enemy`, `set_pick_discard_callback`, `gain_player_block`, `apply_power_to_enemy`, `apply_power_to_enemy_self`, `is_player_dead`, `deal_damage_to_enemy`, `enemy_attack_player`, `player`, `enemies`, `round`, `combat_over` | T-CMB-005 … 070, 230, 235 |
| `can_play` D1 left, D1 right, D2 | 075, 080, 085, 090, 095 |
| `play_card` D1, D2 | 105, 115, 120, 125 (D2 FALSE noted unreachable §14.3) |
| `draw` D1-D4 | 130, 135, 140, 145, 150 |
| `reshuffle` | 155, 160 |
| `end_player_turn` | 165, 170 |
| `start_player_turn` D1-D4 | 175, 180, 185 |
| `enemy_phase` D1-D7 | 190, 195, 200, 205, 210 |
| `end_turn` D1, D2 | 215, 220, 225 |
| `all_enemies_dead` | 240, 245, 250 |
| `check_win_or_lose` | 255, 260, 265, 270 |
| `discard_chosen_from_hand` | 275, 280, 285, 290 |

All `Combat` branches covered (with the noted unreachable in §14.3).

---

## 11. Module: `render` (`src/render/Bar.cpp`, `Render.cpp`, `Render_internal.h`)

### 11.1 `render::hp_bar(int current, int maximum, int width)` — CC=7, branches=12

**CFG recap:**

- D1: `if width <= 0 return {}`.
- D2: `if maximum <= 0 maximum = 1`.
- D3: `clamped = max(0, min(current, maximum))` — calls `std::min` and `std::max` (no decisions modelled here; they're inlined library calls).
- D4: `if clamped > 0 && filled_chars == 0 filled_chars = 1` (short-circuit `&&`).
- D5: `for i = 0; i < filled_chars; ++i` — fill loop.
- D6: `for i = filled_chars; i < width; ++i` — empty loop.

**Equivalence partitions:**

| Class | (current, maximum, width) | Expected |
|---|---|---|
| Width 0 | (5, 10, 0) | `""` |
| Width negative | (5, 10, -3) | `""` |
| Maximum 0 | (5, 0, 4) | maximum coerced to 1 → all filled (since current > 1) → `"████"` |
| Maximum negative | (5, -1, 4) | same as above |
| Current 0 | (0, 10, 4) | all empty `"░░░░"` |
| Current negative | (-3, 10, 4) | clamped to 0 → all empty |
| Current > maximum | (15, 10, 4) | clamped to 10 → all filled |
| Tiny positive (visibility floor) | (1, 100, 4) | filled_chars math = 0 BUT D4 raises to 1 → `"█░░░"` |
| Half | (5, 10, 4) | filled_chars = 2 → `"██░░"` |
| Full | (10, 10, 4) | `"████"` |

#### Tests

- **T-RND-005 — BP, BV** — Width zero — D1 TRUE.
  - **Action:** `auto s = render::hp_bar(5, 10, 0);`.
  - **Expected:** `s.empty()`.

- **T-RND-010 — BV, EG** — Negative width — D1 TRUE.
  - **Action:** `hp_bar(5, 10, -3)`.
  - **Expected:** `""`.

- **T-RND-015 — BP, EG** — Maximum zero coerced — D2 TRUE.
  - **Action:** `hp_bar(5, 0, 4)`.
  - **Expected:** `"████"` (4× kFullBlock UTF-8 sequence).

- **T-RND-020 — EG** — Negative maximum coerced — D2 TRUE.
  - **Action:** `hp_bar(5, -1, 4)`.
  - **Expected:** `"████"`.

- **T-RND-025 — BP, BV** — Current zero — clamp lower.
  - **Action:** `hp_bar(0, 10, 4)`.
  - **Expected:** `"░░░░"` (4× kEmptyBlock).

- **T-RND-030 — BV** — Current negative — clamp lower.
  - **Action:** `hp_bar(-3, 10, 4)`.
  - **Expected:** `"░░░░"`.

- **T-RND-035 — BV** — Current above max — clamp upper.
  - **Action:** `hp_bar(15, 10, 4)`.
  - **Expected:** `"████"`.

- **T-RND-040 — BP, BV** — Visibility floor — D4 TRUE (clamped > 0 AND filled_chars == 0).
  - **Action:** `hp_bar(1, 100, 4)`.
  - **Expected:** `"█░░░"` (`(1*4)/100 == 0`, but raised to 1).
  - **Covers:** D4 `&&` both operands TRUE.

- **T-RND-045 — BP, EP** — Half-fill normal case.
  - **Action:** `hp_bar(5, 10, 4)`.
  - **Expected:** `"██░░"`.

- **T-RND-050 — BV** — Full bar.
  - **Action:** `hp_bar(10, 10, 4)`.
  - **Expected:** `"████"`.

- **T-RND-055 — EG** — Width 1 — degenerate but legal.
  - **Action:** `hp_bar(5, 10, 1)`.
  - **Expected:** `"░"` (`(5*1)/10 == 0`, AND clamped > 0 → raised to 1) → actually `"█"`.
  - **Verify:** which branch wins? D4 is `clamped > 0 && filled_chars == 0`, both true → `filled_chars = 1`; then loop runs once → `"█"`. Confirm before pinning.

- **T-RND-060 — EG, BV** — `clamped == 0` doesn't trigger D4.
  - **Action:** `hp_bar(0, 10, 4)`.
  - **Expected:** `"░░░░"`. (`clamped == 0` makes D4 left operand FALSE, short-circuits.)
  - **Covers:** D4 short-circuit at first operand.

### 11.2 Hoisted anon-namespace helpers (`Render_internal.h`)

#### `repeat_utf8(const char*, int)` — CC=2

- **T-RND-065 — BP, BV** — Count 0 → empty.
- **T-RND-070 — BP** — Count 3 → glyph repeated 3 times.
- **T-RND-075 — EG** — Negative count → empty (loop predicate `i < count` FALSE on entry).

#### `spaces(size_t)` — CC=1

- **T-RND-080 — BP, BV** — `spaces(0)` → `""`; `spaces(5)` → `"     "`.

#### `power_color(PowerKind)` — CC=1

- **T-RND-085 — BP** — Returns `ansi::kReset` for any PowerKind.
  - **Rationale:** lock current (no per-kind colour) behaviour.

#### `power_name(PowerKind)` — CC=4 (switch with 3 cases + fallthrough return)

- **T-RND-090 — BP** — Each enum value maps correctly: Weak→"Weak", Strength→"Str", Ritual→"Ritual".
- **T-RND-095 — EG** — Out-of-enum value (cast `static_cast<PowerKind>(99)`) → `""` (post-switch return).
  - **Covers:** switch fall-through default-equivalent path.

#### `format_powers(const std::vector<Power>&)` — CC=3

**CFG:** D1 `if ps.empty() return {}`; D2 `for p in ps`; D3 `if !first` (separator).

- **T-RND-100 — BP, BV** — Empty vector → empty string.
- **T-RND-105 — BP** — Single power → `"<color>Weak 2<reset>"` (no leading separator).
- **T-RND-110 — BP** — Two powers → `"<color>Weak 2<reset>, <color>Str 3<reset>"` (separator between).
- **T-RND-115 — EG** — Three+ powers — separator count `n-1`.

#### `format_intent(const Enemy&)` — CC=3 (switch 2 cases)

- **T-RND-120 — BP** — Incantation → contains kArrowUp glyph and "Buff".
- **T-RND-125 — BP** — DarkStrike → contains kSwords glyph and the *computed* damage (e.g. base 9 with no powers → "9").
- **T-RND-130 — DF** — DarkStrike with Strength on enemy reflects boosted damage in intent.
  - **Setup:** Enemy with `vitals.powers=[Strength,2]`, base 9 → intent shows "11".

#### `max_enemy_name_len(const std::vector<Enemy>&)` — CC=3

- **T-RND-135 — BP, BV** — Empty → 0.
- **T-RND-140 — BP** — All alive — returns max name length.
- **T-RND-145 — EG** — Dead enemies excluded.
  - **Setup:** Enemy A name "longer" hp=0; Enemy B name "x" hp=10.
  - **Expected:** returns 1 (length of "x"), not 6.

#### `total_deck_size(const Player&)` — CC=1

- **T-RND-150 — BP** — Sum of all four piles.

### 11.3 `render::render_combat(const Combat&, std::ostream&)` — CC=13, branches=24

The integration target. Tested via `CapturedSink` (`std::ostringstream`).

**Decision points (line numbers from Render.cpp):** 99, 103, 114, 116, 121, 125, 132, 135, 136, 137, 143, 147 (per Part 1).

#### Tests

- **T-RND-155 — BP, EP** — Initial state — round 1, full HP, no block, no powers, both enemies alive, hand of 7.
  - **Setup:** `StarterCombatFixture`.
  - **Action:** `std::ostringstream os; render::render_combat(c, os); auto out = os.str();`.
  - **Expected (substring assertions):**
    - contains `"Round 1"`
    - contains `"Energy 3/3"`
    - contains `"70/70"` (player HP)
    - contains `"Calcified Cultist"` and `"Damp Cultist"`
    - contains `"[0]"` ... `"[6]"` (one for each card in hand)
    - does NOT contain `"blk"` (no block to display)
    - does NOT contain `"Weak"`/`"Str"`/`"Ritual"` token (no powers visible)
    - contains `kRelicDiamond` glyph ("Ring of the Snake" line)
  - **Covers:** D99 FALSE; D103 FALSE; D114 enters; D116 FALSE×2; D121 FALSE×2; D125 FALSE×2; D132 enters; D135-137 ternaries (cards playable initially with energy 3 and most cost 1).

- **T-RND-160 — BP** — Block visible on player.
  - **Setup:** as above; `c.gain_player_block(5);`.
  - **Expected:** output contains `"5"` followed by `"blk"`.
  - **Covers:** D99 TRUE.

- **T-RND-165 — BP** — Powers visible on player.
  - **Setup:** `c.apply_power_to_enemy(0, ...)` is enemy-side; for player, drive Weak via enemy DarkStrike + Neutralize? But powers on player are typically applied via enemy's Weak attack — currently no enemy applies player Weak. Instead, use a controlled construction: build a `Combat` where player's Vitals.powers are populated via the `tick`/`apply` indirection. **Alternative:** the only public surface that adds powers to the player is `apply_power_to_enemy_self`-like — but that targets enemy. There's no public Combat method to apply a power to the player.
  - **Resolution:** to test player-side power rendering, we need to invoke `powers::apply` directly on a vector we own and then construct a `Combat` whose state includes that. Since `Combat`'s player is private, this test can't be set up via public API.
  - **Plan:** mark this as **not directly reachable via public API** in §14.3. Indirect coverage comes from enemy-side power rendering (T-RND-175) which exercises the *same* `format_powers` formatter and the enemy variant of D125.

- **T-RND-170 — BP** — Block visible on enemy.
  - **Setup:** test fixture builds an enemy via `add_enemy` whose `vitals.block` = 3 (set on the prebuilt struct), then renders.
  - **Expected:** output contains the enemy block in blue ANSI.
  - **Covers:** D121 TRUE.

- **T-RND-175 — BP, DF** — Enemy with Ritual visible.
  - **Setup:** `StarterCombatFixture`; `c.end_turn();` (R1 enemy_phase applies Ritual).
  - **Expected:** output contains `"Ritual 2"` (calcified) and `"Ritual 5"` (damp).
  - **Covers:** D125 TRUE; integration with `format_powers`.

- **T-RND-180 — BP** — Dead enemy hidden.
  - **Setup:** kill enemy 0 via `deal_damage_to_enemy(0, 99)`.
  - **Action:** render.
  - **Expected:** `"Calcified Cultist"` NOT present; `"Damp Cultist"` IS present.
  - **Covers:** D116 TRUE → `continue`.

- **T-RND-185 — BP** — Unplayable card displayed in dim.
  - **Setup:** energy 0; hand has Strike (cost 1).
  - **Action:** to set energy=0 via public API — play cards until 0, OR use a deck/scenario that ends with energy=0. Concretely: `start` gives energy=3; play 3 Strikes → energy=0; render.
  - **Expected:** the remaining cards (cost 1) render with kBulletHollow and kDim ANSI, not kBulletFilled and kGreen.
  - **Covers:** D135, D136 ternary FALSE branch.

- **T-RND-190 — BP** — Attack vs Skill colour.
  - **Setup:** as above; hand contains both an Attack (Strike) and a Skill (Defend).
  - **Expected:** Strike line contains kRed; Defend line contains kBlue.
  - **Covers:** D137 ternary TRUE/FALSE.

- **T-RND-195 — BP** — Target arrow on AnyEnemy cards.
  - **Setup:** hand contains Strike (AnyEnemy) and Defend (Self).
  - **Expected:** Strike line contains kArrowRight; Defend line does NOT.
  - **Covers:** D143 TRUE/FALSE.

- **T-RND-200 — BP** — Description rendering.
  - **Setup:** hand contains Neutralize (description has 2 lines).
  - **Expected:** output contains both `"Deal 3 damage."` and `"Apply 1 Weak."`.
  - **Covers:** D147 (range-for over description) — multiple iterations.

- **T-RND-205 — DF** — Intent rendering toggles per move.
  - **Setup A:** R1 just-started → enemies have `current_move == Incantation`; render.
  - **Expected A:** intent line shows "Buff".
  - **Setup B:** R2 just-started → enemies have `current_move == DarkStrike`; render.
  - **Expected B:** intent line shows the boosted damage value (computed via `damage::compute_outgoing`).

- **T-RND-210 — EG** — All enemies dead — no enemy lines.
  - **Setup:** kill both enemies.
  - **Expected:** no `"["` lines for enemies.
  - **Covers:** D116 TRUE for all entries; `display_idx` stays at 0.

### 11.4 Coverage tally — `render`

| Function | Branches covered |
|---|---|
| `hp_bar` D1-D6 + D4 short-circuit | T-RND-005 … 060 |
| Hoisted helpers | T-RND-065 … 150 |
| `render_combat` D99-D147 | T-RND-155 … 210 (player-power D103 noted unreachable §14.3) |

---

## 12. Module: `input` and `console`

### 12.1 `input::read_action(std::istream&)` — CC=8, branches=14

**CFG recap (line 33):**

- D1: `if !std::getline(in, line)` — EOF → Quit.
- D2: `if line.empty()` — Invalid.
- D3: `if line == "e" || line == "E"` (short-circuit `||`) — EndTurn.
- D4: `if line == "q" || line == "Q"` (short-circuit `||`) — Quit.
- D5: `if parse_nonneg_int(line, idx)` — PlayCard.

**Equivalence partitions:**

| Input line | Expected kind | Decision path |
|---|---|---|
| EOF (closed stream) | Quit | D1 TRUE |
| `""` | Invalid | D2 TRUE |
| `"e"` | EndTurn | D3 TRUE (left) |
| `"E"` | EndTurn | D3 TRUE (right of `||`) |
| `"q"` | Quit | D4 TRUE (left) |
| `"Q"` | Quit | D4 TRUE (right) |
| `"3"` | PlayCard, idx=3 | D5 TRUE |
| `"3a"` | Invalid | D5 FALSE |
| `"  e  "` | EndTurn | trim → "e", D3 TRUE |

#### Tests

- **T-INP-005 — BP** — EOF → Quit.
  - **Setup:** `std::istringstream in("");` (empty stream — getline returns false).
  - **Expected:** `Action.kind == Quit`.
  - **Covers:** D1 TRUE.

- **T-INP-010 — BP, BV** — Empty line → Invalid.
  - **Setup:** `std::istringstream in("\n");`.
  - **Expected:** kind=Invalid.
  - **Covers:** D2 TRUE (post-trim is empty).

- **T-INP-015 — BP, EP** — `"e"` → EndTurn.
  - **Setup:** `"e\n"`.
  - **Covers:** D3 left-op TRUE.

- **T-INP-020 — BP, EP** — `"E"` (uppercase) → EndTurn.
  - **Covers:** D3 right-op TRUE.

- **T-INP-025 — BP, EP** — `"q"` → Quit.
  - **Covers:** D4 left-op TRUE.

- **T-INP-030 — BP, EP** — `"Q"` → Quit.
  - **Covers:** D4 right-op TRUE.

- **T-INP-035 — BP, EP** — `"3"` → PlayCard with idx 3.
  - **Covers:** D5 TRUE.

- **T-INP-040 — BP, EG** — `"3a"` → Invalid (parse fails on letter).
  - **Covers:** D5 FALSE.

- **T-INP-045 — EP** — Whitespace tolerant.
  - **Setup:** `"  e  \n"`.
  - **Expected:** EndTurn.
  - **Covers:** trim path → D3 TRUE.

- **T-INP-050 — EG** — Trailing CR/LF mix (Windows line endings) trimmed.
  - **Setup:** `"q\r\n"`.
  - **Expected:** Quit (the `\r` is whitespace per `std::isspace`).

- **T-INP-055 — EG** — Multi-line input — `read_action` consumes only first line.
  - **Setup:** `"q\ne\n"`.
  - **Action:** call `read_action(in)` once.
  - **Expected:** Quit; `in.peek() == 'e'` (next call would read EndTurn).

- **T-INP-060 — EG** — Long numeric string → PlayCard with parsed value.
  - **Setup:** `"42\n"`.
  - **Expected:** PlayCard, idx=42 (no upper-bound check at this layer).

- **T-INP-065 — EG** — Overflow guard via `parse_nonneg_int`.
  - **Setup:** `"9999999\n"` (above 1_000_000 cap).
  - **Expected:** Invalid (parse_nonneg_int returns false).
  - **Covers:** D5 FALSE via overflow.

### 12.2 `input::read_index(std::istream&, int max_inclusive)` — CC=4, branches=6

**CFG recap:**

- D1: `if !std::getline(in, line)` → -1.
- D2: `if !parse_nonneg_int(line, v)` → -1.
- D3: `if v > max_inclusive` → -1.

#### Tests

- **T-INP-070 — BP** — EOF → -1.
- **T-INP-075 — BP** — Non-digit → -1.
- **T-INP-080 — BP** — In-range value → that value.
- **T-INP-085 — BV** — `v == max_inclusive` → returns `max_inclusive`.
- **T-INP-090 — BV** — `v == max_inclusive + 1` → -1.
- **T-INP-095 — EG** — `max_inclusive == 0`, `v = 0` → returns 0; `v = 1` → -1.
- **T-INP-100 — EG** — Negative `max_inclusive` (e.g. -1) — `parse_nonneg_int` only produces ≥ 0; v=0 leads to D3 TRUE → -1. Documents: when there are zero valid options, every input rejects.

### 12.3 Hoisted helpers (`Input_internal.h`)

#### `trim(std::string)` — CC=5

- **T-INP-105 — BP, BV** — Empty.
- **T-INP-110 — BP, BV** — All whitespace.
- **T-INP-115 — BP** — Leading whitespace only.
- **T-INP-120 — BP** — Trailing whitespace only.
- **T-INP-125 — BP** — Both ends.
- **T-INP-130 — BP** — No whitespace.
- **T-INP-135 — EG** — Embedded whitespace preserved (`"  a b  "` → `"a b"`).
- **T-INP-140 — EG** — Tab and CR also stripped (`std::isspace`-true characters).

#### `parse_nonneg_int(const std::string&, int&)` — CC=5

- **T-INP-145 — BP, BV** — Empty → false; out unchanged.
- **T-INP-150 — BP** — `"0"` → true, out=0.
- **T-INP-155 — BP** — `"42"` → true, out=42.
- **T-INP-160 — BV** — `"1000000"` → true, out=1_000_000 (boundary, **not** > 1_000_000).
- **T-INP-165 — BV** — `"1000001"` → false (> cap).
- **T-INP-170 — BP** — `"1a"` → false.
- **T-INP-175 — EG** — `"+5"` → false (sign char fails `isdigit`).
- **T-INP-180 — EG** — `"-1"` → false.
- **T-INP-185 — EG** — Leading zeros `"007"` → true, out=7.

### 12.4 `console::enable_ansi_and_utf8()` — CC=4 (Windows-only branches)

This calls Win32 APIs and has no return value. Direct test coverage of `if (h && h != INVALID_HANDLE_VALUE)` and `if (GetConsoleMode(...))` requires either mocking the Win32 surface (heavyweight) or accepting smoke coverage.

**Plan:**

- **T-CON-005 — Smoke** — Calling `console::enable_ansi_and_utf8()` does not throw or crash.
  - **Action:** call once.
  - **Expected:** no exception; subsequent `std::cout << "x"` succeeds.
  - **Coverage note:** because this code is `#ifdef _WIN32` gated and the test runs on Windows (per project README), the body executes; llvm-cov will record at least the entry path. The `else` (non-Windows empty body) is covered by build configuration, not by tests.
  - **Marked partial — see §14.3.**

### 12.5 Coverage tally — `input`/`console`

| Function | Branches covered by |
|---|---|
| `read_action` D1-D5, `||` short-circuits | T-INP-005 … 065 |
| `read_index` D1-D3 | T-INP-070 … 100 |
| `trim` (CFG) | T-INP-105 … 140 |
| `parse_nonneg_int` | T-INP-145 … 185 |
| `enable_ansi_and_utf8` | T-CON-005 (smoke; partial) |

---

## 13. Module: `main.cpp` helpers (hoisted)

After hoisting per §2.1, the following are unit-testable:

### 13.1 `parse_uint64(const std::string&, uint64_t&)` — CC=6

**CFG recap:**

- D1: `if s.empty()` → false.
- D2: `for ch in s` — character loop.
- D3: `if ch < '0' || ch > '9'` (short-circuit `||`) → false.
- D4: `if next < v` (overflow check, `next = v*10 + ch-'0'`) → false.

#### Tests

- **T-MAIN-005 — BP, BV** — Empty string → false.
- **T-MAIN-010 — BP** — `"0"` → true, out=0.
- **T-MAIN-015 — BP, EP** — `"42"` → true, 42.
- **T-MAIN-020 — BV** — `"18446744073709551615"` (UINT64_MAX) → true, out=UINT64_MAX.
- **T-MAIN-025 — BV** — `"18446744073709551616"` (UINT64_MAX + 1) → false (overflow detected via D4).
- **T-MAIN-030 — BP** — `"12a"` → false (D3 right operand TRUE).
- **T-MAIN-035 — EG** — `"a12"` → false (D3 left operand TRUE on first char).
- **T-MAIN-040 — EG** — `"-1"` → false (`-` not in '0'-'9').
- **T-MAIN-045 — EG** — Leading whitespace `" 5"` → false (space not digit). Documents: caller is expected to trim.

### 13.2 `parse_args(int, char**, uint64_t&, bool&)` — CC=5

**CFG recap:**

- D1: outer `for i = 1; i < argc; ++i`.
- D2: `if arg == "--seed"`.
- D3: `if i + 1 >= argc` (after `--seed`).
- D4: `if !parse_uint64(argv[i+1], seed_out)`.
- (else branch on D2: unknown arg → error)

#### Tests

Tests construct `argv` arrays. Helper `make_argv(...)` builds a vector of `char*` from string literals.

- **T-MAIN-050 — BP** — `[prog]` (no args) → returns true; `seed_provided==false`.
- **T-MAIN-055 — BP** — `[prog, --seed, 42]` → returns true; `seed_provided==true`; `seed==42`.
- **T-MAIN-060 — BP, EG** — `[prog, --seed]` (missing value) → returns false; stderr contains `"--seed requires a value"`.
- **T-MAIN-065 — BP, EG** — `[prog, --seed, abc]` (bad value) → returns false; stderr contains `"is not a valid uint64"`.
- **T-MAIN-070 — BP** — `[prog, --foo]` (unknown) → returns false; stderr contains `"unknown argument"`.
- **T-MAIN-075 — BV** — Multiple args, last is `--seed`: `[prog, --foo, --seed, 1]` would be invalid (foo first → returns false at index 1 before seeing --seed). Documents short-circuit on first error.
- **T-MAIN-080 — BV** — `[prog, --seed, 0]` → seed=0, ok.
- **T-MAIN-085 — BV** — `[prog, --seed, 18446744073709551615]` → seed=UINT64_MAX.

To capture stderr for assertions: temporarily redirect `stderr` via `freopen` or use an in-test process-internal redirect. Alternative: refactor `parse_args` to take an `std::ostream& err` parameter — recommend this small extension to avoid `freopen` brittleness in `gtest`.

> **Recommended addendum to §2.1:** also refactor `parse_args` to accept `std::ostream& err = std::cerr` so error-path tests can assert on captured strings without `freopen`.

### 13.3 `random_seed()` — CC=1

- **T-MAIN-090 — BP** — Returns a 64-bit value; two consecutive calls usually differ (non-deterministic seed source). Assert returns of two calls differ at least 50 % of the time across 10 invocations to avoid flakiness — better, just assert it doesn't always return 0 across N=10 calls (loose lock for non-determinism).

### 13.4 `prompt_index(std::ostream&, std::istream&, const char* label, int max_inclusive)` — CC=3

**CFG recap:**

- D1: `while(true)` — outer loop (no decision; it's an unconditional loop).
- D2: `if idx >= 0 return idx`.

(The "loop forever until valid" semantics depend on the stream — when the stream EOFs, `read_index` returns -1 and we re-prompt. Without a terminating valid input, this blocks. Tests must terminate.)

#### Tests

- **T-MAIN-095 — BP, EP** — Valid first input.
  - **Setup:** `std::istringstream in("3\n");`, `std::ostringstream out;`, `max_inclusive=5`.
  - **Expected:** returns 3; `out.str()` contains label.
  - **Covers:** D2 TRUE on first iter.

- **T-MAIN-100 — BP, EG** — Invalid then valid.
  - **Setup:** `in("abc\n2\n")`.
  - **Expected:** returns 2; `out.str()` contains label twice and the invalid-index notice once.
  - **Covers:** D2 FALSE → loop again.

- **T-MAIN-105 — EG** — All-invalid stream eventually EOFs.
  - **Setup:** `in("a\nb\n");` (both invalid).
  - **Action:** call `prompt_index`.
  - **Expected:** when `in` reaches EOF, `read_index` returns -1, retries forever — to avoid infinite loop in tests, **document this as a refactoring item: `prompt_index` should bail on EOF**. For now, mark as **out-of-scope-for-test coverage; documented hazard**.

### 13.5 `prompt_target(const Combat&, std::istream&, std::ostream&)` (after stream-injection refactor)

**CFG recap:**

- D1: collect alive indices via for-loop.
- D2: `if alive_indices.empty() return -1`.
- D3: `if alive_indices.size() == 1 return alive_indices[0]`.
- (else: prompt user; return mapped index)

#### Tests

- **T-MAIN-110 — BP** — No alive enemies → -1.
- **T-MAIN-115 — BP** — Exactly one alive → returns its index without consulting stream.
- **T-MAIN-120 — BP** — Two alive, user picks 1 → returns the second alive index.
  - **Setup:** kill enemy 1 (so alive=[0,2]); user input "1\n".
  - **Expected:** returns 2.
- **T-MAIN-125 — EG** — User invalid then valid input — uses `prompt_index` retry.

### 13.6 `prompt_discard(const Combat&, std::istream&, std::ostream&)` — CC=2

**CFG recap:** D1 `if hand.size() == 1 return 0`.

#### Tests

- **T-MAIN-130 — BP** — Single-card hand → returns 0 without consulting stream.
- **T-MAIN-135 — BP** — Multi-card hand — calls `render_combat` then `prompt_index` with proper `max=size-1`.
  - **Expected:** `out.str()` contains the rendered combat AND the discard prompt; returns the user's pick.

### 13.7 Coverage tally — main.cpp helpers

| Function | Branches covered |
|---|---|
| `parse_uint64` D1-D4 | T-MAIN-005 … 045 |
| `parse_args` D1-D4 | T-MAIN-050 … 085 |
| `random_seed` | T-MAIN-090 |
| `prompt_index` D2 | T-MAIN-095, 100 (D1 infinite-loop hazard noted) |
| `prompt_target` D2, D3 | T-MAIN-110-125 |
| `prompt_discard` D1 | T-MAIN-130, 135 |

---

## 14. Coverage projection and roll-up

### 14.1 Test count by module

| Module | Tests planned | Range |
|---|---:|---|
| `Rng` | 14 | T-RNG-005 … 070 |
| `powers` | 29 | T-PWR-005 … 150 |
| `damage` | 18 | T-DMG-005 … 090 |
| `cards` | 13 | T-CRD-005 … 065 |
| `enemies` | 12 | T-ENM-005 … 060 |
| `Combat` | 58 | T-CMB-005 … 290 |
| `render` | 42 | T-RND-005 … 210 |
| `input` | 37 | T-INP-005 … 185 |
| `console` | 1 | T-CON-005 |
| `main.cpp` helpers | 27 | T-MAIN-005 … 135 |
| **Total** | **251** | — |

### 14.2 Branch-coverage projection

From Part 1: **253 branches** across the project. The plan covers:

| Module | Branches | Covered | Excluded¹ | Net coverage |
|---|---:|---:|---:|---:|
| `Rng` | 4 | 4 | 0 | 100 % |
| `powers` | 24 | 24 | 0 | 100 % |
| `damage` | 8 | 8 | 0 | 100 % |
| `cards` | 4 | 4 | 0 | 100 % |
| `enemies` | 6 | 6 | 0 | 100 % |
| `Combat` | 60 | 59 | 1 (U-1) | 100 % of reachable |
| `render` (Bar + Render + hoisted helpers) | 55 | 53 | 2 (U-2) | 100 % of reachable |
| `input` (incl. hoisted) | 36 | 36 | 0 | 100 % |
| `console` | 6 | 2 (smoke) | 4 (U-3) | partial |
| `main.cpp` helpers (excl. `main()` body) | 32 | 32 | 0 | 100 % |
| `main()` body itself | 18 | 0 | 18² | 0 % |
| **Total** | **253** | **228** | **25** | **90.1 %** raw, **100 % of plan-reachable** (203 / 203) |

¹ "Excluded" means branches the plan does not target — split into:
- **U-1 / U-2 / U-3** unreachable under public-API-only / no-Win32-mock constraints (see §14.3).
- ² `main()` is excluded because the user disallowed process invocations and the body has no testable seam other than the helpers it calls (each of which is covered individually). The 18 branches inside `main()` are the orchestration glue: arg dispatch, prompt loop, switch on `Action::Kind`, target prompting. None contains logic absent from a tested helper.

> The plan-reachable branch count is **228** (= 253 − 18 main-body − 1 U-1 − 2 U-2 − 4 U-3). The plan exercises **228 / 228 = 100 %** of those. The raw 90.1 % is what a coverage report would print without the exclusion annotations; with `llvm-cov`'s `lcov` exclusion comments, the report cleans up to 100 % over the targeted region.

### 14.3 Unreachable branches — justified exclusions

These branches cannot be exercised under the user-imposed constraints (public API only, no process invocation, no Win32 mocking). The plan documents each so a future reviewer can verify the exclusion is justified rather than missed.

| ID | Location | Branch | Why unreachable | Mitigation |
|---|---|---|---|---|
| **U-1** | `Combat::play_card` line 85 | `if (card.on_play)` FALSE | All `cards::make_*` factories set `on_play`. Constructing a `Card` with `on_play = {}` and inserting it into the deck requires bypassing the factory, which means private-state access. Public API can't construct such a card. | Static-analysis check: assert in CI that every `cards::make_*` returns a Card with `on_play` set. Treat the FALSE branch as defensive-only; comment in code. |
| **U-2** | `render::render_combat` line 103 | `if (!c.player().vitals.powers.empty())` TRUE | No public `Combat` method applies a power to the player. Only enemy DarkStrike acts on the player (and currently doesn't apply Weak). | Add a `Combat::apply_power_to_player(PowerKind, int)` for parity with the enemy variant — documented as a small API extension; would also enable testing player-side `format_powers` rendering directly. |
| **U-3a-d** | `console::enable_ansi_and_utf8` D1 (`h && h != INVALID_HANDLE_VALUE`) and D2 (`GetConsoleMode succeeds`) | Both operands of each — 4 branches | Real `GetStdHandle` returns a valid handle on a normal CI runner, so the FALSE branches require Win32 API mocking (e.g. via Detours or function-pointer swap). | Smoke-test only (T-CON-005). If we add a Win32 abstraction layer (`win32::IConsoleApi` interface), unit tests can mock the failing paths. Out of scope for this plan; keep the smoke. |
| **U-4** | `prompt_index` while-loop never-terminating EOF condition | Doesn't map to a branch in McCabe's strict sense — it's a livelock under EOF. | Refactor `prompt_index` to break on stream EOF (`if (!in.good()) return -1;`). Recommended addendum to §2.2; until then, tests must always supply a valid input eventually. | Document the hazard; add the EOF-break in a follow-up. |

### 14.4 Statement-coverage projection

llvm-cov measures statement coverage line-by-line. Statement counts (proxy from Part 1's `statements` field) by file:

| File | ~Statements | Plan covers | Projected |
|---|---:|---:|---:|
| `Rng.cpp` (+ template body in Rng.h) | 6 | 6 | 100 % |
| `Powers.cpp` | 28 | 28 | 100 % |
| `Damage.cpp` | 13 | 13 | 100 % |
| `Cards.cpp` | 70 (incl. lambda init lists) | 70 | 100 % |
| `Enemies.cpp` | 24 | 24 | 100 % |
| `Combat.cpp` | 96 | ≥ 92 (assert lines × 2 not exercised under Debug; deliberate paths only) | ~96 % |
| `Bar.cpp` | 12 | 12 | 100 % |
| `Console.cpp` | 6 | ~2 (smoke) | ~33 % — bounded by U-3 |
| `Render.cpp` (+ hoisted) | 79 | ≥ 77 | ~98 % (D103 player-power gating skips 2 stmts; see U-2) |
| `Input.cpp` (+ hoisted) | 32 | 32 | 100 % |
| `main.cpp` helpers | 50 | ~46 | ~92 % (prompt_index EOF-break path skipped pending refactor) |
| **Whole `src/`** | **~416** | **~398** | **~96 %** |

> The 70 % statement-coverage minimum is comfortably exceeded. Console.cpp's 33 % drags the average least because it's only 6 statements (≤ 1.5 % of the codebase by statement count).

### 14.5 Strategy traceability matrix

For audit, every test is tagged with one or more strategies. Aggregate counts:

| Strategy | Test count (a test may be tagged with multiple) |
|---:|---:|
| Structured Basis Path (BP) | 150 |
| Branch Coverage (BR) — implicit in BP tags | — |
| Data-Flow (DF) | 26 |
| Equivalence Partition (EP) | 33 |
| Boundary Value (BV) | 59 |
| Error Guessing (EG) | 62 |

(BR is folded into BP because every basis path test exercises both decision outcomes by construction across the test suite. The branch-projection table in §14.2 verifies this concretely.)

### 14.6 Def-use coverage targeting

Per Part 1 the project has ~210 (var, def) pairs and ~640 (var, use) reads. The plan targets:

- **All-defs** (every definition reached by at least one use in some test): **100 %** — every locally-defined variable in every non-trivial function appears in at least one test action that subsequently observes a downstream effect through that variable.
- **All-uses** (every (def, use) pair exercised by at least one test): **≥ 90 %** — the residual ~10 % is reads that occur on impossible paths under the public API (e.g. `Combat::all_enemies_dead` reading `e.vitals.hp` on the second alive enemy when the first short-circuits at index 0; that read can be reached by other test setups but doesn't add new coverage).

Critical du-pairs explicitly tested:

- `Combat::start_player_turn`: `round_ → draw_count` (T-CMB-175 round 1, T-CMB-180 round 2).
- `damage::compute_outgoing`: `d → static_cast<int>(d * 0.75)` (T-DMG-020/025/045 — the truncation use).
- `powers::tick_at_turn_end`: `ritual->amount → gain → apply(Strength, gain)` (T-PWR-110/115).
- `Combat::play_card`: `card → on_play(combat, target_idx) → discard_pile.push_back(move(card))` (T-CMB-115/120 — the move-after-call sequence).
- `enemies::act`: `e.dark_strike_base → enemy_attack_player(e, base)` (T-ENM-050).

### 14.7 Recommended extensions (post-plan)

These are out-of-scope for the current plan as user-restricted, but would close the small remaining unreachable set:

1. Add `Combat::apply_power_to_player(PowerKind, int)` — closes U-2; one new test in §11.3.
2. Refactor `prompt_index` to bail on EOF — closes U-4; modify T-MAIN-105 from "out of scope" to a normal test.
3. Refactor `parse_args` to take `std::ostream& err` — already in §13.2's recommended addendum; required for clean stderr-capture in T-MAIN-060/065/070.
4. Win32 abstraction layer for `console::enable_ansi_and_utf8` — closes U-3; deferrable.

### 14.8 Test execution profile

- **GoogleTest sharding:** the suite is small (~250 tests, all CPU-bound, deterministic). No need to shard.
- **Determinism:** every Rng-seeded test pins seed `0xC0FFEE`, `0xDEADBEEFCAFE`, or `0x42`. Re-running on any platform produces identical results.
- **Expected runtime:** under 2 s wall-clock for the full suite (no I/O beyond `std::stringstream`).
- **Coverage build:** Clang with `-fprofile-instr-generate -fcoverage-mapping`, run tests, then `llvm-profdata merge -sparse default.profraw -o merged.profdata && llvm-cov show ./tests --instr-profile=merged.profdata --format=html --output-dir=coverage_html --show-branches=count`. The `--show-branches=count` flag exposes the branch-coverage roll-up that this plan targets at 100 % (modulo §14.3).

---

## 15. Open items / follow-ups (non-blocking)

1. Pin the concrete shuffled-deck order for seed `0xC0FFEE` once the suite is stood up — current expectations reference "the seeded order" without naming it.
2. Decide whether `parse_args` getting an `err` stream (recommended in §13.2) is in-scope for the same PR as the test rollout or a precursor PR.
3. Decide whether U-2 (`apply_power_to_player`) is added to round out symmetry; if not, the corresponding render branch stays as documented unreachable.
4. Confirm GoogleTest version target — recommend 1.14+ for `gmock` matchers (`HasSubstr`, `MatchesRegex`) used in the renderer assertions.
5. Confirm test directory layout (`tests/game/test_*.cpp`, `tests/render/test_*.cpp`, etc.) before generation so file names line up with module sections.

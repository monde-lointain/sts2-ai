# CULTISTS_NORMAL — Overview

A 2-enemy normal Monster room in **Underdocks** (Act 1). Two cultists with identical move structure but very different stats — the contrast is the whole design.

Both share the same state machine (`CalcifiedCultist.cs:65`, `DampCultist.cs:65`):

```
INCANTATION (turn 1, buff intent)  →  DARK_STRIKE  →  DARK_STRIKE  →  ...
```

`DarkStrike.FollowUpState = self` ⇒ once they start hitting, they never stop. `_performedFirstMove` in `MonsterMoveStateMachine.cs:60` guarantees Incantation runs once before transitioning, so the opener is deterministic.

---

## Calcified Cultist (front, "coral" skin) — `CalcifiedCultist.cs`

| Stat | A0 | Ascension |
|---|---|---|
| HP | 38–41 | 39–42 (ToughEnemies) |
| Incantation → Ritual | **2** | 2 (no scaling) |
| Dark Strike | **9** | 11 (DeadlyEnemies) |

- Banter: `OUR POWER IS UNMATCHED!` (purple VFX).
- Buff SFX: `cultists_buff_calcified`. Shared attack SFX with rising `enemy_strength` param (+0.2/hit).
- Role: high base damage, slow ramp.

## Damp Cultist (back, "slug" skin) — `DampCultist.cs`

| Stat | A0 | Ascension |
|---|---|---|
| HP | 51–53 | 52–54 (ToughEnemies) |
| Incantation → Ritual | **5** | 6 (DeadlyEnemies) |
| Dark Strike | **1** | 3 (DeadlyEnemies) |

- Banter: `CAW! / CAAAW` (swamp/green VFX).
- Buff SFX: `cultists_buff_damp`.
- Role: trivial early damage, runaway scaling.

---

## Ritual mechanics — `RitualPower.cs`

- Counter stack, Buff type, hover-tips Strength.
- `AfterApplied`: if owner is enemy, set `WasJustAppliedByEnemy = true`.
- `AfterTurnEnd` (owner's side):
  - flag true → clear it, **no Strength this turn**;
  - flag false → flash, `PowerCmd.Apply<StrengthPower>(owner, Amount)`.
- ⇒ Cultists do **not** gain Strength on the turn they cast Incantation. From the *following* turn-end onward, +Amount Strength every turn.

## Damage curve (A0, both alive, no debuffs)

| Turn | Calcified hit | Damp hit | Combined |
|---|---|---|---|
| 1 | — (Incantation) | — (Incantation) | 0 |
| 2 | 9  (Str 0)  → +2 Str at end | 1  (Str 0)  → +5 Str at end | 10 |
| 3 | 11 (Str 2)  → +2 | 6  (Str 5)  → +5 | 17 |
| 4 | 13 (Str 4)  → +2 | 11 (Str 10) → +5 | 24 |
| 5 | 15 (Str 6) | 16 (Str 15) | 31 |
| 6 | 17 | 21 | 38 |

Formula from turn 2 onward: `base + Ritual·(turn-2)`. Calcified out-damages Damp turns 2–4; Damp overtakes turn 5.

Strategic implication: kill Damp first if you can survive a couple Calcified hits — Damp's ramp is the runaway threat. Otherwise tempo-kill Calcified to remove the early-pressure damage. AoE that kills both ~turn 3 trivializes the fight; stalling past turn 5 is lethal.

---

## Cosmetic / engine details

- Both share spine rig, differentiated by skin overlay (`coral` vs `slug` — `SetupSkins`).
- Damage SFX type: `Fur`. Death VFX padding `(1.5, 1.2)`. Death SFX differs per cultist.
- Animator states: `idle_loop` (default loop) + any-state triggers `Cast` / `Attack` / `Hit` / `Dead`.
- Loss flavor (`encounters.json:26`): *"{character} was slain by some Cultists. Caaaaw…"*.

## Unresolved questions

- Does `Cmd.CustomScaledWait(0.25, 0.5)` between cast and Ritual apply matter for any interrupt window? (Probably purely cosmetic, but not verified.)
- `IncantationAmount` on Calcified is hardcoded `2` with no ascension scaling — intentional, or was `DeadlyEnemies` scaling overlooked vs. Damp's `5→6`?
- `_buffSfx` constant is declared on Calcified but the Incantation method uses the literal string instead — dead field, or planned refactor?

# Fixture 08 — NibbitsNormal, seed 42

Wave-24/K.q1 Nibbit port. Q2 K.γ_setup prerequisite.

## Summary

- Encounter: `NibbitsNormal`
- Seed: 42
- Monsters: 2 Nibbits (per-slot move overrides)

## Initial moves per slot

| Slot | Monster | Initial Move | Notes        |
|------|---------|-------------|--------------|
| 0    | Nibbit  | SLICE_MOVE  | Front Nibbit |
| 1    | Nibbit  | HISS_MOVE   | Back Nibbit  |

## Notes

Two-Nibbit encounter with deterministic per-slot overrides (no RNG ticks at spawn).
SLICE_MOVE is Attack 6 with +5 self-block side-effect.
HISS_MOVE is Buff (+2 Strength applied to self).

## Quirk: Intent.Kind vs MoveId divergence at fixture capture

At spawn time, `MonsterIntent.FromContentIntent(monsterModel.InitialIntent, override)`
keeps `Intent.Kind` from `Nibbit.InitialIntent` (=`Attack`, BUTT-flavored) while
setting `Intent.MoveId` from the encounter override (=`SLICE_MOVE` for slot 0,
`HISS_MOVE` for slot 1). Result: this fixture captures `Kind=Attack` on both slots
even though slot 1's MoveId is HISS_MOVE (a Buff move). `ResolveEnemyIntents`
corrects Kind on the first player turn. Q2's wire-format read uses `MoveId`
exclusively, so this divergence is consumer-irrelevant. See Q1-ADR-014 Consequences.

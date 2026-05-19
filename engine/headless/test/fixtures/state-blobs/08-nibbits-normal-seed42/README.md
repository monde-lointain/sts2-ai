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

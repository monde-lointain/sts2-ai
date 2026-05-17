# Wave 10.5.β — Patch-Notes Audit v0.103.2 → v0.105.1

**Date:** 2026-05-17
**Auditor:** Project-lead
**Inputs:** `engine/headless/docs/specs/03-v0.103.2-to-v0.105.1-port-decisions.json` (post-Wave-10.5.α regen, schema v2, 110 hint-bearing rows)

## Summary

| Category | Count | Notes |
|---|---|---|
| **CONFIRMED-PORT-RELEVANT (already landed)** | 1 | Untouchable (UpgradeDelta 2→3) — Wave 10 |
| **CONFIRMED-PORT-RELEVANT (pending)** | **0** | None |
| **PATCH-NOTES-CLAIM-ONLY** | ~5 | Q1 has card stub but behavior lives in upstream-only Power/Enchantment file |
| **FALSE-POSITIVE** | ~80 | Hint surfaced but Q1 has no analogue + no Q1-relevant change |
| **MISSED-BY-TOOL** | 1 | Inky.cs (user's patch note flagged) — correlator didn't link Blade-of-Ink bold tag to Inky enchantment file |

**Headline:** **Zero pending PORT-relevant rows.** v0.103.2 → v0.105.1 is fundamentally Godot UI/multiplayer plumbing + content adds for upstream classes Q1 doesn't carry. Q1's single PORT-relevant change (Untouchable) was caught in Wave 10. Path D (compressed bridge) is the obvious win.

## Per-row audit (Q1-substrate-relevant rows only)

### CONFIRMED-PORT-RELEVANT (already landed)

| Row | Patch hint | Status |
|---|---|---|
| `src/Core/Models/Cards/Untouchable.cs` | `buffed +2→+3` (v0.104.0) | ✓ Landed Wave 10 (`UpgradeDelta = 3`) |

### PATCH-NOTES-CLAIM-ONLY (behavior lives in Q1-absent file)

| Row | Hint | Q1 file? | Real change location | Why CLAIM-ONLY |
|---|---|---|---|---|
| `src/Core/Models/Cards/Nightmare.cs` | `changed` ("Affliction stripped from clones") | Yes (stub) | upstream `NightmarePower.cs` | Q1 has no `Powers/NightmarePower.cs`; behavior would need Phase-2 power implementation |
| `src/Core/Models/Cards/Speedster.cs` | `buffed 8(10)→9(11)` | Yes | upstream `SpeedsterPower.cs` base damage | Q1 has no `Powers/SpeedsterPower.cs`; card just enqueues power |
| `src/Core/Models/Enchantments/Inky.cs` | `null` (TOOL MISS) | No | Q1 has no enchantments substrate | Can't port without Inky.cs |
| Other Powers/Relics/Monsters hint rows | various | No Q1 analogue | upstream-only classes | Q1's parallel substrate has fewer files than upstream |

### FALSE-POSITIVE (hint surfaced; Q1 not affected or no real change)

| Row | Why NOT relevant |
|---|---|
| `src/Core/Models/Cards/Abrasive.cs` | Unclassified hint; excerpt was artwork-revision list; diff is `PowerCmd.Apply(choiceContext)` threading only |
| `src/Core/Models/Cards/Flanking.cs` | Unclassified hint; "Flanking card now properly specifies" — card-text description fix; Q1 doesn't render card text |
| `src/Core/Models/Cards/Strangle.cs` | `fixed` hint; "Fixed Strangle power proccing off of OTHER PLAYER'S card plays" — multiplayer-specific bug; Q1 is single-player per Q1-ADR-009 |
| ~24 UI/scenes-ui/scenes-gameplay rows | UI/artwork; Q1 headless doesn't touch |
| ~54 upstream-only Powers/Relics/Monsters with hints | Q1 has zero analogous files in these buckets at hint-bearing paths |

### MISSED-BY-TOOL

| Row | Why missed | Recommendation |
|---|---|---|
| `src/Core/Models/Enchantments/Inky.cs` | Correlator scored low — bold tag in patch note was "Blade of Ink" (card name), not "Inky" (enchantment name). Allowlist heuristic didn't cross-link card-to-enchantment. | Future improvement (post-bridge): correlator cross-link card files to their associated enchantment/power files via reflection of `EnchantDamageAdditive` or similar references. Not blocking; Q1 has no Inky substrate today so even with the hint there's no port to do. |

## Path A vs Path D recommendation

Per Wave 10.5 plan's quantitative decision criteria:

| Confirmed PORT-pending count | Recommendation |
|---|---|
| ≤ 5 | Path D obvious win |
| 6-15 | Path D recommended |
| 16-30 | Project-lead judgment |
| > 30 | Path A (per-bucket structure helps tracking) |

**Audit result: 0 pending PORT-relevant rows.** Path D is the obvious win — by a wide margin.

### Path D path forward

Skip Waves 11, 12, 13 (Powers + Relics + Monsters batches). Proceed directly to:
- **Wave 14** (B.3 / slime port retry — independent of bridge; real content addition; ~67 LOC per Wave 6.5 precedent)
- **Wave 14.5** (Q4 canonical registry catch-up — ~54 monster+potion token-table entries; project-lead-driven; ~1 hr)
- **Wave 15** (pin-advance ceremony — `upstream-pin.json` to v0.105.1; genealogy bundle v2; tag `v0.105.1-engine-pinned`)

Skipping Waves 11-13 saves an estimated 2-3 days of wall-clock with no loss of code completeness (0 PORT pending).

### Tooling outcomes from 10.5.α (positive)

- **Correlator successfully surfaced Untouchable hint** with correct change_type + magnitude — would have caught Wave 9's miss had it been available
- **Correlator surfaced NightmarePower-class changes** with correct change_type
- **Prompt templates render hint block prominently** — Wave 11+ engineers (if dispatched) would see hints at TOP of per-row context
- **PATCH-NOTES-CLAIM-ONLY flag** ready for use; no rows triggered it in current data set (all hint rows had at least the threading diff present); will be valuable for future syncs

### Tooling gaps surfaced (defer to follow-up)

- **Card-to-Enchantment/Power cross-linking**: correlator doesn't follow content references; user-flagged Inky change wasn't surfaced
- **Allowlist heuristic over-inclusion**: scenes-ui + scenes-gameplay rows still get hints (e.g., Vakuu artwork) — heuristic needs tightening or full ModelDb introspection
- **`change_type: "unclassified"` rate is high** for non-stat changes — classifier's keyword set could expand

These are all post-bridge polish items, not blockers.

## Concrete action items

1. **User decision**: ratify Path D (skip Waves 11-13)
2. **If Path D ratified**:
   - Dispatch Wave 14 (slime port retry) per existing plan
   - Project-lead handles Wave 14.5 (Q4 registry catch-up) directly
   - Dispatch Wave 15 (pin-advance ceremony) — final wave
3. **If Path D rejected** (continue Waves 11-13): engineer prompts now carry enriched hints; expected outcome is ~0-2 PORT-pending across all 3 waves; ~3 days wall-clock

## References

- Wave 10.5.α commit: `e6487d8` (enrichment tooling)
- Cached patch notes: `tools/upstream-sync/cache/patch-notes/patch_notes_20.json` (v0.103.0 through v0.105.1 in range)
- ADR-026 (upstream-sync pipeline), ADR-027 (Q4 fixture growth policy)
- Plan: `~/.claude/plans/use-your-best-judgement-sparkling-feigenbaum.md` § Wave 10.5

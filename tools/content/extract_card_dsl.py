#!/usr/bin/env python3
"""
extract_card_dsl.py — ADR-035 Amendment §1 verified 12-pattern DSL extractor.

Usage:
    python tools/content/extract_card_dsl.py
        Runs extraction only; writes tools/content/extracted-card-dsl-silent.json.

    python tools/content/extract_card_dsl.py --reseed
        Runs extraction then re-seeds contracts/registry/phase1-silent.json.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# ---------------------------------------------------------------------------
# Paths (absolute per project convention)
# ---------------------------------------------------------------------------
CARDS_DIR = Path("/home/clydew372/development/projects/godot/sts2/src/Core/Models/Cards")
REGISTRY_PATH = Path(
    "/home/clydew372/development/projects/cpp/sts2-ai/.claude/worktrees/agent-a1ea96689ec03c4ff/contracts/registry/phase1-silent.json"
)
OUTPUT_PATH = Path(
    "/home/clydew372/development/projects/cpp/sts2-ai/.claude/worktrees/agent-a1ea96689ec03c4ff/tools/content/extracted-card-dsl-silent.json"
)

K_UNKNOWN_MAX = 25
TOTAL_CARDS = 98

# ---------------------------------------------------------------------------
# Pattern helpers
# ---------------------------------------------------------------------------


def _dv_name(expr: str) -> str:
    """Extract DynamicVar name from base.DynamicVars.<Name>… or DynamicVars[\"<Name>\"]…"""
    m = re.search(r"base\.DynamicVars\.(\w+)", expr)
    if m:
        return m.group(1)
    m = re.search(r'DynamicVars\["(\w+)"\]', expr)
    if m:
        return m.group(1)
    return "Unknown"


def _power_type(expr: str) -> str:
    """Extract T from PowerCmd.Apply<T>(…)"""
    m = re.search(r"PowerCmd\.Apply<(\w+)>", expr)
    return m.group(1) if m else "UnknownPower"


# ---------------------------------------------------------------------------
# Core extractor: given file text + name, return (effects, coverage)
# ---------------------------------------------------------------------------


def _extract_on_play_body(text: str) -> tuple[str, int]:
    """Return (on_play_body, start_line_number_of_body) or ('', 0)."""
    lines = text.splitlines()
    in_body = False
    brace_depth = 0
    body_lines: list[str] = []
    start_line = 0

    for i, line in enumerate(lines, start=1):
        if not in_body:
            if re.search(r"override\s+async\s+Task\s+OnPlay\b", line):
                in_body = True
                start_line = i
        if in_body:
            body_lines.append(line)
            brace_depth += line.count("{") - line.count("}")
            if brace_depth <= 0 and len(body_lines) > 1:
                break

    return "\n".join(body_lines), start_line


def _is_noop_card(text: str) -> bool:
    """No OnPlay body AND (Unplayable keyword OR only OnTurnEndInHand)."""
    has_on_play = bool(re.search(r"override\s+async\s+Task\s+OnPlay\b", text))
    has_unplayable = bool(re.search(r"CardKeyword\.Unplayable", text))
    has_turn_end_in_hand = bool(re.search(r"override\s+.*Task\s+OnTurnEndInHand\b", text))
    # Noop: no OnPlay AND (unplayable OR has turn-end-in-hand only)
    return not has_on_play and (has_unplayable or has_turn_end_in_hand)


def extract_effects(card_name: str, text: str) -> tuple[list[dict], str]:  # noqa: ARG001
    """
    Returns (effects_list, coverage).
    coverage: "extracted" | "noop" | "unknown_dominant"
    """
    # --- Noop check (unplayable curse/status with no OnPlay body) ---
    if _is_noop_card(text):
        return [{"op": "noop"}], "noop"

    body, start_line = _extract_on_play_body(text)
    if not body:
        # Has no OnPlay at all and not unplayable → unknown
        return [
            {"op": "unknown", "source": "card-extractor", "upstream_line": 0}
        ], "unknown_dominant"

    lines = body.splitlines()
    effects: list[dict] = []

    for rel_idx, line in enumerate(lines):
        abs_line = start_line + rel_idx

        stripped = line.strip()

        # ---- attack: DamageCmd.Attack(<BV>).WithHitCount first (Skewer X-cost pattern) ----
        if re.search(
            r"DamageCmd\.Attack\(base\.DynamicVars\.(\w+)\.BaseValue\)\.WithHitCount\(", stripped
        ):
            dv = _dv_name(stripped)
            eff: dict = {"op": "attack", "base_var": dv}
            _add_hit_count(eff, stripped, body, abs_line)
            context = "\n".join(lines[rel_idx : rel_idx + 10])
            if re.search(r"TargetingAllOpponents\b", context):
                eff["target"] = "all_enemies"
            elif re.search(r"TargetingRandomOpponents\b", context):
                eff["target"] = "random_enemies"
            elif re.search(r"\.Targeting\(cardPlay\.Target\)", context):
                eff["target"] = "single"
            else:
                eff["target"] = "unknown_target"
            effects.append(eff)
            continue

        # ---- attack: DamageCmd.Attack(<BV>) standard ----
        if re.search(r"DamageCmd\.Attack\(base\.DynamicVars\.(\w+)\.BaseValue\)", stripped):
            dv = _dv_name(stripped)
            # Determine target — check current + next lines
            context = "\n".join(lines[rel_idx : rel_idx + 10])
            if re.search(r"TargetingAllOpponents\b", context):
                eff = {"op": "attack", "base_var": dv, "target": "all_enemies"}
                _add_hit_count(eff, context, body, abs_line)
            elif re.search(r"TargetingRandomOpponents\b", context):
                eff = {"op": "attack", "base_var": dv, "target": "random_enemies"}
                _add_hits_var_random(eff, context)
            elif re.search(r"\.Targeting\(cardPlay\.Target\)", context):
                eff = {"op": "attack", "base_var": dv, "target": "single"}
                _add_hit_count(eff, context, body, abs_line)
            else:
                eff = {
                    "op": "unknown",
                    "source": "card-extractor",
                    "upstream_line": abs_line,
                }
            effects.append(eff)
            continue

        # ---- block_self ----
        m = re.search(
            r'CreatureCmd\.GainBlock\(\s*base\.Owner\.Creature\s*,\s*base\.DynamicVars(?:\.(\w+)|\["(\w+)"\])\s*,\s*cardPlay\)',
            stripped,
        )
        if m:
            dv = m.group(1) or m.group(2)
            effects.append({"op": "block_self", "base_var": dv})
            continue

        # ---- apply_power: self ----
        m = re.search(
            r"PowerCmd\.Apply<(\w+)>\(\s*choiceContext\s*,\s*base\.Owner\.Creature\s*,\s*"
            r'(base\.DynamicVars(?:\.\w+|\["\w+"\])(?:\.\w+)*|-?[\d]+(?:\.\d+)?m?)\s*,\s*base\.Owner\.Creature',
            stripped,
        )
        if m:
            power = m.group(1)
            bv_expr = m.group(2)
            eff = {"op": "apply_power", "power": power, "target": "self"}
            _fill_base(eff, bv_expr)
            effects.append(eff)
            continue

        # ---- apply_power: single (cardPlay.Target) ----
        m = re.search(
            r"PowerCmd\.Apply<(\w+)>\(\s*choiceContext\s*,\s*cardPlay\.Target\s*,\s*"
            r'(base\.DynamicVars(?:\.\w+|\["\w+"\])(?:\.\w+)*|-?[\d]+(?:\.\d+)?m?)',
            stripped,
        )
        if m:
            power = m.group(1)
            bv_expr = m.group(2)
            eff = {"op": "apply_power", "power": power, "target": "single"}
            _fill_base(eff, bv_expr)
            effects.append(eff)
            continue

        # ---- apply_power: all enemies (foreach ... HittableEnemies) ----
        if re.search(r"foreach\b.*HittableEnemies", stripped):
            lookahead = "\n".join(lines[rel_idx : rel_idx + 8])
            m2 = re.search(
                r"PowerCmd\.Apply<(\w+)>\(\s*choiceContext\s*,\s*\w+\s*,\s*"
                r'(base\.DynamicVars(?:\.\w+|\["\w+"\])(?:\.\w+)*|-?[\d]+(?:\.\d+)?m?)',
                lookahead,
            )
            if m2:
                power = m2.group(1)
                bv_expr = m2.group(2)
                eff = {"op": "apply_power", "power": power, "target": "all_enemies"}
                _fill_base(eff, bv_expr)
                effects.append(eff)
            continue

        # ---- draw ----
        m = re.search(
            r"CardPileCmd\.Draw\(\s*choiceContext\s*,\s*"
            r'(base\.DynamicVars(?:\.\w+|\["\w+"\])(?:\.\w+)*|[\d]+(?:\.\d+)?m?)\s*,\s*base\.Owner\s*\)',
            stripped,
        )
        if m:
            bv_expr = m.group(1)
            if "DynamicVars" in bv_expr:
                effects.append({"op": "draw", "base_var": _dv_name(bv_expr)})
            else:
                lit = re.sub(r"m$", "", bv_expr)
                try:
                    effects.append({"op": "draw", "base": int(float(lit))})
                except ValueError:
                    effects.append({"op": "draw", "base_var": "Unknown"})
            continue

        # ---- gain_energy ----
        m = re.search(
            r'PlayerCmd\.GainEnergy\(\s*(base\.DynamicVars(?:\.\w+|\["\w+"\])\.IntValue)',
            stripped,
        )
        if m:
            effects.append({"op": "gain_energy", "base_var": _dv_name(m.group(1))})
            continue

        # ---- create_shivs (3-arg form):
        # Shiv.CreateInHand(base.Owner, <DV.IntValue>|<lit>, base.CombatState) ----
        m = re.search(
            r"Shiv\.CreateInHand\(\s*base\.Owner\s*,\s*"
            r'(base\.DynamicVars(?:\.\w+|\["\w+"\])\.IntValue|[\d]+)\s*,\s*base\.CombatState\s*\)',
            stripped,
        )
        if m:
            bv_expr = m.group(1)
            if "DynamicVars" in bv_expr:
                effects.append({"op": "create_shivs", "base_var": _dv_name(bv_expr)})
            else:
                try:
                    effects.append({"op": "create_shivs", "base": int(bv_expr)})
                except ValueError:
                    effects.append({"op": "create_shivs", "base_var": "Unknown"})
            continue

        # ---- create_shivs (loop form):
        # for (int i = 0; i < DV.IntValue; i++) { Shiv.CreateInHand(base.Owner, combatState); }
        # Also handles literal loop bounds.
        m_loop = re.search(
            r'for\s*\(\s*int\s+\w+\s*=\s*0\s*;\s*\w+\s*<\s*(base\.DynamicVars(?:\.\w+|\["\w+"\])\.IntValue|[\d]+)\s*;',
            stripped,
        )
        if m_loop:
            # Check that loop body creates shivs (look ahead)
            lookahead = "\n".join(lines[rel_idx : rel_idx + 6])
            if re.search(r"Shiv\.CreateInHand\(", lookahead):
                bv_expr = m_loop.group(1)
                if "DynamicVars" in bv_expr:
                    effects.append({"op": "create_shivs", "base_var": _dv_name(bv_expr)})
                else:
                    try:
                        effects.append({"op": "create_shivs", "base": int(bv_expr)})
                    except ValueError:
                        effects.append({"op": "create_shivs", "base_var": "Unknown"})
            continue

    # If no structured effects found → unknown
    if not effects:
        effects = [{"op": "unknown", "source": "card-extractor", "upstream_line": start_line}]

    # Determine coverage
    ops = [e["op"] for e in effects]
    all_noop = all(op == "noop" for op in ops)
    all_unknown = all(op == "unknown" for op in ops)
    has_known = any(op not in ("unknown", "noop") for op in ops)

    if all_noop:
        coverage = "noop"
    elif all_unknown:
        coverage = "unknown_dominant"
    elif has_known:
        coverage = "extracted"
    else:
        coverage = "unknown_dominant"

    return effects, coverage


def _fill_base(eff: dict, bv_expr: str) -> None:
    """Populate base_var or base on eff from a base value expression."""
    if "DynamicVars" in bv_expr:
        eff["base_var"] = _dv_name(bv_expr)
    else:
        lit = re.sub(r"m$", "", bv_expr)
        negative = lit.startswith("-")
        lit = lit.lstrip("-")
        try:
            val = int(float(lit))
            eff["base"] = -val if negative else val
        except ValueError:
            eff["base_var"] = "Unknown"


def _add_hit_count(eff: dict, text: str, full_body: str, abs_line: int) -> None:  # noqa: ARG001
    """Augment eff in-place with hits/hits_var from WithHitCount(…)."""
    m = re.search(r"\.WithHitCount\(\s*base\.DynamicVars\.(\w+)\.IntValue\s*\)", text)
    if m:
        eff["hits_var"] = m.group(1)
        return
    m = re.search(r'\.WithHitCount\(\s*base\.DynamicVars\["(\w+)"\]\.IntValue\s*\)', text)
    if m:
        eff["hits_var"] = m.group(1)
        return
    m = re.search(r"\.WithHitCount\(\s*(\d+)\s*\)", text)
    if m:
        eff["hits"] = int(m.group(1))
        return
    if re.search(r"\.WithHitCount\(\s*ResolveEnergyXValue\(\)", text):
        eff["hits_var"] = "EnergyX"
        return
    if re.search(r"\.WithHitCount\(", text):
        eff["hits_var"] = "Calculated"


def _add_hits_var_random(eff: dict, text: str) -> None:
    """Add hits_var for TargetingRandomOpponents…WithHitCount(…)."""
    m = re.search(r"\.WithHitCount\(\s*base\.DynamicVars\.(\w+)\.IntValue\s*\)", text)
    if m:
        eff["hits_var"] = m.group(1)
        return
    m = re.search(r"\.WithHitCount\(\s*(\d+)\s*\)", text)
    if m:
        eff["hits"] = int(m.group(1))


# ---------------------------------------------------------------------------
# Registry lookup
# ---------------------------------------------------------------------------


def _load_silent_card_names() -> list[str]:
    with REGISTRY_PATH.open() as f:
        reg = json.load(f)
    return [t["name"] for t in reg.get("tokens", []) if t.get("kind") == "card"]


# ---------------------------------------------------------------------------
# Main extraction
# ---------------------------------------------------------------------------


def run_extraction() -> dict[str, dict]:
    """Extract all 98 Silent cards; return {token: {effects, coverage}}."""
    card_names = _load_silent_card_names()
    results: dict[str, dict] = {}

    for name in card_names:
        cs_path = CARDS_DIR / f"{name}.cs"
        token = f"card:{name}"
        if not cs_path.exists():
            results[token] = {
                "effects": [{"op": "unknown", "source": "card-extractor", "upstream_line": 0}],
                "coverage": "unknown_dominant",
            }
            continue
        text = cs_path.read_text(encoding="utf-8")
        effects, coverage = extract_effects(name, text)
        results[token] = {"effects": effects, "coverage": coverage}

    return results


# ---------------------------------------------------------------------------
# Acceptance gate
# ---------------------------------------------------------------------------


def print_gate(results: dict[str, dict]) -> bool:
    total = len(results)
    extractable = sum(1 for v in results.values() if v["coverage"] == "extracted")
    noop = sum(1 for v in results.values() if v["coverage"] == "noop")
    unknown_dominant = sum(1 for v in results.values() if v["coverage"] == "unknown_dominant")
    ok = unknown_dominant <= K_UNKNOWN_MAX

    print(f"total_cards = {total}")
    print(f"extractable = {extractable}")
    print(f"noop = {noop}")
    print(f"unknown_dominant = {unknown_dominant}")
    print(f"K_UNKNOWN_MAX_OK = {ok}")

    if not ok:
        print("WARN: K_UNKNOWN_MAX exceeded; surface to orchestrator.")

    return ok


# ---------------------------------------------------------------------------
# Re-seed registry
# ---------------------------------------------------------------------------


def reseed_registry(extracted: dict[str, dict], registry_path: Path) -> None:
    with registry_path.open() as f:
        reg = json.load(f)

    # Bump schema version 0.0 → 0.1
    reg["manifest"]["schema_version"] = {"major": 0, "minor": 1}

    new_dsl: list[dict] = []
    for entry in reg.get("card_dsl", []):
        token = entry["token"]
        new_entry = {k: v for k, v in entry.items() if k not in ("effects",)}
        if token in extracted:
            new_entry["effects"] = extracted[token]["effects"]
            new_entry["coverage"] = extracted[token]["coverage"]
        else:
            new_entry["effects"] = [
                {"op": "unknown", "source": "card-extractor", "upstream_line": 0}
            ]
            new_entry["coverage"] = "unknown_dominant"
        new_dsl.append(new_entry)

    reg["card_dsl"] = new_dsl

    with registry_path.open("w") as f:
        json.dump(reg, f, indent=2)
        f.write("\n")

    print(f"Registry re-seeded: {registry_path}")


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def main() -> None:
    parser = argparse.ArgumentParser(description="ADR-035 §1 DSL extractor for Silent cards")
    parser.add_argument(
        "--reseed", action="store_true", help="After extraction, re-seed phase1-silent.json"
    )
    args = parser.parse_args()

    print("Extracting Silent card DSL…")
    results = run_extraction()

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT_PATH.open("w") as f:
        json.dump(results, f, indent=2)
        f.write("\n")
    print(f"Written: {OUTPUT_PATH}")

    print()
    ok = print_gate(results)

    if args.reseed:
        print()
        reseed_registry(results, REGISTRY_PATH)

    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()

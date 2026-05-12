#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path("/home/clydew372/development/projects/cs/sts2-headless/test/fixtures/q4-manifest-phase1.json")
OUT = ROOT / "contracts" / "registry" / "phase1-silent.json"


KINDS = [
    ("special", ["[CLS]", "[MASK]", "[CHAR_SILENT]", "[ACT_1]", "[ACT_2]", "[ACT_3]"]),
    ("card", None),
    ("relic", None),
    ("power", None),
    ("enemy", None),
]


def token_hash(kind: str, name: str) -> str:
    return hashlib.sha256(f"{kind}:{name}".encode("utf-8")).hexdigest()


def main() -> int:
    manifest = json.loads(SOURCE.read_text(encoding="utf-8"))
    tokens = []
    next_id = 1
    for kind, fixed in KINDS:
        names = fixed if fixed is not None else manifest[kind + "s" if kind != "enemy" else "monsters"]
        for name in names:
            token = f"{kind}:{name}" if kind != "special" else name
            tokens.append(
                {
                    "token_id": next_id,
                    "token": token,
                    "kind": kind,
                    "name": name,
                    "content_hash": token_hash(kind, name),
                    "since_version": "phase1-silent.0",
                    "deprecated_in": None,
                    "references": [],
                }
            )
            next_id += 1

    registry = {
        "manifest": {
            "version": "phase1-silent.0",
            "schema_version": {"major": 0, "minor": 0},
            "parent_version": None,
            "source_fixture": str(SOURCE),
            "upstream_source": "/home/clydew372/development/projects/godot/sts2/src",
        },
        "tokens": tokens,
        "deprecation_log": [],
        "card_dsl": [
            {
                "token": token["token"],
                "cost": "unknown",
                "type": "stub",
                "target": "unknown",
                "effects": [{"op": "stub", "source": "phase1-seed"}],
            }
            for token in tokens
            if token["kind"] == "card"
        ],
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(registry, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""PreToolUse hook: track edits to module specs and the system overview.

Soft tracker — does NOT block. Appends entry to
`.claude/state/spec-edits-pending-resolution.json`. Enforcement happens at
push-time via `pre-push-spec-resolution-gate.py` (per ADR-024).

A spec edit without a corresponding substrate commit (or explicit
`doc-only:` commit flag) is a candidate for semantic drift — the gate
will flag it. The badge convention from ADR-023 means specs and code
should advance together; this hook records the spec-side edit so the
gate can pair it.
"""

import json
import os
import re
import subprocess
import sys
import time

try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)

tool = data.get("tool_name")
if tool not in ("Edit", "Write", "MultiEdit"):
    sys.exit(0)

file_path = data.get("tool_input", {}).get("file_path", "") or ""
if not re.search(r"docs/specs/(modules/.+|00-system-overview)\.md$", file_path):
    sys.exit(0)

try:
    common = subprocess.run(
        ["git", "rev-parse", "--git-common-dir"], capture_output=True, text=True, check=True
    ).stdout.strip()
    root = os.path.dirname(os.path.abspath(common))
    head_sha = subprocess.run(
        ["git", "rev-parse", "HEAD"], capture_output=True, text=True, check=True
    ).stdout.strip()
except Exception:
    sys.exit(0)

state_path = os.path.join(root, ".claude/state/spec-edits-pending-resolution.json")
os.makedirs(os.path.dirname(state_path), exist_ok=True)

try:
    with open(state_path) as f:
        state = json.load(f)
except (FileNotFoundError, json.JSONDecodeError):
    state = {"entries": []}

rel_path = os.path.relpath(file_path, root) if os.path.isabs(file_path) else file_path
state["entries"].append(
    {
        "file": rel_path,
        "edited_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "agent": data.get("tool_name", "unknown"),
        "head_sha_at_edit": head_sha,
        "resolution": None,
    }
)

tmp = state_path + f".tmp.{os.getpid()}"
with open(tmp, "w") as f:
    json.dump(state, f, indent=2)
os.replace(tmp, state_path)

sys.stderr.write(
    f"NOTE: spec-edit-tracker logged edit to {rel_path}. "
    "Pair with a substrate commit in the same PR, or mark commit "
    "message with `doc-only:` if intentional (per ADR-024).\n"
)
sys.exit(0)

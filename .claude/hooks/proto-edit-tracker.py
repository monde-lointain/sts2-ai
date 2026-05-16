#!/usr/bin/env python3
"""PreToolUse hook: track edits to `contracts/schemas/*.proto`.

Soft tracker — does NOT block. Appends entry to
`.claude/state/proto-edits-pending-adr.json`. Enforcement happens at
push-time via `pre-push-proto-adr-gate.py`.

Per schema-bump skill: proto edits must be paired with an ADR
(ADR-019/ADR-022 precedent). This hook records the edit so the pre-push
gate can detect missing ADR refs.
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
if not re.search(r"contracts/schemas/.*\.proto$", file_path):
    sys.exit(0)

# Resolve project root via git-common-dir
try:
    common = subprocess.run(
        ["git", "rev-parse", "--git-common-dir"],
        capture_output=True, text=True, check=True
    ).stdout.strip()
    root = os.path.dirname(os.path.abspath(common))
    head_sha = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        capture_output=True, text=True, check=True
    ).stdout.strip()
except Exception:
    sys.exit(0)  # don't block if git unavailable

state_path = os.path.join(root, ".claude/state/proto-edits-pending-adr.json")
os.makedirs(os.path.dirname(state_path), exist_ok=True)

try:
    with open(state_path) as f:
        state = json.load(f)
except (FileNotFoundError, json.JSONDecodeError):
    state = {"entries": []}

# Use repo-relative file path for portability
rel_path = os.path.relpath(file_path, root) if os.path.isabs(file_path) else file_path
state["entries"].append({
    "file": rel_path,
    "edited_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    "agent": data.get("tool_name", "unknown"),
    "head_sha_at_edit": head_sha,
    "adr_ref": None,
})

tmp = state_path + f".tmp.{os.getpid()}"
with open(tmp, "w") as f:
    json.dump(state, f, indent=2)
os.replace(tmp, state_path)

sys.stderr.write(
    f"NOTE: proto-edit-tracker logged edit to {rel_path}. "
    "Pair with an ADR before pushing to main "
    "([[bumping-a-schema-version]]).\n"
)
sys.exit(0)

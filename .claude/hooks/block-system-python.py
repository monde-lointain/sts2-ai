#!/usr/bin/env python3
"""PreToolUse hook: block bare system python invocations.

Project rule: Python must run via .venv/bin/python (or absolute venv path).
System python lacks project deps (numpy/torch/proto) and fails silently on
imports. Allow `python -c "..."` for inline diagnostics.

Reads Claude Code hook JSON from stdin. Exit 2 to block (stderr → model),
0 to allow. Non-Bash tools pass through.
"""
import json
import re
import sys

try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)

if data.get("tool_name") != "Bash":
    sys.exit(0)

command = data.get("tool_input", {}).get("command", "") or ""

# Match `python` (or python3, python3.12) at a command boundary,
# not preceded by `.venv/bin/` or `/.venv/bin/` (absolute venv path).
BARE = re.compile(
    r"(?:^|[\s;&|`(])"
    r"(?!\S*\.venv/bin/)"
    r"python(?:\d+(?:\.\d+)?)?"
    r"(?=\s|$)"
)
# Exception: python -c "..." is allowed (quick inline diagnostics)
INLINE_C = re.compile(r"(?:^|[\s;&|`(])python(?:\d+(?:\.\d+)?)?\s+-c\b")

if BARE.search(command) and not INLINE_C.search(command):
    sys.stderr.write(
        "BLOCKED by block-system-python hook: bare 'python' invocation.\n"
        "Use .venv/bin/python (or absolute venv path). System python lacks\n"
        "project deps and will silently fail on imports.\n"
        f"Command: {command}\n"
    )
    sys.exit(2)

sys.exit(0)

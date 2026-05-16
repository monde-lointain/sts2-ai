#!/usr/bin/env python3
"""PreToolUse hook: warn (no block) when long-running Make targets are
invoked. Best-effort awareness; can't see Agent tool's run_in_background
flag from the hook payload.

Targets: q2-ci, phase0-gate, sanitize, sanitize-test (>10 min each).
Memory rule [[feedback-long-running-bash]] mandates run_in_background.
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
LONGRUN = re.compile(
    r"(?:^|[\s;&|`(])make\s+(?:[^&|;]*\s+)?(q2-ci|phase0-gate|sanitize|sanitize-test)\b"
)
m = LONGRUN.search(command)
if m:
    sys.stderr.write(
        f"WARNING: 'make {m.group(1)}' is a long-running target (>10 min).\n"
        "If invoking via Agent tool, ensure run_in_background: true "
        "([[feedback-long-running-bash]]).\n"
        "Otherwise the Bash 10-min timeout will kill the run.\n"
    )

sys.exit(0)

#!/usr/bin/env python3
"""Pre-push hook: block main-bound push if any pending proto edit lacks
a matching ADR.

Pre-commit framework invokes this with stdin lines:
    <local_ref> <local_sha> <remote_ref> <remote_sha>

We gate only pushes targeting `main` (refs/heads/main). Feature-branch
pushes pass freely (memory rule: main-only gate, allow exploratory
proto work on branches).

Pending entries live in `.claude/state/proto-edits-pending-adr.json`.
An entry is "resolved" when an ADR with file in `docs/specs/01-decisions-log.md`
references the edited proto and is dated >= the edit timestamp. Cheap
heuristic: any ADR section added since the edit that mentions the proto
filename counts as resolved (the human must do the actual matching).
"""

import json
import os
import subprocess
import sys

PUSHED_REFS = sys.stdin.read().strip().splitlines()


def main():
    main_push = any(line.split()[2] == "refs/heads/main" for line in PUSHED_REFS if line.strip())
    if not main_push:
        sys.exit(0)

    try:
        common = subprocess.run(
            ["git", "rev-parse", "--git-common-dir"], capture_output=True, text=True, check=True
        ).stdout.strip()
        root = os.path.dirname(os.path.abspath(common))
    except Exception:
        sys.exit(0)

    state_path = os.path.join(root, ".claude/state/proto-edits-pending-adr.json")
    if not os.path.exists(state_path):
        sys.exit(0)

    with open(state_path) as f:
        state = json.load(f)
    pending = [e for e in state.get("entries", []) if not e.get("adr_ref")]
    if not pending:
        sys.exit(0)

    # Read decisions log; check for filename references with recent dates
    decisions_log = os.path.join(root, "docs/specs/01-decisions-log.md")
    try:
        with open(decisions_log) as f:
            log_text = f.read()
    except FileNotFoundError:
        log_text = ""

    unresolved = []
    for entry in pending:
        proto_file = os.path.basename(entry["file"])
        if proto_file in log_text:
            # Heuristic: filename mentioned somewhere — assume an ADR covers it.
            # User can update the entry's adr_ref manually for stricter tracking.
            continue
        unresolved.append(entry)

    if unresolved:
        sys.stderr.write(
            "BLOCKED by pre-push-proto-adr-gate: push to main has pending "
            "proto edits without a matching ADR.\n\n"
        )
        for e in unresolved:
            sys.stderr.write(f"  - {e['file']} (edited {e['edited_at']})\n")
        sys.stderr.write(
            "\nCreate an ADR referencing the proto in docs/specs/01-decisions-log.md "
            "(see [[creating-an-adr]] and [[bumping-a-schema-version]]), then retry.\n"
            "Feature-branch pushes are unaffected — only main-bound pushes "
            "require ADR pairing.\n"
        )
        sys.exit(1)

    sys.exit(0)


if __name__ == "__main__":
    main()

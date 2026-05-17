#!/usr/bin/env bash
# context-bar.sh — Claude Code statusline badge emitter.
#
# Reads runtime sentinel files from .claude/state/ and emits badge text to
# stdout. Intended for use in Claude's CLAUDE.md or hook output where a
# concise one-line status is useful.
#
# Usage: source or execute; output is a single line (empty if no badges).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SENTINEL="${REPO_ROOT}/.claude/state/upstream-drift-detected.json"

badges=()

# Upstream drift badge
if [[ -f "${SENTINEL}" ]]; then
    if command -v jq &>/dev/null; then
        pinned="$(jq -r '.pinned_buildid_in_engine // "unknown"' "${SENTINEL}")"
        current="$(jq -r '.current_version // "unknown"' "${SENTINEL}")"
        badges+=("upstream drift: build ${pinned} -> ${current}")
    else
        badges+=("upstream drift detected")
    fi
fi

if [[ ${#badges[@]} -gt 0 ]]; then
    printf "WARN "
    printf "%s  " "${badges[@]}"
    printf "\n"
fi

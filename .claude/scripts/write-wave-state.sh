#!/usr/bin/env bash
# Atomic write to .claude/state/{current-wave,active-worktrees}.json or
# snapshot to .claude/state/waves/<N>.json. Uses jq for safety.
# Args:
#   <kind> = current-wave | active-worktrees | wave-snapshot
#   <wave_n>
#   <json-payload>  (must be valid JSON; the wrapper validates)
set -euo pipefail
kind="$1"; wave_n="$2"; payload="$3"
command -v jq >/dev/null || { echo "jq required" >&2; exit 2; }
echo "$payload" | jq empty   # validate
root="$(dirname "$(git rev-parse --git-common-dir)")"
case "$kind" in
  current-wave)      out="$root/.claude/state/current-wave.json" ;;
  active-worktrees)  out="$root/.claude/state/active-worktrees.json" ;;
  wave-snapshot)     out="$root/.claude/state/waves/${wave_n}.json" ;;
  *) echo "unknown kind: $kind" >&2; exit 2 ;;
esac
mkdir -p "$(dirname "$out")"
tmp="$out.tmp.$$"
echo "$payload" | jq . > "$tmp"
mv "$tmp" "$out"

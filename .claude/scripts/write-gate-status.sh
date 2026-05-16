#!/usr/bin/env bash
# Atomic write to .claude/state/last-gate.json
# Args: <quantum> <target> <status> <exit_code> <duration_s> <head_sha>
# Status: pass | fail | running
set -euo pipefail
quantum="$1"; target="$2"; status="$3"; exit_code="$4"; duration_s="$5"; head_sha="$6"
root="$(dirname "$(git rev-parse --git-common-dir)")"
out="$root/.claude/state/last-gate.json"
mkdir -p "$(dirname "$out")"
tmp="$out.tmp.$$"
now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$tmp" <<EOF
{
  "quantum": "$quantum",
  "target": "$target",
  "status": "$status",
  "exit_code": $exit_code,
  "duration_s": $duration_s,
  "started_at": "$now",
  "ended_at": "$now",
  "head_sha": "$head_sha"
}
EOF
mv "$tmp" "$out"

#!/usr/bin/env bash
# Atomic write to .claude/state/ci-rescue.json
# Subcommands:
#   init <branch> <head_sha> <run_id> <workflow> <max_iter> <auto_push>
#   update-status <new-status>            (running|green|escalated|idle)
#   record-iteration <category> <summary> <commit_sha> <error_signature>
#   escalate <reason>
#   reset
# Pass --force as final arg on init/reset to override an active running rescue.
set -euo pipefail
command -v jq >/dev/null || { echo "jq required" >&2; exit 2; }
root="$(dirname "$(git rev-parse --git-common-dir)")"
out="$root/.claude/state/ci-rescue.json"
mkdir -p "$(dirname "$out")"
now="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

read_status() {
  [[ -s "$out" ]] || { echo "idle"; return; }
  jq -r '.status // "idle"' "$out" 2>/dev/null || echo "idle"
}

atomic_write() {
  local payload="$1"
  echo "$payload" | jq empty
  local tmp="$out.tmp.$$"
  echo "$payload" | jq . > "$tmp"
  mv "$tmp" "$out"
}

require_not_running() {
  local force="${1:-}"
  if [[ "$(read_status)" == "running" && "$force" != "--force" ]]; then
    echo "refusing to overwrite active running rescue; pass --force to override" >&2
    exit 3
  fi
}

cmd="${1:?subcommand required}"; shift || true

case "$cmd" in
  init)
    branch="$1"; head_sha="$2"; run_id="$3"; workflow="$4"; max_iter="$5"; auto_push="$6"
    force="${7:-}"
    require_not_running "$force"
    payload=$(jq -n \
      --arg status "running" \
      --arg branch "$branch" \
      --arg head_sha "$head_sha" \
      --argjson run_id "$run_id" \
      --arg workflow "$workflow" \
      --argjson max_iter "$max_iter" \
      --argjson auto_push "$auto_push" \
      --arg now "$now" \
      '{
        status: $status,
        branch: $branch,
        head_sha: $head_sha,
        current_run_id: $run_id,
        workflow: $workflow,
        iteration_count: 0,
        max_iterations: $max_iter,
        auto_push: $auto_push,
        last_error_signature: null,
        attempted_fixes: [],
        started_ts: $now,
        last_update_ts: $now,
        escalation_reason: null
      }')
    atomic_write "$payload"
    ;;
  update-status)
    new_status="$1"
    case "$new_status" in
      running|green|escalated|idle) ;;
      *) echo "invalid status: $new_status" >&2; exit 2 ;;
    esac
    [[ -s "$out" ]] || { echo "no state file to update; run init first" >&2; exit 2; }
    payload=$(jq --arg s "$new_status" --arg now "$now" \
      '.status = $s | .last_update_ts = $now' "$out")
    atomic_write "$payload"
    ;;
  record-iteration)
    category="$1"; summary="$2"; commit_sha="$3"; error_signature="$4"
    case "$category" in
      compiler-error|test-failure|lint|unactionable) ;;
      *) echo "invalid category: $category" >&2; exit 2 ;;
    esac
    [[ -s "$out" ]] || { echo "no state file to update; run init first" >&2; exit 2; }
    payload=$(jq \
      --arg cat "$category" \
      --arg sum "$summary" \
      --arg csha "$commit_sha" \
      --arg sig "$error_signature" \
      --arg now "$now" \
      '.iteration_count += 1
       | .last_error_signature = $sig
       | .last_update_ts = $now
       | .attempted_fixes += [{
           iteration: .iteration_count,
           category: $cat,
           summary: $sum,
           commit_sha: $csha
         }]' "$out")
    atomic_write "$payload"
    ;;
  escalate)
    reason="$1"
    case "$reason" in
      same-error-twice|iteration-cap|unactionable|divr|cancelled-run|gh-auth-failure) ;;
      *) echo "invalid reason: $reason" >&2; exit 2 ;;
    esac
    [[ -s "$out" ]] || { echo "no state file to update; run init first" >&2; exit 2; }
    payload=$(jq --arg r "$reason" --arg now "$now" \
      '.status = "escalated" | .escalation_reason = $r | .last_update_ts = $now' "$out")
    atomic_write "$payload"
    ;;
  reset)
    force="${1:-}"
    require_not_running "$force"
    atomic_write '{"status":"idle"}'
    ;;
  *)
    echo "unknown subcommand: $cmd" >&2
    echo "usage: $0 {init|update-status|record-iteration|escalate|reset} ..." >&2
    exit 2
    ;;
esac

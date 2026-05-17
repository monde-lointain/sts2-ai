#!/usr/bin/env bash
# update-port-decision-status.sh — Idempotent per-row status update for port-decision JSON sidecars.
#
# Usage:
#   update-port-decision-status.sh <version> <row_path> <new_status> [--wave N] [--stream ID]
#       version    — upstream version string, e.g. v0.105.1
#       row_path   — upstream relative path of the row, e.g. src/Monsters/Slime.cs
#       new_status — one of: PENDING | DISPATCHED | MERGED | DEFERRED | NO_ACTION_NEEDED
#       --wave N   — (optional) wave number (int or float, e.g. 10 or 10.5)
#                    populated into the row's "wave" field when flipping to MERGED.
#       --stream ID — (optional) stream identifier string, e.g. "10.5.α" or "B.2-γ"
#                    populated into the row's "stream_id" field when flipping to MERGED.
#
#   update-port-decision-status.sh --batch <wave_branch> [--wave N] [--stream ID]
#       wave_branch — git branch name; derives rows from git diff --name-only main..<branch>
#                     cross-referenced against all JSON sidecars, bumps matching rows to MERGED.
#       --wave N / --stream ID — same as single-row mode; applied to all matched rows.
#
# Sidecar path convention:
#   engine/headless/docs/specs/<prefix>-port-decisions.json
#   (discovered by glob; matched by version string in the filename or sidecar metadata)
#
# Soft-fail contract: missing sidecar logs a warning and exits 0. Never blocks /wave-close.
#
# Requirements: jq, git (for --batch mode)

set -euo pipefail

VALID_STATUSES="PENDING|DISPATCHED|MERGED|DEFERRED|NO_ACTION_NEEDED"

# Locate git repo root (works from worktree or main repo)
_git_root() {
    git rev-parse --show-toplevel 2>/dev/null || {
        echo "ERROR: not inside a git repository" >&2
        exit 1
    }
}

# Find all port-decision JSON sidecars under the repo
_find_sidecars() {
    local root="$1"
    find "$root/engine/headless/docs/specs" -maxdepth 1 -name "*-port-decisions.json" 2>/dev/null
}

# Update a single row in a sidecar file. Idempotent.
# Args: sidecar_path  row_path  new_status  [wave]  [stream_id]
#   wave     — wave number string (e.g. "10" or "10.5") or "" to skip
#   stream_id — stream identifier string (e.g. "10.5.α") or "" to skip
_update_row() {
    local sidecar="$1"
    local row_path="$2"
    local new_status="$3"
    local wave_arg="${4:-}"
    local stream_arg="${5:-}"

    if [[ ! -f "$sidecar" ]]; then
        echo "WARN: sidecar not found: $sidecar — skipping (soft-fail)" >&2
        return 0
    fi

    command -v jq >/dev/null 2>&1 || {
        echo "WARN: jq not found — cannot update sidecar $sidecar (soft-fail)" >&2
        return 0
    }

    # Check the row exists
    local match
    match=$(jq --arg p "$row_path" '.rows[] | select(.path == $p) | .path' "$sidecar" 2>/dev/null || true)
    if [[ -z "$match" ]]; then
        echo "WARN: row_path '$row_path' not found in $sidecar — skipping" >&2
        return 0
    fi

    # Check current status — skip if already at target (idempotent)
    local current
    current=$(jq --arg p "$row_path" '.rows[] | select(.path == $p) | .status' "$sidecar" | tr -d '"')
    if [[ "$current" == "$new_status" && -z "$wave_arg" && -z "$stream_arg" ]]; then
        echo "INFO: $row_path already at status $new_status in $sidecar — no-op" >&2
        return 0
    fi

    # Never flip NO_ACTION_NEEDED rows — they are terminal at generation time
    if [[ "$current" == "NO_ACTION_NEEDED" ]]; then
        echo "WARN: $row_path is NO_ACTION_NEEDED (terminal) — refusing to flip to $new_status" >&2
        return 0
    fi

    # Build the jq update expression. Always set status; conditionally set wave/stream_id.
    local tmp="${sidecar}.tmp.$$"
    local jq_expr
    jq_expr='(.rows[] | select(.path == $p) | .status) |= $s'

    # Populate wave field if provided (parse as number for JSON correctness).
    if [[ -n "$wave_arg" ]]; then
        jq_expr="${jq_expr} | (.rows[] | select(.path == \$p) | .wave) |= (\$w | tonumber)"
    fi

    # Populate stream_id field if provided.
    if [[ -n "$stream_arg" ]]; then
        jq_expr="${jq_expr} | (.rows[] | select(.path == \$p) | .stream_id) |= \$sid"
    fi

    # Build jq arg list.
    local jq_args=("--arg" "p" "$row_path" "--arg" "s" "$new_status")
    if [[ -n "$wave_arg" ]]; then
        jq_args+=("--arg" "w" "$wave_arg")
    fi
    if [[ -n "$stream_arg" ]]; then
        jq_args+=("--arg" "sid" "$stream_arg")
    fi

    jq "${jq_args[@]}" "$jq_expr" "$sidecar" > "$tmp"
    mv "$tmp" "$sidecar"

    local wave_info=""
    [[ -n "$wave_arg" ]] && wave_info=" wave=$wave_arg"
    [[ -n "$stream_arg" ]] && wave_info="${wave_info} stream_id=$stream_arg"
    echo "INFO: $sidecar: $row_path → $new_status (was: $current)${wave_info}"
}

# Single-row mode: update-port-decision-status.sh <version> <row_path> <new_status> [--wave N] [--stream ID]
_mode_single() {
    local version="$1"
    local row_path="$2"
    local new_status="$3"
    shift 3
    local wave_arg=""
    local stream_arg=""

    # Parse optional --wave / --stream flags
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --wave)
                wave_arg="$2"
                shift 2
                ;;
            --stream)
                stream_arg="$2"
                shift 2
                ;;
            *)
                echo "ERROR: unknown flag '$1'" >&2
                exit 1
                ;;
        esac
    done

    local root
    root=$(_git_root)

    # Validate status
    if ! echo "$new_status" | grep -qE "^($VALID_STATUSES)$"; then
        echo "ERROR: invalid status '$new_status'. Valid: $VALID_STATUSES" >&2
        exit 1
    fi

    # Find matching sidecar by version string in filename
    local found=0
    while IFS= read -r sidecar; do
        if [[ "$sidecar" == *"$version"* ]]; then
            _update_row "$sidecar" "$row_path" "$new_status" "$wave_arg" "$stream_arg"
            found=1
        fi
    done < <(_find_sidecars "$root")

    if [[ "$found" -eq 0 ]]; then
        echo "WARN: no sidecar found matching version '$version' under $root/engine/headless/docs/specs/ — skipping (soft-fail)" >&2
    fi
}

# Batch mode: --batch <wave_branch> [--wave N] [--stream ID]
# Derives changed upstream paths from git diff, matches against all sidecars, bumps to MERGED.
_mode_batch() {
    local branch="$1"
    shift 1
    local wave_arg=""
    local stream_arg=""

    # Parse optional --wave / --stream flags
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --wave)
                wave_arg="$2"
                shift 2
                ;;
            --stream)
                stream_arg="$2"
                shift 2
                ;;
            *)
                echo "ERROR: unknown flag '$1'" >&2
                exit 1
                ;;
        esac
    done

    local root
    root=$(_git_root)

    # Get files changed in the wave branch vs main
    local changed_paths
    changed_paths=$(git -C "$root" diff --name-only "main..${branch}" 2>/dev/null) || {
        echo "WARN: could not get diff for branch '$branch' — skipping batch update (soft-fail)" >&2
        return 0
    }

    if [[ -z "$changed_paths" ]]; then
        echo "INFO: no files changed in branch '$branch' vs main — nothing to update"
        return 0
    fi

    local sidecars
    sidecars=$(_find_sidecars "$root")

    if [[ -z "$sidecars" ]]; then
        echo "WARN: no port-decision JSON sidecars found — skipping batch update (soft-fail)" >&2
        return 0
    fi

    # For each sidecar, cross-reference changed paths against sidecar rows
    while IFS= read -r sidecar; do
        [[ -f "$sidecar" ]] || continue
        command -v jq >/dev/null 2>&1 || {
            echo "WARN: jq not found — cannot process $sidecar" >&2
            continue
        }

        # Extract all row paths from sidecar
        while IFS= read -r row_path; do
            [[ -z "$row_path" ]] && continue
            # Check if any changed file corresponds to this upstream path
            # Convention: upstream src path maps to ported engine path via last path component
            local basename
            basename=$(basename "$row_path" .cs)
            if echo "$changed_paths" | grep -q "$basename"; then
                _update_row "$sidecar" "$row_path" "MERGED" "$wave_arg" "$stream_arg"
            fi
        done < <(jq -r '.rows[].path' "$sidecar" 2>/dev/null || true)
    done <<< "$sidecars"

    echo "INFO: batch status update complete for branch '$branch'"
}

# Entry point
main() {
    if [[ $# -lt 1 ]]; then
        echo "Usage:" >&2
        echo "  $(basename "$0") <version> <row_path> <new_status> [--wave N] [--stream ID]" >&2
        echo "  $(basename "$0") --batch <wave_branch> [--wave N] [--stream ID]" >&2
        exit 1
    fi

    if [[ "$1" == "--batch" ]]; then
        if [[ $# -lt 2 ]]; then
            echo "ERROR: --batch requires <wave_branch>" >&2
            exit 1
        fi
        # Pass remaining args (including --wave/--stream) to _mode_batch
        _mode_batch "${@:2}"
    else
        if [[ $# -lt 3 ]]; then
            echo "ERROR: single-row mode requires <version> <row_path> <new_status>" >&2
            exit 1
        fi
        # Pass remaining args (including --wave/--stream) to _mode_single
        _mode_single "$@"
    fi
}

main "$@"

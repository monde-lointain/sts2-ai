#!/usr/bin/env bash
# update-port-decision-status.sh — Idempotent per-row status update for port-decision JSON sidecars.
#
# Usage:
#   update-port-decision-status.sh <version> <row_path> <new_status>
#       version    — upstream version string, e.g. v0.105.1
#       row_path   — upstream relative path of the row, e.g. src/Monsters/Slime.cs
#       new_status — one of: PENDING | DISPATCHED | MERGED | DEFERRED | NO_ACTION_NEEDED
#
#   update-port-decision-status.sh --batch <wave_branch>
#       wave_branch — git branch name; derives rows from git diff --name-only main..<branch>
#                     cross-referenced against all JSON sidecars, bumps matching rows to MERGED.
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
# Args: sidecar_path  row_path  new_status
_update_row() {
    local sidecar="$1"
    local row_path="$2"
    local new_status="$3"

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
    if [[ "$current" == "$new_status" ]]; then
        echo "INFO: $row_path already at status $new_status in $sidecar — no-op" >&2
        return 0
    fi

    # Never flip NO_ACTION_NEEDED rows — they are terminal at generation time
    if [[ "$current" == "NO_ACTION_NEEDED" ]]; then
        echo "WARN: $row_path is NO_ACTION_NEEDED (terminal) — refusing to flip to $new_status" >&2
        return 0
    fi

    # Write atomically via tmp file
    local tmp="${sidecar}.tmp.$$"
    jq --arg p "$row_path" --arg s "$new_status" \
        '(.rows[] | select(.path == $p) | .status) |= $s' \
        "$sidecar" > "$tmp"
    mv "$tmp" "$sidecar"
    echo "INFO: $sidecar: $row_path → $new_status (was: $current)"
}

# Single-row mode: update-port-decision-status.sh <version> <row_path> <new_status>
_mode_single() {
    local version="$1"
    local row_path="$2"
    local new_status="$3"
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
            _update_row "$sidecar" "$row_path" "$new_status"
            found=1
        fi
    done < <(_find_sidecars "$root")

    if [[ "$found" -eq 0 ]]; then
        echo "WARN: no sidecar found matching version '$version' under $root/engine/headless/docs/specs/ — skipping (soft-fail)" >&2
    fi
}

# Batch mode: --batch <wave_branch>
# Derives changed upstream paths from git diff, matches against all sidecars, bumps to MERGED.
_mode_batch() {
    local branch="$1"
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
                _update_row "$sidecar" "$row_path" "MERGED"
            fi
        done < <(jq -r '.rows[].path' "$sidecar" 2>/dev/null || true)
    done <<< "$sidecars"

    echo "INFO: batch status update complete for branch '$branch'"
}

# Entry point
main() {
    if [[ $# -lt 1 ]]; then
        echo "Usage:" >&2
        echo "  $(basename "$0") <version> <row_path> <new_status>" >&2
        echo "  $(basename "$0") --batch <wave_branch>" >&2
        exit 1
    fi

    if [[ "$1" == "--batch" ]]; then
        if [[ $# -lt 2 ]]; then
            echo "ERROR: --batch requires <wave_branch>" >&2
            exit 1
        fi
        _mode_batch "$2"
    else
        if [[ $# -lt 3 ]]; then
            echo "ERROR: single-row mode requires <version> <row_path> <new_status>" >&2
            exit 1
        fi
        _mode_single "$1" "$2" "$3"
    fi
}

main "$@"

#!/usr/bin/env bash
# cron-sync-check.sh — Local crontab wrapper for upstream drift detection.
#
# Activates the project .venv, runs `make sync-check`, and on drift writes
# a sentinel to .claude/state/upstream-drift-detected.json.
#
# Install via:
#   crontab -e
#   0 */6 * * * /path/to/repo/tools/upstream-sync/scripts/cron-sync-check.sh >> /tmp/sts2-upstream-cron.log 2>&1
#
# Lock contention: `make sync-check` calls the Python tool which manages its
# own lockfile. Concurrent cron runs will see a stderr error from the Python
# layer (non-zero exit); this script will propagate that exit via set -e.
# No flock here — lockfile responsibility belongs to the Python tool (A.2).
#
# Steam dependency: `make sync-check` queries Steam API; if Steam is
# unreachable or STEAM_HOME is unset/invalid, the Python tool exits non-zero.
# This script treats that as a soft failure (warns, does not write sentinel).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Walk up to repo root (tools/upstream-sync/scripts/ -> tools/upstream-sync/ -> tools/ -> repo/)
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

VENV="${REPO_ROOT}/.venv"
STATE_DIR="${REPO_ROOT}/.claude/state"
SENTINEL="${STATE_DIR}/upstream-drift-detected.json"
LOGPREFIX="[cron-sync-check $(date -Iseconds)]"

if [[ ! -f "${VENV}/bin/activate" ]]; then
    echo "${LOGPREFIX} ERROR: .venv not found at ${VENV}. Run: python -m venv ${VENV} && ${VENV}/bin/pip install -e tools/upstream-sync" >&2
    exit 1
fi

# Activate venv
# shellcheck source=/dev/null
source "${VENV}/bin/activate"

echo "${LOGPREFIX} Running make sync-check from ${REPO_ROOT}"

# Capture output + exit code without aborting on non-zero (Steam unavailable etc.)
set +e
sync_output="$(cd "${REPO_ROOT}" && make sync-check 2>&1)"
sync_exit=$?
set -e

if [[ ${sync_exit} -ne 0 ]]; then
    echo "${LOGPREFIX} WARN: make sync-check exited ${sync_exit}. Possible Steam unavailability or lock contention — not writing sentinel." >&2
    echo "${sync_output}" >&2
    exit 0
fi

echo "${sync_output}"

# Parse drift signal from sync-check output.
# The Python tool exits 0 with a machine-readable line when drift is detected:
#   DRIFT_DETECTED current_buildid=<B> current_version=<V> current_dll_sha256=<H> pinned_buildid=<P>
# If the Python tool doesn't emit this line, no drift — clean exit.
if echo "${sync_output}" | grep -q "^DRIFT_DETECTED "; then
    drift_line="$(echo "${sync_output}" | grep "^DRIFT_DETECTED ")"
    current_buildid="$(echo "${drift_line}" | grep -oP 'current_buildid=\K[^ ]+')"
    current_version="$(echo "${drift_line}" | grep -oP 'current_version=\K[^ ]+')"
    current_dll_sha256="$(echo "${drift_line}" | grep -oP 'current_dll_sha256=\K[^ ]+')"
    pinned_buildid="$(echo "${drift_line}" | grep -oP 'pinned_buildid=\K[^ ]+')"
    detected_at="$(date -Iseconds)"

    mkdir -p "${STATE_DIR}"
    tmp="${SENTINEL}.tmp.$$"
    cat > "${tmp}" <<EOF
{
  "detected_at": "${detected_at}",
  "current_buildid": "${current_buildid}",
  "current_version": "${current_version}",
  "current_dll_sha256": "${current_dll_sha256}",
  "pinned_buildid_in_engine": "${pinned_buildid}"
}
EOF
    mv "${tmp}" "${SENTINEL}"
    echo "${LOGPREFIX} Drift detected — sentinel written to ${SENTINEL}"
else
    # No drift: remove stale sentinel if present
    if [[ -f "${SENTINEL}" ]]; then
        rm -f "${SENTINEL}"
        echo "${LOGPREFIX} No drift — removed stale sentinel"
    else
        echo "${LOGPREFIX} No drift — sentinel clean"
    fi
fi

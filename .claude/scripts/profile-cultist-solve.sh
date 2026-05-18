#!/usr/bin/env bash
# Profile cultist solve: peak RSS (OS-authoritative via /usr/bin/time -v),
# wall-clock, TT size, solve correctness. Writes JSON report.
# Args:
#   $1 = output JSON path (absolute or repo-relative)
# Usage:
#   .claude/scripts/profile-cultist-solve.sh .claude/state/profiles/wave-19-pre.json
#
# Methodology rationale: see plan-the-q2-oracle-glittery-pony.md §9.
# - /usr/bin/time -v: OS PEAK RSS counter; captures all process memory
# - gtest_filter isolates cultist solve from other tests' memory residue
# - Release build measures optimized footprint
# - Clean rebuild prevents stale artifact contamination
# - PASSED check prevents writing garbage JSON if test fails mid-run

set -euo pipefail

[[ $# -eq 1 ]] || { echo "usage: $0 <out_json_path>" >&2; exit 2; }
OUT="$1"

command -v /usr/bin/time >/dev/null || { echo "GNU /usr/bin/time -v required" >&2; exit 2; }

REPO_ROOT="$(git rev-parse --show-toplevel)"
BUILD_DIR="${REPO_ROOT}/build-profile"
TEST_BIN="${BUILD_DIR}/Release/sts2_simulator_tests"
TIME_LOG="$(mktemp)"
STDOUT_LOG="$(mktemp)"
trap 'rm -f "${TIME_LOG}" "${STDOUT_LOG}"' EXIT

# Clean rebuild — pre/post measurements must reflect from-scratch Release
# build, not incremental artifacts that could carry mixed pre/post symbols.
# Top-level CMake lives at repo root (engine/cpp/ is a subdirectory); the
# project's binary output convention places executables under
# ${BUILD_DIR}/Release/ (set via CMAKE_RUNTIME_OUTPUT_DIRECTORY).
rm -rf "${BUILD_DIR}"
cmake -B "${BUILD_DIR}" -S "${REPO_ROOT}" \
      -DCMAKE_BUILD_TYPE=Release >/dev/null
cmake --build "${BUILD_DIR}" --target sts2_simulator_tests -j"$(nproc)" >/dev/null

[[ -x "${TEST_BIN}" ]] || { echo "test binary not found at ${TEST_BIN}" >&2; exit 1; }

/usr/bin/time -v -o "${TIME_LOG}" \
  "${TEST_BIN}" \
  --gtest_filter='Search.DISABLED_StarterCombatSolves_LogsDiagnostics' \
  --gtest_also_run_disabled_tests \
  > "${STDOUT_LOG}" 2>&1

# Sanity-check the test PASSED before parsing solve outputs — a crash or
# assertion-failure would leave stdout in a partial state that would write
# garbage JSON downstream.
grep -q '\[  PASSED  \] 1 test' "${STDOUT_LOG}" || {
  echo "cultist solve did NOT pass; aborting profile" >&2
  cat "${STDOUT_LOG}" >&2
  exit 1
}

MAX_RSS_KB="$(grep 'Maximum resident set size' "${TIME_LOG}" | grep -oE '[0-9]+')"
WALL_S="$(grep 'Elapsed (wall clock)' "${TIME_LOG}" | awk -F': ' '{print $2}')"
USER_S="$(grep 'User time' "${TIME_LOG}" | awk -F': ' '{print $2}')"
TT_SIZE="$(grep -oE 'tt_size=[0-9]+' "${STDOUT_LOG}" | head -1 | cut -d= -f2)"
EXP_HP="$(grep -oE 'expected_hp=[0-9.]+' "${STDOUT_LOG}" | head -1 | cut -d= -f2)"
EXP_R="$(grep -oE 'expected_rounds=[0-9.]+' "${STDOUT_LOG}" | head -1 | cut -d= -f2)"
ELAPSED_MS="$(grep -oE 'elapsed_ms=[0-9]+' "${STDOUT_LOG}" | head -1 | cut -d= -f2)"

mkdir -p "$(dirname "${OUT}")"

cat > "${OUT}" <<EOF
{
  "profiled_at": "$(date -u +%FT%TZ)",
  "git_sha": "$(git rev-parse HEAD)",
  "host": "$(uname -n)",
  "cpu_model": "$(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | xargs)",
  "peak_rss_kb": ${MAX_RSS_KB},
  "peak_rss_gb": $(awk "BEGIN { printf \"%.3f\", ${MAX_RSS_KB}/1024/1024 }"),
  "wall_clock": "${WALL_S}",
  "user_time": "${USER_S}",
  "tt_size": ${TT_SIZE},
  "expected_hp": ${EXP_HP},
  "expected_rounds": ${EXP_R},
  "elapsed_ms": ${ELAPSED_MS}
}
EOF

echo "wrote ${OUT}"

#!/usr/bin/env bash
# ISSUE-312 gate. `dotnet test` prints `Passed! - Failed: 0, Passed: N` and can exit 0
# even when the run ABORTS (test-host crash, a killed process, resource contention). A
# partial run then looks identical to a full green suite to any scripted gate — CI,
# /implement-all, or an agent — and the next regression hides in the tests that never ran
# (this already happened: an aborted run read as green missed a test red 358 days/year).
#
# This wrapper forces a NON-ZERO exit on any VSTest abort marker, regardless of dotnet's
# own exit code, so an aborted run can never be mistaken for a pass. A clean run passes
# through dotnet's real exit code unchanged.
#
# Usage:  scripts/run-backend-tests.sh [any dotnet-test args...]
#   e.g.  scripts/run-backend-tests.sh src/backend/HRM.sln --no-build -c Release
#
# Works under bash on Linux CI and Git Bash on Windows. DOTNET_BIN overrides the binary
# (used only by the self-test to replay canned transcripts); it defaults to `dotnet`.
set -uo pipefail

log="$(mktemp)"
trap 'rm -f "$log"' EXIT

# Stream to the console AND capture for post-run analysis. PIPESTATUS[0] is dotnet's own
# exit code (tee would otherwise mask it).
"${DOTNET_BIN:-dotnet}" test "$@" 2>&1 | tee "$log"
code=${PIPESTATUS[0]}

# VSTest abort markers. Any of these means the run did NOT complete — a `Passed!` line
# above them covers only the subset that ran before the crash.
if grep -qiE 'Test Run Aborted|Test host process crashed|The active test run was aborted|Aborted with error' "$log"; then
  {
    echo ""
    echo "ISSUE-312 GATE: the backend test run ABORTED -- forcing FAILURE (a partial run is not a pass)."
    echo "  Any 'Passed!' line above covers only the tests that ran before the crash."
    echo "  Re-run on an idle machine (no parallel builds/agents), or split the Testcontainers pass out."
  } >&2
  exit 1
fi

exit "$code"

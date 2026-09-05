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
#
# GAP-031: set COVERAGE=1 to collect line coverage (coverlet.collector is already referenced by
# HRM.Tests but had never once been invoked, so tech-doc §6.6's >=70%/>=85% target could be neither
# met nor missed — nobody knew the number). Off by default because it slows the run; CI turns it on.
# Deliberately MEASURE-ONLY for now: no threshold is enforced, because setting a gate before anyone
# has seen the figure is how you end up lowering the gate.
set -uo pipefail

# ── ISSUE-492: refuse to test a DIFFERENT tree than the one you are standing in ────────────────
# `dotnet test src/backend/HRM.sln` resolves that path against $PWD. Both the main checkout and every
# .claude/worktrees/* worktree contain a valid solution at the SAME relative path, so invoking this from
# the repo root while working in a worktree silently tests the WRONG TREE — and reports `Passed!` for
# code that was never compiled.
#
# Observed 2026-09-04: an agent working in a worktree ran this from the repo root, got `Passed! 3`, and
# that run had executed the MAIN tree's copy of the class, without the agent's new test in it at all. The
# real run, from the worktree, was RED. A pass was reported for code that did not run.
#
# Fail LOUDLY on the mismatch rather than testing the wrong thing quietly. Silence is the whole defect.
for arg in "$@"; do
  case "$arg" in
    *.sln|*.slnx|*.csproj)
      [ -e "$arg" ] || continue
      target_root="$(cd "$(dirname "$arg")" && git rev-parse --show-toplevel 2>/dev/null || true)"
      here_root="$(git rev-parse --show-toplevel 2>/dev/null || true)"
      if [ -n "$target_root" ] && [ -n "$here_root" ] && [ "$target_root" != "$here_root" ]; then
        {
          echo "ISSUE-492 GATE: refusing to run -- the solution you passed lives in a DIFFERENT working tree."
          echo "  you are in : $here_root"
          echo "  target is  : $target_root  ($arg)"
          echo ""
          echo "  Both trees hold a solution at the same relative path, so this would have tested the other"
          echo "  one and printed a 'Passed!' line for code that never ran. cd to the tree you mean."
        } >&2
        exit 1
      fi
      ;;
  esac
done

coverage_args=()
if [ "${COVERAGE:-0}" = "1" ]; then
  coverage_args=(--collect:"XPlat Code Coverage")
fi

log="$(mktemp)"
trap 'rm -f "$log"' EXIT

# Stream to the console AND capture for post-run analysis. PIPESTATUS[0] is dotnet's own
# exit code (tee would otherwise mask it).
"${DOTNET_BIN:-dotnet}" test "$@" ${coverage_args[@]+"${coverage_args[@]}"} 2>&1 | tee "$log"
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

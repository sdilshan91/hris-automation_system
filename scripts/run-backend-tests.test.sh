#!/usr/bin/env bash
# Self-test for run-backend-tests.sh (ISSUE-312). Replays canned VSTest transcripts + exit
# codes through a fake `dotnet` and asserts the gate's verdict. No real build/suite needed.
#   run:  bash scripts/run-backend-tests.test.sh
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
gate="$here/run-backend-tests.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# A fake `dotnet` that ignores its args, replays a transcript file, and exits with a code.
make_fake() { # $1 = transcript file, $2 = exit code
  cat > "$work/fake-dotnet" <<EOF
#!/usr/bin/env bash
cat "$1"
exit $2
EOF
  chmod +x "$work/fake-dotnet"
}

FAILED=0
run_case() { # $1 name, $2 transcript, $3 fake-exit, $4 expected-gate-exit
  make_fake "$2" "$3"
  DOTNET_BIN="$work/fake-dotnet" bash "$gate" dummy.sln >/dev/null 2>&1
  local got=$?
  if [ "$got" -eq "$4" ]; then
    echo "PASS: $1 (gate exit $got)"
  else
    echo "FAIL: $1 (gate exit $got, expected $4)"
    FAILED=1
  fi
}

# 1. THE defect: an aborted run that dotnet still reported as Passed!/exit 0 -> gate MUST fail.
printf 'Test Run Aborted.\nThe active test run was aborted. Reason: Test host process crashed\nPassed! - Failed: 0, Passed: 964\n' > "$work/aborted.txt"
run_case "aborted-but-reported-passed-exit0" "$work/aborted.txt" 0 1

# 2. A clean full pass (exit 0) -> gate passes it through.
printf 'Passed! - Failed: 0, Passed: 4058\n' > "$work/clean.txt"
run_case "clean-full-pass" "$work/clean.txt" 0 0

# 3. Genuine test failures (exit 1) -> gate passes the non-zero through.
printf 'Failed! - Failed: 3, Passed: 4055\n' > "$work/failed.txt"
run_case "real-failures-exit1" "$work/failed.txt" 1 1

# 4. Aborted AND non-zero already -> still fails (abort detection is independent of exit code).
run_case "aborted-and-exit1" "$work/aborted.txt" 1 1

if [ "$FAILED" -eq 0 ]; then
  echo "ALL GATE SELF-TESTS PASSED"
else
  echo "GATE SELF-TESTS FAILED"
fi
exit $FAILED

#!/usr/bin/env bash
# Self-test for no-verify-guard.py.
#
# EIGHT of the thirteen DENY cases below were live BYPASSES on 2026-09-01 (marked
# `# was-bypass`): the guard only inspected the first token of a command segment, so any
# wrapper — env, nice, time, sudo, a FOO=1 assignment, or a `bash -c` payload — hid the
# `git` from it and a --no-verify commit sailed straight through a hook whose whole job
# is to stop that. The other five already worked and are here as regression cover.
#
# The distinction is written down because an earlier draft of this header claimed ALL
# thirteen were bypasses. That was false, and nothing would have contradicted it: a
# comment asserting an invariant is not evidence the code holds it. Verified by replaying
# every case against the pre-fix guard — 8 allowed, 5 denied.
#
# The ALLOW cases matter just as much. A deny-guard with false positives gets disabled,
# and then it protects nothing: `git log -n 5` must not be mistaken for `commit -n`, and
# a commit *message* that mentions --no-verify is not a bypass.
#   run:  bash .claude/hooks/scripts/no-verify-guard.test.sh
set -uo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
guard="$here/no-verify-guard.py"
pass=0; fail=0

verdict() { # $1 = command string -> prints allow|deny
  # No `|| echo allow` fallback here: under `set -o pipefail` a failing guard makes the
  # parser print its own "allow" default AND the fallback fire, doubling the output so
  # every comparison fails — including cases that should pass. Capture once, default once.
  local out
  out="$(printf '{"tool_name":"Bash","tool_input":{"command":%s}}' \
    "$(python3 -c 'import json,sys;print(json.dumps(sys.argv[1]))' "$1")" \
  | python3 "$guard" 2>/dev/null \
  | python3 -c 'import json,sys;d=sys.stdin.read().strip();print(json.loads(d).get("hookSpecificOutput",{}).get("permissionDecision","allow") if d else "allow")' 2>/dev/null)"
  printf '%s' "${out:-allow}"
}

expect() { # $1 = allow|deny, $2 = command
  local got; got="$(verdict "$2")"
  if [ "$got" = "$1" ]; then
    pass=$((pass+1))
  else
    fail=$((fail+1)); printf '  FAIL expected %-5s got %-5s : %s\n' "$1" "$got" "$2"
  fi
}

# --- must DENY: hook bypasses, however they are dressed up ---
expect deny "git commit --no-verify -m x"
expect deny "git commit -n -m x"
expect deny "env git commit --no-verify -m x"   # was-bypass
expect deny "nice git commit --no-verify -m x"   # was-bypass
expect deny "nice -n 10 git commit --no-verify -m x"   # was-bypass
expect deny "time git commit --no-verify -m x"   # was-bypass
expect deny "sudo git commit --no-verify -m x"   # was-bypass
expect deny "FOO=1 git commit --no-verify -m x"   # was-bypass
expect deny "env FOO=1 nice git push --no-verify"   # was-bypass
expect deny "bash -c 'git commit --no-verify -m x'"   # was-bypass
expect deny "true; git commit --no-verify -m x"
expect deny "cd /tmp && git commit -n -m x"
expect deny "git -c core.hooksPath=/dev/null commit -m x"

# --- must ALLOW: legitimate commands a false positive would wedge ---
expect allow "git status"
expect allow "git commit -m x"
expect allow "git push origin main"
expect allow "git log -n 5"
expect allow "git commit -m 'mention --no-verify in the message'"
expect allow "echo 'git commit --no-verify'"
expect allow "npm test"
expect allow "nice -n 10 npm run build"
expect allow "env NODE_ENV=prod npm test"

echo
if [ "$fail" -gt 0 ]; then echo "FAIL: $fail failed, $pass passed"; exit 1; fi
echo "OK: $pass assertions passed"

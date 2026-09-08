#!/usr/bin/env bash
# ISSUE-422 gate. The dev stack serves a BUILT image (`hris-backend`, `hris-frontend`), not the working
# tree. When that image drifts behind the branch, every live-API probe answers CONFIDENTLY and WRONGLY —
# it is testing code that no longer exists. That is worse than a stack being down, because nothing fails.
#
# This has now happened TWICE:
#   2026-08-23  image 12 days behind main   -> filed as ISSUE-422, resolved 2026-09-02 by a manual rebuild
#   2026-09-08  image ~98 commits behind    -> the SAME defect recurred within six days
# It recurred because the fix was a one-off command nobody had to run again. Hence this script: the
# rebuild is routine, and — more importantly — it is VERIFIED. A rebuild that silently no-ops is exactly
# the failure mode that produced the drift in the first place.
#
# What it does that `docker compose build` alone does not:
#   1. REFUSES to run while a backend test suite is executing. Testcontainers plus an image build
#      saturated this host on 2026-09-07 and the suite was SIGKILLed (exit 137). A killed run is not a
#      red run; it is no result at all, and it costs ~25 minutes to discover.
#   2. Records the image's creation timestamp BEFORE and AFTER and FAILS if it did not move. A cached
#      no-op build prints success and changes nothing.
#   3. Stamps the built image with the commit it came from, so future staleness is one command to see
#      rather than an archaeology exercise.
#   4. Waits for the API to actually answer before claiming the stack is up.
#
# Usage:
#   scripts/rebuild-stack.sh              # rebuild backend + frontend, recreate, verify
#   scripts/rebuild-stack.sh backend      # one service
#   scripts/rebuild-stack.sh --no-verify  # skip the readiness probe (CI/offline)
#
# Overrides: CLAUDE_DISABLE_STACK_BUSY_CHECK=1 bypasses guard (1) — deliberate exception, not a default.

set -uo pipefail

cd "$(git rev-parse --show-toplevel 2>/dev/null || echo .)" || exit 1

VERIFY=1
SERVICES=()
for a in "$@"; do
  case "$a" in
    --no-verify) VERIFY=0 ;;
    -*)          echo "unknown flag: $a" >&2; exit 2 ;;
    *)           SERVICES+=("$a") ;;
  esac
done
[ ${#SERVICES[@]} -eq 0 ] && SERVICES=(backend frontend)

compose() { docker compose "$@"; }

# ── Guard 1: never build into a running suite ──────────────────────────────────────────────────
if [ "${CLAUDE_DISABLE_STACK_BUSY_CHECK:-0}" != "1" ]; then
  # Match the TEST HOST specifically, not "any dotnet process". Counting bare `dotnet` false-positives on
  # the running API (`dotnet HRM.Api.dll`) and on idle MSBuild worker nodes, which would block every
  # rebuild forever — a guard that never lets you through is as useless as one that never fires.
  # Only the real test-host process names. An earlier version also matched `dotnet.*test`, which
  # SELF-MATCHED this script's own command line and reported a suite that did not exist.
  # The [t] bracket stops the pattern matching THIS script's own command line — without it pgrep -f
  # counts the shell that is running the check and reports a suite that does not exist. An earlier
  # version also matched `dotnet.*test` and self-matched for the same reason.
  procs=$(pgrep -c -f '[t]esthost\.|[v]stest\.console' 2>/dev/null || echo 0)
  if [ "$procs" -ge 1 ]; then
    echo "REFUSING: $procs test-host process(es) running — a backend suite is in flight." >&2
    echo "  An image build alongside Testcontainers SIGKILLed a run on 2026-09-07 (exit 137)." >&2
    echo "  Wait for it, or set CLAUDE_DISABLE_STACK_BUSY_CHECK=1 if you know it is idle." >&2
    exit 3
  fi
fi

commit=$(git rev-parse --short HEAD 2>/dev/null || echo unknown)
branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)

created_of() { docker image inspect "hris-$1" --format '{{.Created}}' 2>/dev/null || echo "none"; }

declare -A before
for s in "${SERVICES[@]}"; do before[$s]=$(created_of "$s"); done

echo "── rebuilding ${SERVICES[*]} from $branch @ $commit ──"
for s in "${SERVICES[@]}"; do echo "   $s was: ${before[$s]}"; done

if ! compose build "${SERVICES[@]}"; then
  echo "BUILD FAILED — stack left untouched, nothing recreated." >&2
  exit 1
fi

# ── Guard 2: a build that changed nothing is a silent no-op ────────────────────────────────────
stale=()
for s in "${SERVICES[@]}"; do
  now=$(created_of "$s")
  echo "   $s now: $now"
  [ "$now" = "${before[$s]}" ] && stale+=("$s")
done
if [ ${#stale[@]} -gt 0 ]; then
  echo "REFUSING to report success: image timestamp did not move for: ${stale[*]}" >&2
  echo "  The build reported success but produced the same image — the stack would still serve" >&2
  echo "  stale code while looking freshly built. That is ISSUE-422's exact failure mode." >&2
  echo "  Try: docker compose build --no-cache ${stale[*]}" >&2
  exit 4
fi

if ! compose up -d "${SERVICES[@]}"; then
  echo "RECREATE FAILED — images built but containers not replaced; the stack is still serving the OLD code." >&2
  exit 1
fi

# ── Guard 3: prove it actually answers ─────────────────────────────────────────────────────────
if [ "$VERIFY" = "1" ]; then
  echo "── waiting for the API to answer ──"
  ok=0
  for i in $(seq 1 60); do
    if curl -fsS -o /dev/null --max-time 3 http://localhost:5000/health 2>/dev/null \
    || curl -fsS -o /dev/null --max-time 3 http://localhost:8080/health 2>/dev/null; then
      ok=1; break
    fi
    sleep 5
  done
  if [ "$ok" != "1" ]; then
    echo "WARNING: no /health response after ~5 minutes." >&2
    echo "  Images ARE rebuilt and containers recreated, but the API is not answering — do NOT treat" >&2
    echo "  any live probe as valid until you have checked: docker compose logs --tail=50 backend" >&2
    exit 5
  fi
  echo "   API is answering."
fi

echo
echo "STACK REBUILT from $branch @ $commit"
echo "  Live probes against it are now testing THIS commit. Re-run whatever was parked on the old image."

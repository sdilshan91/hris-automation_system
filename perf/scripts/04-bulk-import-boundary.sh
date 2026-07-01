#!/usr/bin/env bash
# Scenario 4 — bulk-import sync->async boundary (US-CHR-010 FR-7).
# Threshold in code: <=500 rows = synchronous (inline result); >500 rows = queued Hangfire job (JobId).
# This probes BOTH sides of the boundary (500 sync, 600 async) against the perf tenant.
# Run: bash perf/scripts/04-bulk-import-boundary.sh
set -uo pipefail
BASE="${BASE_URL:-http://localhost:5000}"
SUB="${SUBDOMAIN:-perf}"
EMAIL="${EMAIL:-perfadmin@perf.test}"
PASS="${PASSWORD:-Admin@123!}"
OUT="${OUT_DIR:-./perf/results}"; mkdir -p "$OUT"
RUN="$(date +%s)"

TOKEN=$(curl -s -X POST "$BASE/api/v1/auth/login" -H "Content-Type: application/json" -H "X-Tenant-Subdomain: $SUB" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}" | grep -oE '"accessToken":"[^"]+"' | cut -d'"' -f4)
[ -z "$TOKEN" ] && { echo "LOGIN FAILED"; exit 1; }

mk_csv () { # $1=rows $2=outfile
  curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Subdomain: $SUB" \
    "$BASE/api/v1/tenant/employees/import/template?format=csv" > "$2"
  for i in $(seq 1 "$1"); do
    printf 'Imp%s,Row%s,imp_%s_%s@perf.test,,,,2024-06-01,Perf Dept 1,Perf Title 1,Full-Time,,Active\n' \
      "$i" "$i" "$RUN" "$i" >> "$2"
  done
}

probe () { # $1=rows $2=expected(sync|async)
  local rows="$1" exp="$2" f="$OUT/import-$1-$RUN.csv"
  mk_csv "$rows" "$f"
  local resp; resp=$(curl -s -H "Authorization: Bearer $TOKEN" -H "X-Tenant-Subdomain: $SUB" \
    -F "file=@$f;type=text/csv" "$BASE/api/v1/tenant/employees/import")
  echo "--- $rows rows (expect $exp) ---"
  echo "$resp" | head -c 400; echo
  if echo "$resp" | grep -qE '"jobId":"[0-9a-f-]{36}"'; then echo "=> ASYNC (jobId present)"; else echo "=> SYNC (no jobId)"; fi
}

probe 500 sync
probe 600 async
echo "DONE — CSV fixtures + responses under $OUT"

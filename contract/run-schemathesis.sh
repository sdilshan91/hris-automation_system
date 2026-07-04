#!/usr/bin/env bash
# Contract / property-based API testing via Schemathesis (report-only).
# Fuzzes the live API against its OpenAPI (Swagger) spec to catch the platform's
# recurring bug classes: unhandled 500s and FE<->BE response-shape drift
# (paginated {items,totalCount} vs bare array, envelope mismatches, /tenant/ prefix drift).
#
# SAFETY: defaults to GET-only (read-only) so it never mutates a live multi-tenant DB.
# This mirrors the read-only stance of the Postgres MCP and the report-only test loop.
# To exercise write paths you must opt in explicitly (METHODS=...) against a throwaway DB.
#
# Usage:
#   bash contract/run-schemathesis.sh                # GET-only, acme tenant, default checks
#   TENANT=techoneglobal bash contract/run-schemathesis.sh
#   TOKEN="eyJ..." bash contract/run-schemathesis.sh  # add JWT for authed endpoints
#   MAX=5 WORKERS=6 bash contract/run-schemathesis.sh
#
# Requires: uv (uvx) on PATH; the API running on $BASE with Swagger enabled (Development).

set -euo pipefail

BASE="${BASE:-http://localhost:5000}"
SPEC="${SPEC:-$BASE/swagger/v1/swagger.json}"
TENANT="${TENANT:-acme}"
METHODS="${METHODS:-GET}"          # GET-only by default; override at your own risk
CHECKS="${CHECKS:-not_a_server_error,response_schema_conformance}"
MAX="${MAX:-3}"                     # examples generated per operation
WORKERS="${WORKERS:-4}"
OUTDIR="${OUTDIR:-contract/reports}"

mkdir -p "$OUTDIR"
STAMP="$(date +%Y%m%d-%H%M%S 2>/dev/null || echo run)"
OUT="$OUTDIR/schemathesis-$STAMP.txt"

# PYTHONUTF8=1 is REQUIRED on Windows: schemathesis prints box-drawing glyphs and
# crashes under the cp1252 default when stdout is redirected to a file.
export PYTHONUTF8=1 PYTHONIOENCODING=utf-8 NO_COLOR=1
export PATH="$HOME/.local/bin:$PATH"

method_flags=()
IFS=',' read -ra M <<< "$METHODS"
for m in "${M[@]}"; do method_flags+=(--include-method "$m"); done

hdr_flags=(-H "X-Tenant-Subdomain: $TENANT")
[ -n "${TOKEN:-}" ] && hdr_flags+=(-H "Authorization: Bearer $TOKEN")

echo "Schemathesis contract run -> $OUT"
echo "  spec=$SPEC base=$BASE tenant=$TENANT methods=$METHODS checks=$CHECKS max=$MAX"

uvx schemathesis run "$SPEC" \
  -u "$BASE" \
  "${method_flags[@]}" \
  "${hdr_flags[@]}" \
  -c "$CHECKS" \
  -n "$MAX" -w "$WORKERS" \
  --exclude-deprecated \
  | tee "$OUT"

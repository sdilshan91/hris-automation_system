#!/usr/bin/env bash
# GAP-S1 — step 1 of 2: emit the API's OpenAPI document to a COMMITTED file.
#
# Why this exists: the FE↔BE contract drifted in 9 of 13 modules because the contract had two
# hand-written descriptions (C# DTOs and Angular interfaces) and nothing compared them. This script
# produces the single machine-readable description they can both be checked against; step 2
# (`npm run api:types` in src/frontend) turns it into TypeScript the compiler enforces.
#
# The document is emitted from the BUILT ASSEMBLY, not from a running server: `dotnet swagger tofile`
# constructs the app's service provider in-process, so no Kestrel, no port and — importantly — no
# database. The placeholder connection strings below exist only because Program.cs:93 registers an
# AddNpgSql health check that throws on a NULL connection string while services are being built. They
# are never connected to and carry no password (none is needed to satisfy the null check) — which also
# keeps this file clear of anything resembling a credential. That is what lets this run in CI next to
# `dotnet build`.
#
# Usage:
#   scripts/gen-openapi.sh            # regenerate contracts/openapi/hrm-v1.json in place
#   scripts/gen-openapi.sh --check    # CI mode: fail if the committed file is stale, write nothing
#
# --check is the gate that makes a C# DTO rename break the build: rename a property, forget to
# regenerate, and the committed document no longer matches the assembly.
#
# DOTNET_BIN overrides the binary (matches scripts/run-backend-tests.sh); it defaults to `dotnet`.
# CONFIGURATION selects the build configuration; it defaults to Debug (what a developer has locally). CI
# sets it to Release so this reuses the assembly the Build step just produced instead of building twice.
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
backend="$repo_root/src/backend"
out="$repo_root/contracts/openapi/hrm-v1.json"
dotnet_bin="${DOTNET_BIN:-dotnet}"
configuration="${CONFIGURATION:-Debug}"

check_mode=0
[ "${1:-}" = "--check" ] && check_mode=1

# The Swashbuckle CLI package targets net9.0 while this repo is on .NET 10, so the host must be told
# to roll forward. Without this it fails with "You must install or update .NET to run this application".
export DOTNET_ROLL_FORWARD=Major
# Development so Program.cs takes the WebApplication path (a Production host looks for a `Startup`
# type that does not exist here) and so the doc includes the same endpoint set developers see.
export ASPNETCORE_ENVIRONMENT=Development
# Non-null placeholders only — see the header note on the health-check registration.
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=openapi_gen;Username=openapi_gen"
export ConnectionStrings__PrivilegedConnection="$ConnectionStrings__DefaultConnection"

echo "==> building HRM.Api ($configuration)"
if ! "$dotnet_bin" build "$backend/HRM.Api/HRM.Api.csproj" -c "$configuration" -v q --nologo; then
  echo "gen-openapi: build failed -- cannot emit a document from an assembly that does not exist." >&2
  exit 1
fi

echo "==> restoring local dotnet tools (Swashbuckle CLI)"
if ! (cd "$backend" && "$dotnet_bin" tool restore >/dev/null); then
  echo "gen-openapi: 'dotnet tool restore' failed in $backend (see src/backend/.config/dotnet-tools.json)." >&2
  exit 1
fi

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

echo "==> emitting OpenAPI v1"
if ! (cd "$backend" && "$dotnet_bin" swagger tofile --output "$tmp" HRM.Api/bin/$configuration/net10.0/HRM.Api.dll v1) >/dev/null 2>&1; then
  # Re-run without output suppression so the real exception reaches the operator.
  (cd "$backend" && "$dotnet_bin" swagger tofile --output "$tmp" HRM.Api/bin/$configuration/net10.0/HRM.Api.dll v1) >&2
  echo "gen-openapi: swagger emission failed." >&2
  exit 1
fi

# Normalise to sorted-key, 2-space JSON. Swashbuckle's property order is not guaranteed stable across
# runs or versions, and an unstable file would make --check fail on noise instead of on real drift.
python3 - "$tmp" <<'PY'
import json, sys
path = sys.argv[1]
with open(path, encoding="utf-8") as fh:
    doc = json.load(fh)
with open(path, "w", encoding="utf-8", newline="\n") as fh:
    json.dump(doc, fh, indent=2, sort_keys=True, ensure_ascii=False)
    fh.write("\n")
PY

paths=$(python3 -c "import json,sys;print(len(json.load(open(sys.argv[1]))['paths']))" "$tmp")
schemas=$(python3 -c "import json,sys;print(len(json.load(open(sys.argv[1]))['components']['schemas']))" "$tmp")

if [ "$check_mode" -eq 1 ]; then
  if [ ! -f "$out" ]; then
    echo "gen-openapi --check: $out does not exist. Run scripts/gen-openapi.sh and commit it." >&2
    exit 1
  fi
  if ! diff -q "$out" "$tmp" >/dev/null; then
    echo "" >&2
    echo "GAP-S1 CONTRACT GATE: the committed OpenAPI document is STALE." >&2
    echo "  The API assembly now describes a different contract than contracts/openapi/hrm-v1.json." >&2
    echo "  A backend DTO/route change was made without regenerating the contract, which is exactly" >&2
    echo "  how the FE<->BE drift in 9 of 13 modules happened." >&2
    echo "" >&2
    echo "  Fix: scripts/gen-openapi.sh && (cd src/frontend && npm run api:types)  then commit both." >&2
    echo "" >&2
    diff -u "$out" "$tmp" | head -60 >&2
    exit 1
  fi
  echo "==> OK: committed contract matches the assembly ($paths paths, $schemas schemas)"
  exit 0
fi

mkdir -p "$(dirname "$out")"
cp "$tmp" "$out"
echo "==> wrote $out ($paths paths, $schemas schemas)"
echo "    next: (cd src/frontend && npm run api:types) to regenerate the TypeScript models"

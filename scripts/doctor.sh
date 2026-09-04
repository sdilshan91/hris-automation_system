#!/usr/bin/env bash
# Local capability check. The failure this exists to catch is SILENT capability loss:
# a tool the instructions promise, that is not actually installed, so the capability is
# gone and nothing says so.
#
# This already happened. `csharp-lsp` and `typescript-lsp` were installed as plugins on
# 2026-08-20 and CLAUDE.md called them "the highest-value pair of the group" — but the
# plugins ship only a README, not the language servers. Neither binary was ever on PATH,
# so semantic navigation over 2,378 C# and 781 TypeScript files silently did not exist
# for 12 days. No build failed, no test went red, no agent complained: it just quietly
# fell back to string search and nobody knew.
#
# Two tiers, because they mean different things:
#   REQUIRED   — you cannot build or test without it.          -> exit 1
#   CAPABILITY — the stack still builds, but a documented       -> exit 2
#                capability is dead and no other signal says so.
#
# Usage:  scripts/doctor.sh
set -uo pipefail

req_missing=0
cap_missing=0

check() { # tier  name  probe-cmd  install-hint
  local tier=$1 name=$2 probe=$3 hint=$4 path
  if path=$(command -v "$probe" 2>/dev/null); then
    printf '  \033[32m✓\033[0m %-28s %s\n' "$name" "$path"
  else
    printf '  \033[31m✗\033[0m %-28s MISSING — %s\n' "$name" "$hint"
    [ "$tier" = req ] && req_missing=$((req_missing+1)) || cap_missing=$((cap_missing+1))
  fi
}

echo "Required — build and test:"
check req "dotnet SDK"        dotnet "install .NET 10 SDK"
check req "node"              node   "install Node 20+"
check req "npm"               npm    "ships with node"

echo
echo "Capability — silently dead if absent:"
check cap "csharp-ls (C# LSP)"          csharp-ls                  "dotnet tool install --global csharp-ls"
check cap "typescript-language-server"  typescript-language-server "npm install -g typescript-language-server"
check cap "docker (Testcontainers)"     docker                     "integration tests need a Docker daemon"
check cap "gh (ledger-lock, PR gate)"  gh                         "install GitHub CLI — scripts/ledger-lock.sh fails open without it"
check cap "jq (ledger-lock)"           jq                         "install jq — scripts/ledger-lock.sh needs it to read open-PR file lists"

# The typescript-lsp trap: `npm i -g typescript` now resolves to 7.x (the native port),
# whose lib/ has no tsserver.js, so the language server dies at initialize. Pin to the
# project's own major or the capability is present-but-broken — worse than absent.
if command -v tsc >/dev/null 2>&1; then
  echo
  # Parse with bash builtins, not grep — and only claim a mismatch when BOTH versions
  # actually parsed. An unparseable version is "unknown", never "wrong": a doctor that
  # cries wolf gets ignored, which is the exact failure this script exists to prevent.
  gv=$(tsc --version 2>/dev/null); gv=${gv##* }
  pv=$(node -p "require('./src/frontend/package.json').devDependencies.typescript" 2>/dev/null | tr -d '^~ ')
  if [[ $gv =~ ^[0-9]+\. && $pv =~ ^[0-9]+\. ]]; then
    if [ "${gv%%.*}" != "${pv%%.*}" ]; then
      printf '  \033[31m✗\033[0m %-28s global tsc %s vs project %s — tsserver.js may be absent\n' "typescript version" "$gv" "$pv"
      printf '    fix: npm install -g typescript@%s\n' "$pv"
      cap_missing=$((cap_missing+1))
    else
      printf '  \033[32m✓\033[0m %-28s global %s matches project %s\n' "typescript version" "$gv" "$pv"
    fi
  else
    printf '  \033[33m?\033[0m %-28s could not parse (global=%s project=%s) — not treated as a failure\n' "typescript version" "${gv:-none}" "${pv:-none}"
  fi
fi

echo
if [ "$req_missing" -gt 0 ]; then
  echo "FAIL: $req_missing required tool(s) missing — the stack cannot build."; exit 1
fi
if [ "$cap_missing" -gt 0 ]; then
  echo "DEGRADED: $cap_missing documented capability/capabilities unavailable."
  echo "The build still works, but something the instructions promise does not. Fix or"
  echo "correct the docs — a documented capability that silently does not exist is worse"
  echo "than one that was never claimed."
  exit 2
fi
echo "OK: all required tools and documented capabilities present."

#!/usr/bin/env bash
# ledger-lock.sh — refuse to open a SECOND concurrent PR that writes a shared ledger.
#
# WHY THIS EXISTS
# Every merge conflict in the 2026-09-02 and 2026-09-04 sessions was two open PRs appending
# to the same ledger. Zero were in src/. The `merge=union` .gitattributes entry does NOT
# prevent this: GitHub's merge machinery ignores .gitattributes merge drivers (A/B-verified
# 2026-09-04 — the same merge conflicts on GitHub while resolving clean locally). Union only
# makes the LOCAL rebase trivial; the PR still goes DIRTY and still blocks auto-merge.
#
# A GitHub merge queue does not fix this either — it rebases with GitHub's machinery.
#
# So the only real prevention is serialization: one open PR per ledger at a time. This script
# makes that mechanical instead of remembered.
#
# USAGE
#   scripts/ledger-lock.sh              # check before opening a PR; exit 1 if a ledger is taken
#   scripts/ledger-lock.sh --rebase     # additionally rebase+push any DIRTY PR of your own
#
# Override with CLAUDE_DISABLE_LEDGER_LOCK=1 (every guard in this repo fails open by design).
set -uo pipefail

if [[ "${CLAUDE_DISABLE_LEDGER_LOCK:-0}" == "1" ]]; then
  echo "ledger-lock: disabled via CLAUDE_DISABLE_LEDGER_LOCK=1"; exit 0
fi

LEDGERS=(
  "docs/QA/TEST-FINDINGS.md"
  "docs/QA/TEST-FINDINGS-RESOLVED.md"
  "docs/QA/plans/GAP-CLOSURE-QUEUE.md"
  "docs/QA/TEST-STATUS.md"
  "docs/BA/STATUS.md"
  "docs/QA/TRACEABILITY-MATRIX.md"
)

command -v gh >/dev/null 2>&1 || { echo "ledger-lock: gh not on PATH — skipping (fail-open)"; exit 0; }

BASE="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '')"
open_json="$(gh pr list --state open --json number,headRefName,baseRefName,files,mergeStateStatus 2>/dev/null)" || {
  echo "ledger-lock: could not reach GitHub — skipping (fail-open)"; exit 0; }

# Resolve the branch this work actually targets. `origin/HEAD` is NOT it: it points at the repo default
# (`main`) while this repo develops on a long-lived working branch, so it produced a merge base hundreds
# of commits back and a diff listing most of the tree. Prefer, in order:
#   1. $LEDGER_LOCK_BASE   2. the branch open PRs target   3. the current branch's upstream   4. origin/HEAD
base_ref="${LEDGER_LOCK_BASE:-}"
if [[ -z "$base_ref" ]]; then
  base_ref="$(jq -r '[.[].baseRefName] | if length>0 then (group_by(.) | max_by(length) | .[0]) else empty end' <<<"$open_json" 2>/dev/null)"
  [[ -n "$base_ref" ]] && base_ref="origin/$base_ref"
fi
[[ -z "$base_ref" ]] && base_ref="$(git rev-parse --abbrev-ref '@{upstream}' 2>/dev/null || true)"
[[ -z "$base_ref" ]] && base_ref="$(git rev-parse --abbrev-ref origin/HEAD 2>/dev/null || true)"
merge_base="$(git merge-base HEAD "$base_ref" 2>/dev/null || echo HEAD)"

# Which ledgers does MY branch touch? The committed diff vs that merge base, PLUS staged and unstaged
# work — this is normally run BEFORE committing, and a check that sees only commits passes vacuously then.
#
# Collected into a variable rather than piped into `grep -q`. Under `set -o pipefail`, `grep -q` exits on
# the first match, SIGPIPEs `git diff`, and the PIPELINE reports failure — so the test read false EXACTLY
# when the file matched. The guard was inverted by its own safety flag, and the fixture test that "verified"
# it could not see this, because the bug was in ACQUIRING the data, not in filtering it.
changed="$( { git diff --name-only "$merge_base"...HEAD 2>/dev/null;
              git diff --name-only HEAD 2>/dev/null;
              git diff --name-only --cached 2>/dev/null; } | sort -u )"

mine=()
for f in "${LEDGERS[@]}"; do
  case $'\n'"$changed"$'\n' in
    *$'\n'"$f"$'\n'*) mine+=("$f") ;;
  esac
done

conflicts=0
for f in "${mine[@]}"; do
  holders="$(jq -r --arg f "$f" --arg me "$BASE" \
    '.[] | select(.headRefName != $me) | select([.files[].path] | index($f)) | "#\(.number) (\(.headRefName))"' \
    <<<"$open_json")"
  if [[ -n "$holders" ]]; then
    echo "ledger-lock: BLOCKED — $f is already being written by an open PR:"
    sed 's/^/    /' <<<"$holders"
    conflicts=1
  fi
done

if [[ "${1:-}" == "--rebase" ]]; then
  jq -r '.[] | select(.mergeStateStatus=="DIRTY") | .headRefName' <<<"$open_json" | while read -r br; do
    [[ -z "$br" ]] && continue
    echo "ledger-lock: rebasing DIRTY branch $br"
    git fetch -q origin "$br" && git checkout -q "$br" && \
      git rebase origin/"$(git rev-parse --abbrev-ref origin/HEAD | sed 's|^origin/||')" >/dev/null 2>&1 && \
      git push -q --force-with-lease origin "$br" && echo "    rebased and pushed" || \
      echo "    NEEDS MANUAL RESOLUTION (union did not auto-resolve — a row was rewritten, not appended)"
  done
  git checkout -q "$BASE"
fi

if [[ $conflicts -eq 1 ]]; then
  cat <<'MSG'

  Merge that PR first, then rebase and open yours. Batching bookkeeping into ONE docs PR
  is cheaper than resolving this twice — and note that a rewritten row (a queue tick) will
  NOT auto-resolve even locally, because GAP-CLOSURE-QUEUE.md is deliberately not union.
MSG
  exit 1
fi
echo "ledger-lock: OK — no other open PR writes the ledgers this branch touches"

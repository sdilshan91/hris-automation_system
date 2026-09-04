#!/usr/bin/env bash
# findings-status.sh — the heal/fix status of every finding, DERIVED from the files.
#
# WHY DERIVED AND NOT A FIELD
# Two independent axes were being conflated:
#   HEAL  — did the auto-heal protocol actually run?   documented -> scheduled -> closed-out
#   FIX   — is the defect actually fixed?              OPEN -> FIXED -> VERIFIED
# Both are already computable from the repo, so nothing is hand-maintained and nothing can go stale:
#   documented = the finding has a "### <ID>" entry in a ledger
#   scheduled  = its ID is referenced in docs/QA/plans/GAP-CLOSURE-QUEUE.md
#   deferred   = the finding line says PARKED / needs-decision / WONTFIX (a deliberate non-schedule)
#   archived   = it lives in TEST-FINDINGS-RESOLVED.md (terminal)
#
# A hand-maintained status field was rejected on evidence, not taste: on 2026-09-04 the ledger's own
# Summary table was stale by 49 entries with nothing checking it, and 14 findings sat documented-but-
# unscheduled with nothing noticing. A field that must be remembered is a field that rots.
#
# USAGE
#   scripts/findings-status.sh              # summary + the actionable gaps
#   scripts/findings-status.sh --orphans    # only findings documented but never scheduled
#   scripts/findings-status.sh --since 459  # restrict to IDs >= N (e.g. one session's findings)
set -uo pipefail
cd "$(git rev-parse --show-toplevel)" || exit 1

LIVE="docs/QA/TEST-FINDINGS.md"; ARCH="docs/QA/TEST-FINDINGS-RESOLVED.md"; QUEUE="docs/QA/plans/GAP-CLOSURE-QUEUE.md"
mode="${1:-}"; since=0
[[ "$mode" == "--since" ]] && since="${2:-0}"
[[ "${1:-}" == "--since" ]] && mode=""

queue="$(cat "$QUEUE" 2>/dev/null || true)"

declare -a orphans=() deferred=() scheduled=()
while IFS= read -r id; do
  # `id` arrives already stripped of the "### " prefix by the sed in the feeding pipeline.
  num="${id##*-}"; [[ "$num" =~ ^[0-9]+$ ]] || continue
  (( 10#$num < 10#$since )) && continue   # 10# forces base-10: IDs like 039 are NOT octal
  # the finding's own status line is the one immediately after the heading
  meta="$(grep -A1 -F "### $id " "$LIVE" | tail -1)"
  if [[ "$meta" =~ PARKED|needs-decision|WONTFIX ]]; then deferred+=("$id"); continue; fi
  if grep -qF "$id" <<<"$queue"; then scheduled+=("$id"); else orphans+=("$id"); fi
done < <(grep -oE "^### (BUG|ISSUE|ENH|DECISION)-[0-9]+" "$LIVE" 2>/dev/null | sed 's/^### //')

live_n=$(grep -cE "^### (BUG|ISSUE|ENH|DECISION)-[0-9]+" "$LIVE" 2>/dev/null || echo 0)
arch_n=$(grep -cE "^### (BUG|ISSUE|ENH|DECISION)-[0-9]+" "$ARCH" 2>/dev/null || echo 0)

if [[ "$mode" == "--orphans" ]]; then printf '%s\n' "${orphans[@]:-}"; exit 0; fi

cat <<EOF

FINDINGS STATUS  (derived — nothing here is hand-maintained)
$( [[ $since -gt 0 ]] && echo "  scope: IDs >= $since" )

  HEAL axis
    documented + scheduled ....... ${#scheduled[@]}
    documented + DEFERRED ........ ${#deferred[@]}   (parked / needs-decision / wontfix — deliberate)
    documented, NOT scheduled .... ${#orphans[@]}   <-- the gap: stored, not tracked
  FIX axis
    live (open work) ............. $live_n
    archived (terminal) .......... $arch_n

EOF
if (( ${#orphans[@]} > 0 )); then
  echo "  Documented but never scheduled — these will only ever be found by reading the whole ledger:"
  printf '    %s\n' "${orphans[@]}"
  echo
  echo "  Give each a tier in $QUEUE, or mark it PARKED/needs-decision if not scheduling it is deliberate."
fi

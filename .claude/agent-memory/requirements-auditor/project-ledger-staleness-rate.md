---
name: project-ledger-staleness-rate
description: "Stale-pessimistic" in TEST-FINDINGS.md means two different things with very different rates — fully-wasted vs overstated. Three samples now, ranging 14%–40% waste; module slices differ sharply, so quote the slice, not a global rate.
metadata:
  type: project
---

## Sample 1 — 20 random findings (2026-09-04)

**15% already fixed or obsolete** (3/20 — wholly wasted work if scheduled) but **30% carrying
stale-pessimistic content** (6/20 — the extra 3 are PARTIALLY-FIXED: root cause landed, smaller
residual remains).

**Why the distinction:** the 2026-09-01 audit's headline "29% of ledger entries are stale-pessimistic"
is ambiguous between these two readings. Scheduling decisions need the first number (waste); sizing
decisions need the second (the row overstates the work). At n=20 the 15% point estimate has a 95% CI
of roughly 3–38%, so a single sample **cannot** confirm or refute 29% — say so rather than implying
the sample settled it.

## Sample 2 — the 10-finding PAYROLL slice (2026-09-06)

Materially worse, and the failure mode was different: **only 1 of 10 was accurate as filed.**
- **40% wasted as scheduled** (BUG-456, ENH-017, ISSUE-295, ISSUE-368) — 95% CI ≈ 12–74% at n=10.
- **50% real but mis-sized** — and mis-sized *upward* as often as downward.
- ISSUE-169 was the lone accurate row.

**The dominant defect here was not "already fixed" — it was a false impact claim on top of a true
code claim.** BUG-456 correctly stated a call site omits `defaultMultiplier`; the branch that
parameter prices is **unreachable** from the sole production caller, so the "tenants are paid 1.5×
instead of 2.0×" money impact is zero. It had been tiered into the first money batch on that
sentence. Verifying the *code* claim is not enough — trace **reachability** before accepting severity.

## Sample 3 — the 22-finding LEGACY PRODUCT slice (2026-09-06)

**Back near the baseline, and it refutes the "40–50% everywhere" framing.**
- **14% fully wasted** (3/22: ISSUE-146, ENH-020, ISSUE-032) — 95% CI ≈ 3–35%.
- **45% real but materially mis-scoped** (10/22).
- **36% accurate as filed** (8/22).

So the three samples read 15% / 40% / 14%. **The payroll slice is the outlier, not the rule** — do
not generalise a module slice into a backlog-wide waste rate. Say which slice a number came from.

**The two errors that mattered most here were UNDER-statements, not waste** — both live user-facing
defects filed at or below LOW:
- `ISSUE-036` filed LOW as "attachment 5MB cap missing". The FE submits **bare filenames**, never
  uploads bytes, and the AC-3 gate only checks `attachments.Count == 0` — so a "medical certificate
  required" leave type is satisfied by typing any string. A compliance control defeated by a string,
  and its unit test seeds `attachments: ["cert.pdf"]`, so it is green.
- `ENH-008` filed as a plain ENH. `AllowedLates` is mapped to `ChronicThreshold` (5, HR escalation)
  instead of `ThresholdCount` (3, pay deduction), and the FE renders it with a banner promising a
  deduction — employees at 3–4 lates see green while pay is already docked. The wrong value is
  **enshrined in a passing unit test**, so fixing it means editing that test.

**A third recurring shape, new here: the ledger entry the code itself already refuted.**
`ISSUE-146` ("recommendation `format=pdf` → 400") was fixed 2026-07-31 and never updated. Worse, the
codebase *documents this exact failure mode happening before* — `PerformanceDashboardService.cs`
carries a note that its own header said "PDF deferred" for a long time **after** the renderer shipped
and "was still being read as an open gap" during a prior sweep. When a finding says a format/feature
is deferred, check the render/switch branch, not the header comment.

## Also observed, all three samples: scope runs in BOTH directions

Understatement is the more expensive error. ENH-009 filed against one CSV writer, defect spans 12.
ISSUE-117 names one write site, there are two. In the payroll slice: BUG-075 filed against one
service, the defect spans three (and the sniffing helper it needs already exists with five working
call sites); ISSUE-367 filed as a wording nit, is actually a live FE/BE contract break rendering
"0 active employees" to users.

**A second recurring shape: the ledger describes a gap that the platform has since grown a general
solution for.** ENH-017 asks to build a Redis cache that shipped months ago — the true residual is
one missing table name in a whitelist. Before sizing any "X is deferred/unimplemented" finding,
grep for whether a shared layer for X now exists and which siblings already adopt it.

**How to apply:** when asked "what fraction of the backlog is already fixed", give both numbers with
their definitions and the CI, and name the slice — the three samples read 15% / 40% / 14%, so no
single global rate is honest. Before tiering any finding, re-verify its scope AND its reachability
against `src/`; the filed blast radius and the filed severity are both as unreliable as the filed
status. **Always run a severity pass in the under-stated direction too** — across three samples the
most expensive errors were LOW/ENH rows hiding live user-facing defects, not the wasted rows.

Related: [[reference-gap-tracking-sources]], [[gotcha-grep-blind-source-files]].

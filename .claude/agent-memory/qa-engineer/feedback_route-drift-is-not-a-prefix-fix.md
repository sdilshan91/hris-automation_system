---
name: feedback-route-drift-is-not-a-prefix-fix
description: When correcting stale endpoint paths in TC docs, map each line to the route that actually exists by reading the step's INTENT — a mechanical prefix insertion can still yield a 404, and in an isolation TC that 404 is a fake pass
metadata:
  type: feedback
---

Never fix "route drift" in test-case docs by mechanically inserting a missing prefix. Resolve each
stale path against the live controller table, and pick by the **test step's intent**, not by string
similarity.

**Why:** ISSUE-100 (2026-09-07). The finding said the Performance TCs used `/api/v1/performance/*`
while the live prefix is `/api/v1/tenant/performance/*`, implying a global prefix insert. But
`goals/team` **never existed under any prefix** — a naive fix produces a route that still 404s.
Worse, one of the three stale lines was in `TC-PRF-ISO-001`, a tenant-isolation TC: an isolation step
aimed at a non-existent route **passes by 404**, which is a fake isolation arm (same defect class as
ISSUE-517 / [[project_isolation-arm-must-be-falsified]]). The finding's premise was also partly
false — the FE was never broken; every service under `features/performance/services/` already used
the correct prefix.

**How to apply:**
1. Build the real route table first (regex `[Route(...)]` + `[Http*("...")]` over the controllers),
   normalize `{param}`→`*`, then diff the doc-mentioned routes against it. Cheap, and it finds the
   residuals nobody named.
2. Disambiguate by **permission + story + parameters**, not by name. Two same-sounding surfaces are
   usually different stories: e.g. per-cycle goal-setting status (`SetGoal.Team`, takes `cycleId`)
   vs. a progress summary (`Review.Team`, no `cycleId`).
3. In an isolation TC, the corrected route needs a **positive control in the same step** (own-tenant
   ID returns 200) so the cross-tenant 404 cannot be a dead-route 404.
4. If the true target changes the HTTP verb or payload shape (single-resource POST vs. batch PUT),
   that is a **semantic rewrite, not a docs correction** — it can orphan a recorded PASS/FAIL and the
   evidence of a finding filed against that path. Flag it for a decision; do not choose silently.
5. Leave historical `exec_note:` frontmatter verbatim — rewriting it falsifies a run record. Flag it
   instead (see [[project_exec-note-404-may-be-mis-triaged]]).

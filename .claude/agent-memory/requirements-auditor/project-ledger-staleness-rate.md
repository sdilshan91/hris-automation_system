---
name: project-ledger-staleness-rate
description: "Stale-pessimistic" in TEST-FINDINGS.md means two different things with very different rates — 15% fully fixed vs 30% overstated. Do not quote one number.
metadata:
  type: project
---

A 20-finding random sample of `docs/QA/TEST-FINDINGS.md` (audited 2026-09-04, verified against
`src/` not against ledgers) split as: **15% already fixed or obsolete** (3/20 — would be wholly
wasted work if scheduled) but **30% carrying stale-pessimistic content** (6/20 — the extra 3 are
PARTIALLY-FIXED: the root cause landed, only a smaller residual remains).

**Why:** the 2026-09-01 audit's headline "29% of ledger entries are stale-pessimistic" is
ambiguous between these two readings. Scheduling decisions need the first number (waste);
sizing decisions need the second (the row overstates the work). At n=20 the 15% point
estimate has a 95% CI of roughly 3–38%, so a single sample **cannot** confirm or refute 29% —
say so rather than implying the sample settled it.

**Also observed, and the more expensive error:** two findings *understate* their scope.
ENH-009 was filed against one CSV writer; the defect spans 12. ISSUE-117 names one write site;
there are two, and the sanitizer it needs is already DI-registered and used by four sibling
services. Staleness runs in both directions — a tiering pass that only looks for
already-fixed rows will mis-size the ones that grew.

**How to apply:** when asked "what fraction of the backlog is already fixed", give both numbers
with their definitions, and state the CI. Before tiering any finding, re-verify its scope
against `src/` — the filed blast radius is as unreliable as the filed status.
Related: [[reference-gap-tracking-sources]].

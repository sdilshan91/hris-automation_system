---
name: us-prf-005-360-release-exec-2026-09-02
description: TC-PRF-005-04/05/14 executed 2026-09-02 — 0 pass/0 fail/3 blocked; release endpoint is live and 74/74 Feedback360 tests green; found BUG-431 + ISSUE-432
metadata:
  type: project
---

# US-PRF-005 360-release execution, 2026-09-02 (REPORT-ONLY)

Executed the three never-run `draft` TCs bound to the shipped release endpoint (ISSUE-377's remaining work).
**0 PASS / 0 FAIL / 3 BLOCKED. ISSUE-377 is NOT discharged** — but nothing about the feature failed.

**Why blocked, not failed:** the capability is real and green where it can be observed. `74/74` on
`scripts/run-backend-tests.sh src/backend/HRM.sln --filter "FullyQualifiedName~Feedback360"` (incl. 3
Testcontainers-Postgres release-concurrency tests), and the live endpoint 401s unauthenticated. What is
missing is a **live end-to-end run**, which needs fixtures this stack cannot produce — see
[[local-stack-fixture-constraints]].

**How to apply next time:**
- The BR-4 arms already have precise automated homes; cite them rather than re-deriving:
  `Release_BelowPeerThreshold_Returns422_AndWritesNoRow` (TC-04 step 2), `Release_ExactlyAtMinimumPeers_Succeeds`
  (TC-14 boundary), `Feedback360ReleaseApiTests.*` (HTTP layer, real JWT),
  `Feedback360FormTests.SubmitByAssignment_NonOwner_IsDenied_NotAssigned` (TC-05 IDOR arm).
- The live 401 arm of TC-05 (5 routes: release, reviewers-POST, config-GET, assignments-submit, results-GET)
  is cheap and genuinely passes — do it first, it needs no fixture.
- Routes are split across TWO prefixes on one controller: `.../performance/360/...` (cycles/employees paths)
  and `.../performance/feedback-360/...` (assignment form + submit). Guessing the wrong one gives a 404 that
  looks like a missing feature.
- Fixture residue left in `platform` (report-only, not cleaned): employees Liam Carter / Ava Reyes / Ben Cho,
  cycle `01a05f81-ffb9-767c-9adc-96ef041e0f6f`. Reuse them instead of re-seeding.

**Findings filed:** BUG-431 (HIGH — cycle-creation 500 on the exact date format the Angular form sends;
out-of-lane, belongs to US-PRF-004) · ISSUE-432 (MED — `Min360PeerReviewers` has no write path anywhere,
so FR-3's "configurable" minimum is a hardcoded 2).

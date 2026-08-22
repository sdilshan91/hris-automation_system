---
name: us-rec-006-scorecard-findings
description: 2026-06-26 REPORT-ONLY API pass of US-REC-006 interview scorecard (11P/2F/1B + ISO-015 FAIL); routes, perms, testability hack, findings BUG-064/ISSUE-119/ENH-011
metadata:
  type: project
---

# US-REC-006 Interviewer Scorecard — test pass 2026-06-26 (API, REPORT-ONLY)

Verdicts: **11 PASS / 2 FAIL / 1 BLOCKED** (TC-006-01..13) + **ISO-015 FAIL** (BUG-003). Outcome: tested-with-findings (BUG-064, ISSUE-119, ENH-011; BUG-003 note).

**Routes (all `[Authorize]`, `InterviewsController`):**
- `POST /api/v1/recruitment/interviews/{id}/scorecard` — **Recruitment.View** + service enforces BR-1 (assigned interviewer→403 `not_assigned_interviewer`) + BR-4 lock (409 `scorecard_locked`). 201 on submit/edit.
- `GET /api/v1/recruitment/interviews/{id}/scorecards` — Recruitment.View; returns `{scorecards[],aggregateAverageScore,hiddenCount,antiBiasApplies}`.
- `GET /api/v1/recruitment/applicants/{id}/scorecards` — Recruitment.View (recruiter consolidated).
- `GET /api/v1/recruitment/scorecard-criteria` — Recruitment.View (4 defaults: technical_skills/communication/problem_solving/cultural_fit).
- NO `GET /scorecards/{id}` single-card endpoint exists (TC-006-05 step2/ISO-015 step2 reference one).

**Enums:** `OverallRecommendation` StrongNoHire=0,NoHire=1,Hire=2,StrongHire=3 (wire = numeric or string; string "Maybe"→400 bind-fail; 99→400 Enum.IsDefined). `InterviewType` InPerson=0,Video=1,Phone=2 (numeric on wire; string→400). `ApplicantStage` Applied0/Screening1/Interview2/Offer3/Hired4/Rejected5.

**TESTABILITY HACK (key):** scorecard submit needs ONE principal with BOTH an Employee record AND Recruitment.View. NO seeded persona has both (employee@/manager@ have Employee records but NO recruitment perms; TA/HR have View but NO Employee record). To drive happy/multi/anti-bias: created throwaway role "QA-REC006-Interviewer" w/ only Recruitment.View (POST /tenant/roles), ADDITIVELY assigned `[Manager,QArole]` to manager membership (019efa61-e620-7662…, linkedEmployeeId EMP-MGR01 019eff00-0000-7000-8000-000000000001) and `[Employee,QArole]` to employee membership (019efa61-e621-7186…, linkedEmployeeId John EMP-0001 019efced-88a9…), re-logged in for fresh tokens. **Additive keeps Manager/Attendance perms (concurrent attendance chat unaffected); cached tokens stay valid.** Reverted to single roles + hard-deleted role at cleanup (roles DO hard-delete; PUT /tenant/users/{membershipId}/roles REPLACES set). Interviews CAN be cancelled (no hard-delete) but scorecards CANNOT be deleted.

**FINDINGS:**
- **BUG-064 MED BE** — omitted `overallRecommendation` → binds enum default 0=StrongNoHire, passes IsInEnum/IsDefined → 201 stored StrongNoHire (BR-3 mandatory bypassed for OMITTED case only). DTO `SubmitScorecardRequest.OverallRecommendation` non-nullable, no presence check (`InterviewsController.cs:300`).
- **ISSUE-119 MED BE** — submit doesn't require ALL configured criteria; 3-of-4 card accepted (201), average over submitted subset (validator/service only require ≥1 valid key, no completeness check). TC-006-10 expects 400.
- **ENH-011 BE** — BR-4 after-lock arm un-drivable at API (lock=interview.StartsAtUtc+48h, past-dated interviews validation-blocked; no DB access); no GET /scorecards/{id}; version history deferred. Lock logic itself code-correct.

**SOLID (PASS):** average=mean rounded 2dp AwayFromZero; agg=mean of card averages; FR-4 Completed only when ALL assigned submit (single→flips, 2-interviewer stays Scheduled till both); anti-bias server-side hide (empB sees hidden=1/antiBias=true pre-submit, lifts post-submit); BR-1 (TA/HR/unassigned→403, body interviewer-id injection ignored, unauth→401); one-row-per-interviewer (re-submit edits in place); FR-7 audit `recruitment.scorecard.submitted/edited` in audit-logs; FR-5 LogOnlyRecruitmentNotificationService dispatches (log-only seam, not live-wired); TC-006-11 offer-gate satisfied arm (move w/ scorecard→warnings:[]); latency reads 11-23ms / submits 21-29ms (NFR-1 800ms easily met, k6 P95 not run on shared backend).

**ISO-015 / BUG-003:** scorecard surface NOT self-protected — inherits root (US-AUTH-007/TenantResolutionMiddleware). LIVE: acme HR JWT + `X-Tenant-Subdomain: techoneglobal` → GET interviews HTTP 200 re-scoped to techoneglobal (0 rows, but ACCEPTED not rejected). Self-protected arms: acme JWT+other subdomain → own data invisible 404; no/invalid tenant→400/404 fail-closed; body tenant_id structurally ignored. WRITE not exercised (techoneglobal has no interview; would stamp header-resolved tenant by shared root). **Zero cross-tenant rows written; techoneglobal verified 0.**

**RESIDUE (acme):** 5 cancelled test interviews (IID 019f02bc…/019f02be-7b04…/019f02bf-2045…/019f02bf-782d…/019f02c0-a169…) on applicant Jordan Rivera (019f02a5-3aba…, restored to Interview stage) + 5 un-deletable scorecard rows on Jordan + audit/stage-history rows. Reused 1 applicant + 1 vacancy (019f02a4-7a4b…) throughout. Role mutations fully reverted; QA role deleted. Did NOT edit src/, touch attendance, or change shared-persona passwords.
See [[testing-loop-report-only]] [[qa-personas-reseed-2026-06-25]] [[auth-full-test-pass-2026-06-25]].

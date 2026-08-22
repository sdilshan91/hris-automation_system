---
name: us-rec-010-convert-findings
description: 2026-06-26 US-REC-010 convert-applicant-to-employee API pass (2P/3F/9B) — convert 100% broken on Postgres (BUG-068 CRIT), FR-5/9/8 deferred (ISSUE-140), isolation CLEAN
metadata:
  type: project
---

US-REC-010 (LAST recruitment story) REPORT-ONLY API pass 2026-06-26 — **2 PASS / 3 FAIL / 9 BLOCKED**.

**BUG-068 CRIT** — convert-applicant-to-employee is 100% broken on PostgreSQL. `ApplicantConversionService.ConvertAsync:154` opens a user-initiated `BeginTransactionAsync` (guarded only by `Database.IsRelational()`), but DbContext has `EnableRetryOnFailure(3)` → `NpgsqlRetryingExecutionStrategy` (DependencyInjection.cs:40). EF forbids manual BeginTransaction under a retrying strategy → `InvalidOperationException` on the FIRST query inside (EmployeeService.CreateAsync:68 email-uniqueness SingleAsync) → HTTP 500 every time. **Green-in-tests/broken-in-prod**: InMemory tests skip the tx via IsRelational guard. Fix = wrap body in `CreateExecutionStrategy().ExecuteAsync(...)`, NOT remove retry. Resolves the open BUG-059 (Hired→no auto-convert) note: seam exists but non-functional. Rollback happens to be clean (throw precedes all writes: 0 orphan employee, applicant unconverted, filled_count unchanged).

**ISSUE-140 MED** — FR-5/BR-7 auto-create user account (no tenant setting; `UserAccountCreated` hardcoded false), FR-9 welcome email (log-only, no Hangfire/NFR-5), FR-8 onboarding trigger (log-only) all Phase-1 deferred stubs (PostConversionNotificationsSafeAsync:296 logs "DEFERRED"). TC-010-04 would pass vacuously; TC-010-03 unsatisfiable (no enable switch).

**Real routes** (controller `ApplicantConversionController`, route `api/v1/recruitment`, requires BOTH `Recruitment.Manage` AND `Employee.Create` via two RequirePermission attrs = AND): `GET applicants/{id}/conversion-prefill`, `POST applicants/{id}/convert`. Body: jobTitleId, departmentId, employmentType, dateOfJoining, reportsToEmployeeId?, locationId?, employeeNo?, dateOfBirth?, gender? (name/email/phone/salary mapped server-side from application+offer).

**What WORKS (PASS/verified pre-transaction):** prefill mapping fidelity (TC-010-02 read arm — all fields correct); eligibility rejects TC-010-07 (non-Hired→409 applicant_not_hired, Hired+offerSent→409 no_accepted_offer, Hired+noOffer→409 no_accepted_offer); authz TC-010-12 (manager 403, employee 403 on both convert+prefill; tenantadmin passes gate→500); structural validation TC-010-11 (missing dept/jobtitle→400, employeeNo>50→400). **ISO-019 PASS**: acme-JWT + spoofed `X-Tenant-Subdomain: qa04-matrix-1` → prefill AND convert both 404 (applicant not loadable cross-tenant; loaded via ITenantContext not body id). NOT BUG-003 — conversion is read-self-resolve protected (like CHR/ATT clock-out). No leak, no cross-tenant write, 0 rows in target tenant verified.

**BLOCKED (all need a successful conversion, gated by BUG-068):** 010-03/04/05/06/08/09/10/13. 010-01/02/11 FAIL.

**Seed recipe for a Hired+Accepted applicant** (no DELETE API exists for vacancy/applicant/offer): create vacancy (POST /recruitment/vacancies, headcount N) → publish → apply via `POST /api/v1/careers/vacancies/{vac}/apply` (multipart resume + firstName/lastName/email/phone; INTERNAL endpoint needs linkedEmployeeId so use careers/public) → `POST /recruitment/offers` → `/offers/{id}/send` → `/offers/{id}/respond {accepted:true}` (auto-advances to Hired). move-stage allows direct forward jumps (scorecard gate stubbed).

**Residue in acme (no hard-delete API):** vacancy "REC010 Convert QA Vacancy" (019f030d-c726…, VAC-2026-0028, Open headcount 3 filled 0); applicants maya.f.rec010@/nonhired.rec010@/sentoffer.rec010@example.com; 2 offers (OFR-2026-0023 Accepted for Maya, one Sent). **0 employees created** (all converts 500'd). NO CoreHR mutation, NO seeded-employee mutation (John Doe only referenced as manager FK), NO shared-persona pw change, NO cross-tenant write. See [[testing-loop-report-only]] [[qa-personas-reseed-2026-06-25]]

---
module: Recruitment
total_user_stories: 6
total_test_cases: 96
created: 2026-06-15
updated: 2026-06-15
status: in-progress
---

# Recruitment -- Test Matrix

> US-REC-001 (Create and Publish Job Vacancy) established `test-cases/recruitment/` -- 16 test cases (12 functional/security/perf/a11y + 4 dedicated multi-tenant isolation). US-REC-002 (Applicant Submits Application with Resume Upload) adds 21 test cases (13 functional/security/perf/a11y: TC-REC-002-01..13 + 4 dedicated multi-tenant isolation on the new `applicant` table: TC-REC-ISO-005..008). US-REC-003 (Recruiter Views Applicant Pipeline with Stage Management) adds 18 test cases (14 functional/security/perf/a11y: TC-REC-003-01..14 + 4 dedicated multi-tenant isolation on the pipeline/stage-move/stage-history operations: TC-REC-ISO-009..012). US-REC-004 (Move Applicant Through Pipeline Stages with Gates) adds 13 test cases (12 functional/integration/perf: TC-REC-004-01..12 + 1 new multi-tenant isolation on the stage-history/transition/rejection trail: TC-REC-ISO-013; the generic single-move read/context/write isolation is reused from TC-REC-ISO-009..011). US-REC-005 (Schedule Interviews and Notify Participants) adds 14 test cases (13 functional/integration/security/perf/a11y: TC-REC-005-01..13 + 1 new multi-tenant isolation on the new `interview`/`interview_interviewer` tables + tenant-aware reminder jobs: TC-REC-ISO-014; the generic no/invalid tenant-context rejection + cross-tenant write block are reused from TC-REC-ISO-010/011). US-REC-006 (Interviewer Submits Structured Interview Scorecard) adds 14 test cases (13 functional/integration/security/perf/a11y: TC-REC-006-01..13 + 1 new multi-tenant isolation on the new `interview_scorecard`/`scorecard_criterion_rating` tables: TC-REC-ISO-015; the generic no/invalid tenant-context rejection + cross-tenant write block are reused from TC-REC-ISO-010/011). Module total: 96 test cases, 29/29 acceptance criteria covered (US-REC-006 has 4 ACs).

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 6 (US-REC-001, US-REC-002, US-REC-003, US-REC-004, US-REC-005, US-REC-006) |
| Total Test Cases | 96 (81 functional/integration/security/perf/a11y + 15 dedicated multi-tenant isolation) |
| US-REC-006 Test Cases | 14 (TC-REC-006-01..13 + TC-REC-ISO-015; reuses TC-REC-ISO-010/011) |
| Critical Priority (REC-006) | 4 (TC-REC-006-01, TC-REC-006-05, TC-REC-006-06, TC-REC-ISO-015) |
| High Priority (REC-006) | 10 (TC-REC-006-02*, -03, -04, -07, -08, -09, -10, -11, -12, -13) (*TC-REC-006-02 is critical for the completion gate) |
| US-REC-005 Test Cases | 14 (TC-REC-005-01..13 + TC-REC-ISO-014; reuses TC-REC-ISO-010/011) |
| Critical Priority (REC-005) | 3 (TC-REC-005-01, TC-REC-005-02, TC-REC-005-11, TC-REC-ISO-014) |
| High Priority (REC-005) | 9 (TC-REC-005-03, -04, -05, -06, -07, -08, -09, -10, -12, -13) |
| Medium Priority (REC-005) | 0 |
| US-REC-004 Test Cases | 13 (TC-REC-004-01..12 + TC-REC-ISO-013; reuses TC-REC-ISO-009..011) |
| Critical Priority (REC-004) | 3 (TC-REC-004-01, TC-REC-004-02, TC-REC-ISO-013) |
| High Priority (REC-004) | 8 (TC-REC-004-03, -05, -06, -07, -08, -09, -11, -12) |
| Medium Priority (REC-004) | 1 (TC-REC-004-10) |
| US-REC-002 Test Cases | 21 (TC-REC-002-01..13 + TC-REC-ISO-005..008) |
| Critical Priority (REC-002) | 6 (TC-REC-002-01, -04, -09, -10, TC-REC-ISO-005, -006, -007) |
| High Priority (REC-002) | 7 |
| Medium Priority (REC-002) | 1 (TC-REC-002-08) |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Conditional/Deferred Test Cases | EF-query-filter-vs-PostgreSQL-RLS: US-REC-002 AC-5/NFR-3 specify PostgreSQL RLS on applicant data; this platform enforces isolation via EF Core global query filters + TenantInterceptor. TC-REC-ISO-005/007 describe the EF mechanism and note RLS session-level assertion as an extension point if added on `applicant`. Virus scanning + EXIF strip (FR-3/FR-4/NFR-4) depend on the File & Document Management module (S26.3); TC-REC-002-10 asserts the scan-before-persist ordering and image EXIF stripping, marking the image-EXIF portion conditional on image attachments being accepted. Confirmation email (FR-5) depends on the Notification System (S25) + tenant "Application Received" template; verified in TC-REC-002-01. In-app/email recruiter notification (FR-7) depends on the Notification System -- NOT separately asserted here (owned by a later Recruitment/Notifications story). CAPTCHA/rate-limit (S10 constraint, TC-REC-002-11) verified as configured; the exact mechanism (CAPTCHA vs rate limit) is an implementation choice. Internal pre-fill (FR-8/BR-5, TC-REC-002-02) depends on the Core HR module exposing employee profile data. |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-REC-001 | Create and Publish Job Vacancy | TC-REC-001-01 .. TC-REC-001-12 | 12 |
| Cross-cutting (REC-001) | Multi-tenant isolation (vacancy) | TC-REC-ISO-001, TC-REC-ISO-002, TC-REC-ISO-003, TC-REC-ISO-004 | 4 |
| US-REC-002 | Applicant Submits Application with Resume Upload | TC-REC-002-01, TC-REC-002-02, TC-REC-002-03, TC-REC-002-04, TC-REC-002-05, TC-REC-002-06, TC-REC-002-07, TC-REC-002-08, TC-REC-002-09, TC-REC-002-10, TC-REC-002-11, TC-REC-002-12, TC-REC-002-13 | 13 |
| Cross-cutting (REC-002) | Multi-tenant isolation (applicant) | TC-REC-ISO-005, TC-REC-ISO-006, TC-REC-ISO-007, TC-REC-ISO-008 | 4 |
| US-REC-003 | Recruiter Views Applicant Pipeline with Stage Management | TC-REC-003-01, TC-REC-003-02, TC-REC-003-03, TC-REC-003-04, TC-REC-003-05, TC-REC-003-06, TC-REC-003-07, TC-REC-003-08, TC-REC-003-09, TC-REC-003-10, TC-REC-003-11, TC-REC-003-12, TC-REC-003-13, TC-REC-003-14 | 14 |
| Cross-cutting (REC-003) | Multi-tenant isolation (pipeline / stage move / stage history) | TC-REC-ISO-009, TC-REC-ISO-010, TC-REC-ISO-011, TC-REC-ISO-012 | 4 |
| US-REC-004 | Move Applicant Through Pipeline Stages with Gates | TC-REC-004-01, TC-REC-004-02, TC-REC-004-03, TC-REC-004-04, TC-REC-004-05, TC-REC-004-06, TC-REC-004-07, TC-REC-004-08, TC-REC-004-09, TC-REC-004-10, TC-REC-004-11, TC-REC-004-12 | 12 |
| Cross-cutting (REC-004) | Multi-tenant isolation (stage-history / transition / rejection trail) | TC-REC-ISO-013 (+ reuses TC-REC-ISO-009, TC-REC-ISO-010, TC-REC-ISO-011) | 1 |
| US-REC-005 | Schedule Interviews and Notify Participants | TC-REC-005-01, TC-REC-005-02, TC-REC-005-03, TC-REC-005-04, TC-REC-005-05, TC-REC-005-06, TC-REC-005-07, TC-REC-005-08, TC-REC-005-09, TC-REC-005-10, TC-REC-005-11, TC-REC-005-12, TC-REC-005-13 | 13 |
| Cross-cutting (REC-005) | Multi-tenant isolation (interview / interviewer / reminder job) | TC-REC-ISO-014 (+ reuses TC-REC-ISO-010, TC-REC-ISO-011) | 1 |
| US-REC-006 | Interviewer Submits Structured Interview Scorecard | TC-REC-006-01, TC-REC-006-02, TC-REC-006-03, TC-REC-006-04, TC-REC-006-05, TC-REC-006-06, TC-REC-006-07, TC-REC-006-08, TC-REC-006-09, TC-REC-006-10, TC-REC-006-11, TC-REC-006-12, TC-REC-006-13 | 13 |
| Cross-cutting (REC-006) | Multi-tenant isolation (interview scorecard / criterion rating) | TC-REC-ISO-015 (+ reuses TC-REC-ISO-010, TC-REC-ISO-011) | 1 |

## Test Type Distribution (US-REC-002)

| Type | Test Cases | Count |
|------|------------|-------|
| Functional / E2E (REC-002) | TC-REC-002-01, TC-REC-002-02, TC-REC-002-03, TC-REC-002-05, TC-REC-002-06, TC-REC-002-07, TC-REC-002-08 | 7 |
| Security (REC-002) | TC-REC-002-04 (MIME), TC-REC-002-09 (filename/path-traversal), TC-REC-002-10 (virus scan/EXIF), TC-REC-002-11 (anonymous + rate-limit/CAPTCHA + XSS), TC-REC-ISO-005, TC-REC-ISO-006, TC-REC-ISO-007, TC-REC-ISO-008 | 8 |
| Performance (REC-002) | TC-REC-002-12 (upload <=5s @ 25MB; careers/form load <=2.5s P95 on 4G) | 1 |
| Accessibility / Cross-browser (REC-002) | TC-REC-002-13 (public form WCAG 2.1 AA + responsive 360px) | 1 |

(Note: TC-REC-002-03 carries both Negative and Boundary tags. TC-REC-002-04 is functionally a negative file-type case but is typed Security as it defends against disguised executables. TC-REC-002-08 is typed Functional while carrying Boundary/Negative tags.)

## Acceptance Criteria Coverage (US-REC-002)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Public submit + resume -> status `Applied`, resume at tenant-scoped path, confirmation email | TC-REC-002-01, TC-REC-002-08, TC-REC-002-09, TC-REC-002-11, TC-REC-002-12, TC-REC-002-13 |
| AC-2 | Oversized (>25MB) or disallowed-MIME file rejected, not persisted | TC-REC-002-03, TC-REC-002-04, TC-REC-002-08, TC-REC-002-10 |
| AC-3 | Duplicate application (same email, same vacancy) prevented | TC-REC-002-06, TC-REC-002-07 |
| AC-4 | Internal employee apply -> profile pre-filled + linked to employee record | TC-REC-002-02 |
| AC-5 | Tenant B sees zero of Tenant A's applicants; isolation enforced | TC-REC-ISO-005, TC-REC-ISO-006, TC-REC-ISO-007, TC-REC-ISO-008 |

## Functional Requirement Coverage (US-REC-002)

| FR | Covered By |
|----|------------|
| FR-1 (form fields: name/email/phone/cover letter max 2000/resume max 25MB PDF-DOCX-DOC) | TC-REC-002-01, TC-REC-002-03, TC-REC-002-04, TC-REC-002-08 |
| FR-2 (resume stored at tenant-scoped path `{tenantId}/recruitment/{vacancyId}/{applicantId}/{filename}`) | TC-REC-002-01, TC-REC-002-09, TC-REC-ISO-007, TC-REC-ISO-008 |
| FR-3 (virus scan before persisting storage URL) | TC-REC-002-10 |
| FR-4 (strip EXIF from uploaded images) | TC-REC-002-10 |
| FR-5 (confirmation email via tenant "Application Received" template) | TC-REC-002-01 |
| FR-6 (applicant record created at stage `Applied`) | TC-REC-002-01, TC-REC-002-02, TC-REC-002-05, TC-REC-002-07 |
| FR-7 (notify Recruitment.Read.All users of new application) | Deferred to a later Recruitment/Notifications story (depends on Notification System S25) |
| FR-8 (internal application: pre-fill + link to employee record) | TC-REC-002-02 |

## Non-Functional Requirement Coverage (US-REC-002)

| NFR | Covered By |
|-----|------------|
| NFR-1 (resume upload <= 5s for 25MB) | TC-REC-002-12 (also 25MB accept boundary in TC-REC-002-08) |
| NFR-2 (public form needs no auth + WCAG 2.1 AA) | TC-REC-002-11 (anonymous), TC-REC-002-13 (WCAG) |
| NFR-3 (applicant data tenant-scoped, RLS-protected) | TC-REC-ISO-005, TC-REC-ISO-006, TC-REC-ISO-007, TC-REC-ISO-008 (EF query filters today; RLS extension point) |
| NFR-4 (files scanned for malware before storage URL persisted) | TC-REC-002-10 |
| NFR-5 (mobile-responsive, 360px minimum) | TC-REC-002-13 |
| NFR-6 (careers page + form load <= 2.5s P95 on 4G) | TC-REC-002-12 |

## Business Rule Coverage (US-REC-002)

| BR | Covered By |
|----|------------|
| BR-1 (unique per vacancy by email; duplicate rejected) | TC-REC-002-06, TC-REC-002-07, TC-REC-ISO-008 |
| BR-2 (same email may apply to different vacancies) | TC-REC-002-07 |
| BR-3 (filenames sanitized + renamed to UUID; path-traversal prevented) | TC-REC-002-09, TC-REC-ISO-007 |
| BR-4 (only allowed MIME types: pdf/docx/doc) | TC-REC-002-04 |
| BR-5 (internal applicants flagged `internal`) | TC-REC-002-02 |
| BR-6 (apply only to `Open` vacancies, before deadline) | TC-REC-002-05 |

## Test Type Distribution (US-REC-003)

| Type | Test Cases | Count |
|------|------------|-------|
| Functional / E2E (REC-003) | TC-REC-003-01, TC-REC-003-02, TC-REC-003-03, TC-REC-003-04, TC-REC-003-05, TC-REC-003-07, TC-REC-003-08, TC-REC-003-09, TC-REC-003-10, TC-REC-003-11 | 10 |
| Security (REC-003) | TC-REC-003-06 (authz Read/Manage), TC-REC-003-14 (XSS/SQLi/tamper), TC-REC-ISO-009, TC-REC-ISO-010, TC-REC-ISO-011, TC-REC-ISO-012 | 6 |
| Performance (REC-003) | TC-REC-003-12 (board <=400ms P95 @ 200 applicants; stage move <=800ms P95) | 1 |
| Accessibility / Cross-browser (REC-003) | TC-REC-003-13 (Kanban WCAG 2.1 AA + keyboard drag alternative + responsive 360px) | 1 |

(Note: TC-REC-003-04 carries Happy+Boundary tags; TC-REC-003-06/08/14 are typed Security while also carrying Negative tags; TC-REC-003-07/09/10 carry both Happy and Negative tags; TC-REC-003-12 carries Boundary+Performance.)

## Acceptance Criteria Coverage (US-REC-003)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Kanban board: column per stage with cards (name/date/source), per-column counts + total | TC-REC-003-01, TC-REC-003-05, TC-REC-003-11, TC-REC-003-12, TC-REC-003-13 |
| AC-2 | Drag-and-drop stage move persists + audit/history; optimistic UI | TC-REC-003-02, TC-REC-003-06, TC-REC-003-07, TC-REC-003-08, TC-REC-003-09, TC-REC-003-10, TC-REC-003-12, TC-REC-003-13, TC-REC-003-14 |
| AC-3 | Detail slide-over: profile + resume preview + stage timeline + interviews/notes | TC-REC-003-03 |
| AC-4 | Filter by stage/source/date range/search; counts update; clear restores | TC-REC-003-04, TC-REC-003-14 |
| AC-5 | Tenant B sees zero of Tenant A's pipeline; isolation enforced | TC-REC-ISO-009, TC-REC-ISO-010, TC-REC-ISO-011, TC-REC-ISO-012 |

## Functional Requirement Coverage (US-REC-003)

| FR | Covered By |
|----|------------|
| FR-1 (Kanban one column per stage, ordered by sequence) | TC-REC-003-01, TC-REC-003-11 |
| FR-2 (card: name, applied date, source badge, new/unread indicator) | TC-REC-003-01, TC-REC-003-11 |
| FR-3 (drag-and-drop move w/ optimistic UI + server persistence) | TC-REC-003-02, TC-REC-003-07, TC-REC-003-08, TC-REC-003-12, TC-REC-003-14 |
| FR-4 (table/list view toggle with sortable columns) | TC-REC-003-05 |
| FR-5 (per-stage counts + total) | TC-REC-003-01, TC-REC-003-04, TC-REC-003-11 |
| FR-6 (filter: stage/source/date range/search name+email) | TC-REC-003-04, TC-REC-003-14 |
| FR-7 (detail panel: profile/resume/timeline/interviews/notes/actions) | TC-REC-003-03, TC-REC-003-14 (notes sanitization) |
| FR-8 (bulk: select multiple + move to stage) | TC-REC-003-10, TC-REC-ISO-011 (CONDITIONAL if bulk deferred; single move TC-REC-003-02 covers per-applicant persistence) |

## Non-Functional Requirement Coverage (US-REC-003)

| NFR | Covered By |
|-----|------------|
| NFR-1 (board load <= 400ms P95 @ 200 applicants) | TC-REC-003-12 (Redis board cache CONDITIONAL; DB path measured) |
| NFR-2 (stage transition persists <= 800ms P95 + optimistic UI) | TC-REC-003-12 |
| NFR-3 (applicant queries tenant-scoped; RLS defense-in-depth) | TC-REC-ISO-009, TC-REC-ISO-010, TC-REC-ISO-011, TC-REC-ISO-012 (EF query filters today; RLS extension point) |
| NFR-4 (responsive; mobile horizontal scroll or stacked + stage tabs) | TC-REC-003-13 |
| NFR-5 (inline PDF via pdf.js; no raw blob URL exposed) | TC-REC-003-03, TC-REC-ISO-012 |

## Business Rule Coverage (US-REC-003)

| BR | Covered By |
|----|------------|
| BR-1 (view requires Recruitment.Read.All) | TC-REC-003-06 |
| BR-2 (move/bulk require Recruitment.Manage.All) | TC-REC-003-06, TC-REC-003-08, TC-REC-003-10 |
| BR-3 (move to Rejected requires a reason) | TC-REC-003-07, TC-REC-003-10 |
| BR-4 (backward move requires Manage + reason; forward-only default) | TC-REC-003-08 |
| BR-5 (each transition recorded: timestamp/user/from/to/notes) | TC-REC-003-02, TC-REC-003-07, TC-REC-003-08, TC-REC-003-10, TC-REC-ISO-011 |
| BR-6 (Hired terminal -> triggers convert-to-employee) | TC-REC-003-09 |

## Conditional / Deferred (US-REC-003)

- **Redis board cache (NFR-1):** key shape `tenant:{tenantId}:vacancy:{vacancyId}:pipeline`. If deferred, TC-REC-003-12 measures the DB-backed path and TC-REC-ISO-012 asserts the cache-KEY contract as a design check (no Redis required to pass). CONDITIONAL, not a gap.
- **Bulk stage move (FR-8):** TC-REC-003-10 is BLOCKED-if-deferred (do not weaken); single-applicant move (TC-REC-003-02) covers per-applicant persistence regardless.
- **Audit module entry (AC-2):** in-module `applicant_stage_history` rows asserted directly; the cross-cutting Audit module entry is asserted where that module is integrated.
- **Convert-to-employee workflow (BR-6):** owned by US-REC-010; TC-REC-003-09 asserts the terminal behaviour + trigger seam only.
- **EF query filters vs PostgreSQL RLS (AC-5/NFR-3):** US-REC-003 specifies RLS as defense-in-depth; the platform enforces isolation via EF Core global query filters + TenantInterceptor. ISO TCs describe the EF mechanism and note RLS session-level assertion as an extension point if added on `applicant`/`applicant_stage_history`.
- **Real-time board updates (S10/S35):** SignalR live updates are a "Should Have"; initial coverage assumes manual refresh.

## Test Type Distribution (US-REC-004)

| Type | Test Cases | Count |
|------|------------|-------|
| Functional / E2E (REC-004) | TC-REC-004-01, TC-REC-004-02, TC-REC-004-03, TC-REC-004-04, TC-REC-004-05, TC-REC-004-06, TC-REC-004-07, TC-REC-004-08, TC-REC-004-09 | 9 |
| Integration (REC-004) | TC-REC-004-10 (async notification outbox/Hangfire) | 1 |
| Security (REC-004) | TC-REC-004-05 (backward authz), TC-REC-ISO-013 (+ reused TC-REC-ISO-009/010/011) | 1 dedicated ISO |
| Performance (REC-004) | TC-REC-004-12 (transition <=800ms P95 incl. audit + atomicity) | 1 |
| Accessibility / Cross-browser (REC-004) | (covered for the pipeline UI by TC-REC-003-13; REC-004 reuses it) | 0 new |

(Note: TC-REC-004-02/03/05/06/07/08/09/11 carry multiple tags -- Happy + Negative and/or Boundary; TC-REC-004-05 is typed Functional while also carrying Security/Negative tags for the Manage-only backward authz; TC-REC-004-12 carries Boundary + Performance.)

## Acceptance Criteria Coverage (US-REC-004)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Applied -> Screening updates stage + history record + notification | TC-REC-004-01, TC-REC-004-10 |
| AC-2 | Screening -> Interview validates screening, prompts interview schedule | TC-REC-004-01, TC-REC-004-04 |
| AC-3 | Interview -> Offer validates >=1 scorecard, triggers offer workflow | TC-REC-004-01, TC-REC-004-04 |
| AC-4 | Reject from any active stage: required reason dropdown + optional notes + rejection email | TC-REC-004-02, TC-REC-004-03, TC-REC-004-10 |
| AC-5 | Transitions recorded with tenant_id etc.; no cross-tenant audit entries | TC-REC-ISO-013 (+ reused TC-REC-ISO-009/010/011) |

## Functional Requirement Coverage (US-REC-004)

| FR | Covered By |
|----|------------|
| FR-1 (gate criteria per stage; soft gates) | TC-REC-004-04 (CONDITIONAL on US-REC-005/006), TC-REC-004-09 |
| FR-2 (Applied->Screening->Interview->Offer->Hired; skip if permitted) | TC-REC-004-01, TC-REC-004-07 |
| FR-3 (reject from any active stage; reason + optional notes) | TC-REC-004-02, TC-REC-004-03 |
| FR-4 (record every transition in applicant_stage_history with full fields) | TC-REC-004-01, TC-REC-004-12, TC-REC-ISO-013 |
| FR-5 (backward move only for Manage; mandatory reason) | TC-REC-004-05, TC-REC-004-06 |
| FR-6 (configurable email per transition) | TC-REC-004-10 (CONDITIONAL on Notification System S25) |
| FR-7 (real-time Kanban count update; optimistic UI) | Covered for the board by TC-REC-003-02/12/13; REC-004 reuses |
| FR-8 (prevent advancement if vacancy Closed/Cancelled) | TC-REC-004-08 |

## Non-Functional Requirement Coverage (US-REC-004)

| NFR | Covered By |
|-----|------------|
| NFR-1 (transition <= 800ms P95 incl. audit) | TC-REC-004-12 |
| NFR-2 (transition data tenant-scoped; RLS) | TC-REC-ISO-013 (EF query filters today; RLS extension point) |
| NFR-3 (transition + audit writes atomic, single transaction) | TC-REC-004-12, TC-REC-004-11 |
| NFR-4 (optimistic UI visual feedback) | Covered for the board by TC-REC-003-12/13; REC-004 reuses |
| NFR-5 (emails queued via Hangfire, non-blocking) | TC-REC-004-10 (CONDITIONAL on S25/Hangfire wiring) |

## Business Rule Coverage (US-REC-004)

| BR | Covered By |
|----|------------|
| BR-1 (gate criteria configurable per tenant per stage; defaults) | TC-REC-004-04 |
| BR-2 (rejected applicant cannot advance until Manage reactivation) | TC-REC-004-03, TC-REC-004-06 |
| BR-3 (Hired terminal + irreversible -> convert-to-employee) | TC-REC-004-07 (full workflow owned by US-REC-010) |
| BR-4 (headcount-filled warning before Offer/Hired at capacity) | TC-REC-004-09 |
| BR-5 (transition emails use tenant templates + variable substitution) | TC-REC-004-10 |
| BR-6 (bulk transitions apply gates per applicant; per-applicant failure report) | Covered by TC-REC-003-10 (bulk move); REC-004 gate-per-applicant CONDITIONAL on bulk being delivered |

## Conditional / Deferred (US-REC-004)

- **Soft gates depend on US-REC-005/006 (FR-1/BR-1):** the Interview-schedule and Offer-scorecard gate data sources are US-REC-005 and US-REC-006. TC-REC-004-04 asserts the SOFT-gate contract (warn + pass/fail list + overridable by Manage); if those stories are not yet delivered, the gate evaluator is STUBBED and the case is CONDITIONAL -- it must NOT be weakened to "no gate". NOT a gap.
- **Async notifications depend on Notification System S25 + Hangfire (FR-6/NFR-5/BR-5):** TC-REC-004-10 asserts the queued outbox entry + non-blocking + template substitution; if delivery is a LOG-ONLY stub, assert the outbox/log record. CONDITIONAL, not a gap.
- **Optimistic concurrency token (story assumption #10.3):** TC-REC-004-11 asserts last-writer-conflict (409). If the EF concurrency token is not yet wired, the case is BLOCKED (report to caller) -- do NOT weaken to last-write-wins.
- **Convert-to-employee (BR-3):** owned by US-REC-010; TC-REC-004-07 asserts terminal/irreversible + the trigger seam only.
- **Headcount-filled warning (BR-4):** TC-REC-004-09 evaluates against Hired-applicant counts available in this increment; CONDITIONAL on full conversion/headcount wiring (US-REC-010).
- **EF query filters vs PostgreSQL RLS (AC-5/NFR-2):** US-REC-004 specifies RLS on `applicant_stage_history`; the platform enforces isolation via EF Core global query filters + TenantInterceptor. TC-REC-ISO-013 describes the EF mechanism and notes RLS session-level assertion as an extension point if added.
- **ISO reuse:** TC-REC-ISO-009 (cross-tenant read), TC-REC-ISO-010 (no/invalid/mismatched tenant context), TC-REC-ISO-011 (cross-tenant write block + body-injected tenant_id) operate on the same applicant/stage-history tables and are reused for REC-004; TC-REC-ISO-013 adds the rejection-reason + multi-transition-trail dimension specific to US-REC-004.

## Test Type Distribution (US-REC-005)

| Type | Test Cases | Count |
|------|------------|-------|
| Functional / E2E (REC-005) | TC-REC-005-01, TC-REC-005-05, TC-REC-005-06, TC-REC-005-07, TC-REC-005-08, TC-REC-005-09, TC-REC-005-10 | 7 |
| Integration (REC-005) | TC-REC-005-02 (reminder job fire), TC-REC-005-03 (reschedule job swap), TC-REC-005-04 (cancel job removal) | 3 |
| Security (REC-005) | TC-REC-005-09 (interviewer eligibility), TC-REC-005-11 (authz + XSS notes), TC-REC-ISO-014 (+ reused TC-REC-ISO-010/011) | 2 dedicated + 1 ISO |
| Performance (REC-005) | TC-REC-005-12 (schedule <=800ms P95 incl. outbox; async delivery) | 1 |
| Accessibility / Cross-browser (REC-005) | TC-REC-005-13 (form + calendar/agenda WCAG 2.1 AA + responsive 360px) | 1 |

(Note: TC-REC-005-04/06/07/08/09/10/11 carry multiple tags -- Happy + Negative and/or Boundary; TC-REC-005-09 is typed Security while also carrying Negative; TC-REC-005-12 carries Boundary + Performance; TC-REC-005-13 carries Accessibility + Cross-browser.)

## Acceptance Criteria Coverage (US-REC-005)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Schedule interview (interviewers + date/time + type + location/link) -> saved, participants notified, reminder job scheduled | TC-REC-005-01, TC-REC-005-06, TC-REC-005-07, TC-REC-005-08, TC-REC-005-09, TC-REC-005-11, TC-REC-005-12, TC-REC-005-13 |
| AC-2 | Hangfire fires a reminder ~24h before to all participants | TC-REC-005-02 |
| AC-3 | Edit/cancel -> updated/cancellation notifications + reminder rescheduled/removed | TC-REC-005-03, TC-REC-005-04, TC-REC-005-11 |
| AC-4 | Multiple rounds tracked independently (own interviewers/schedule/scorecards) | TC-REC-005-05 |
| AC-5 | Tenant B sees zero of Tenant A's interviews; isolation enforced | TC-REC-ISO-014 (+ reused TC-REC-ISO-010, TC-REC-ISO-011) |

## Functional Requirement Coverage (US-REC-005)

| FR | Covered By |
|----|------------|
| FR-1 (scheduling form: interviewers >=1, date/start/duration, type, location for in-person, link for video, notes) | TC-REC-005-01, TC-REC-005-08, TC-REC-005-09 |
| FR-2 (multiple rounds per applicant/vacancy, separate record + round number) | TC-REC-005-05 |
| FR-3 (email/in-app notifications on create/update/cancel via tenant templates) | TC-REC-005-01, TC-REC-005-03, TC-REC-005-04 (CONDITIONAL on Notification System S25) |
| FR-4 (Hangfire reminder job 24h before, tenant-configurable) | TC-REC-005-01, TC-REC-005-02, TC-REC-005-03 |
| FR-5 (calendar view filterable by interviewer/vacancy/date/status) | TC-REC-005-10 |
| FR-6 (status: Scheduled/Completed/Cancelled/No-Show) | TC-REC-005-04, TC-REC-005-10 |
| FR-7 (conflict detection -> warn + override, soft) | TC-REC-005-06 |
| FR-8 (attach interview guide / evaluation criteria doc) | CONDITIONAL -- depends on File & Document Management (S26); attachment seam, not separately asserted here |

## Non-Functional Requirement Coverage (US-REC-005)

| NFR | Covered By |
|-----|------------|
| NFR-1 (scheduling API <= 800ms P95 incl. outbox writes) | TC-REC-005-12 |
| NFR-2 (interview data tenant-scoped + RLS) | TC-REC-ISO-014 (EF query filters today; RLS extension point) |
| NFR-3 (notifications async via Hangfire, non-blocking) | TC-REC-005-01, TC-REC-005-02, TC-REC-005-12 |
| NFR-4 (reminder jobs idempotent + tenant-aware) | TC-REC-005-02, TC-REC-ISO-014 |
| NFR-5 (calendar responsive, mobile 360px+ agenda) | TC-REC-005-13 |
| NFR-6 (validate future + business hours) | TC-REC-005-07, TC-REC-005-08 |

## Business Rule Coverage (US-REC-005)

| BR | Covered By |
|----|------------|
| BR-1 (only Recruitment.Manage.All can schedule/edit/cancel) | TC-REC-005-11 |
| BR-2 (interviewers must be active same-tenant employees) | TC-REC-005-09 |
| BR-3 (no past-date scheduling) | TC-REC-005-07 |
| BR-4 (cancel does NOT change pipeline stage) | TC-REC-005-04 |
| BR-5 (reminder lead time configurable per tenant; default 24h) | TC-REC-005-02 |
| BR-6 (reschedule -> old reminder cancelled, new created) | TC-REC-005-03 |
| BR-7 (notify applicant email + each interviewer work email) | TC-REC-005-01, TC-REC-005-02, TC-REC-005-03, TC-REC-005-04 |

## Conditional / Deferred (US-REC-005)

- **Async notifications depend on Notification System S25 + tenant templates (FR-3/NFR-3/BR-5/BR-7):** TC-REC-005-01..04 assert the queued outbox/in-app entries + non-blocking dispatch + recipient set; if delivery is a LOG-ONLY stub, assert the outbox/log record. CONDITIONAL, not a gap.
- **Hangfire reminder timing (AC-2/FR-4/NFR-4):** TC-REC-005-02 simulates/advances the clock or triggers the enqueued job; idempotency + tenant-aware execution asserted. If Hangfire is stubbed, assert the enqueue contract (fire time = start - lead time, tenant_id in params).
- **Business-hours validation (NFR-6):** TC-REC-005-07 asserts past-date rejection (hard) + business-hours rejection where enabled; if business-hours validation is soft/disabled in this increment, the business-hours portion is CONDITIONAL (past-date rejection is NOT). NOT a gap.
- **Interview guide attachment (FR-8):** depends on File & Document Management (S26); the attachment seam is noted, not separately asserted (owned where that module integrates).
- **Scorecard association (AC-4):** each round can carry its own scorecard(s) -- owned by US-REC-006; TC-REC-005-05 asserts the per-round association seam only.
- **Calendar/agenda a11y (NFR-5):** TC-REC-005-13 covers the new scheduling form + calendar/agenda directly (not reused from REC-003, which is the Kanban pipeline board).
- **EF query filters vs PostgreSQL RLS (AC-5/NFR-2):** US-REC-005 specifies RLS on `interview`; the platform enforces isolation via EF Core global query filters + TenantInterceptor. TC-REC-ISO-014 describes the EF mechanism and notes RLS session-level assertion as an extension point if added.
- **ISO reuse:** TC-REC-ISO-010 (no/invalid/mismatched tenant context) and TC-REC-ISO-011 (cross-tenant write block + body-injected tenant_id) are reused for the recruitment surface; TC-REC-ISO-014 adds the interview/interviewer/reminder-job dimension specific to US-REC-005.

## Test Type Distribution (US-REC-006)

| Type | Test Cases | Count |
|------|------------|-------|
| Functional / E2E (REC-006) | TC-REC-006-01, TC-REC-006-02, TC-REC-006-03, TC-REC-006-04, TC-REC-006-07, TC-REC-006-08, TC-REC-006-09, TC-REC-006-10 | 8 |
| Integration (REC-006) | TC-REC-006-11 (Offer-gate satisfied by submitted scorecard, cross-story US-REC-004) | 1 |
| Security (REC-006) | TC-REC-006-05 (anti-bias hide), TC-REC-006-06 (assigned-interviewer-only authz), TC-REC-006-13 (feedback XSS sanitization, also a11y), TC-REC-ISO-015 (+ reused TC-REC-ISO-010/011) | 3 + 1 ISO |
| Performance (REC-006) | TC-REC-006-12 (submission <=800ms P95 incl. average/audit/enqueue; async delivery) | 1 |
| Accessibility / Cross-browser (REC-006) | TC-REC-006-13 (scorecard form + recruiter consolidated view WCAG 2.1 AA + responsive 360px) | 1 |

(Note: TC-REC-006-02 carries Happy + Negative tags and is critical for the completion gate; TC-REC-006-03/08/09/10 carry Boundary + Negative; TC-REC-006-05/06 are typed Security while also Negative; TC-REC-006-11 carries Happy + Negative; TC-REC-006-13 carries Security + Accessibility + Cross-browser.)

## Acceptance Criteria Coverage (US-REC-006)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Submit scorecard (rate criteria 1-5 + comments) -> saved, average computed, interview Completed when all submit, recruiter notified | TC-REC-006-01, TC-REC-006-02, TC-REC-006-06, TC-REC-006-07, TC-REC-006-10, TC-REC-006-12, TC-REC-006-13 |
| AC-2 | Recruiter applicant-detail view shows individual criterion scores + overall average + written feedback | TC-REC-006-04, TC-REC-006-03 |
| AC-3 | Multiple interviewers: independent scorecards + consolidated aggregate average | TC-REC-006-03, TC-REC-006-05 |
| AC-4 | Tenant B sees zero of Tenant A's scorecards; isolation enforced | TC-REC-ISO-015 (+ reused TC-REC-ISO-010, TC-REC-ISO-011) |

## Functional Requirement Coverage (US-REC-006)

| FR | Covered By |
|----|------------|
| FR-1 (scorecard form: configured criteria, 1-5 scale + labels, optional per-criterion comment, mandatory overall recommendation) | TC-REC-006-01, TC-REC-006-07, TC-REC-006-10, TC-REC-006-13 |
| FR-2 (exactly one scorecard per interviewer per interview; edits until lock period) | TC-REC-006-08, TC-REC-006-09 |
| FR-3 (per-card average + aggregate average across interviewers) | TC-REC-006-01, TC-REC-006-03, TC-REC-006-10 |
| FR-4 (interview -> Completed when ALL assigned interviewers submit) | TC-REC-006-01, TC-REC-006-02 |
| FR-5 (notify recruiter on submission, in-app + optional email via Hangfire) | TC-REC-006-01, TC-REC-006-12 (CONDITIONAL on Notification System S25) |
| FR-6 (interviewer cannot view others' scorecards until own submitted; anti-bias) | TC-REC-006-05 |
| FR-7 (audit scorecard submissions) | TC-REC-006-01, TC-REC-006-08 (Audit module dependency) |
| FR-8 (recruiter view: individual scores + comparison data, e.g. radar/bar chart) | TC-REC-006-03, TC-REC-006-04 |

## Non-Functional Requirement Coverage (US-REC-006)

| NFR | Covered By |
|-----|------------|
| NFR-1 (scorecard submission API <= 800ms P95) | TC-REC-006-12 |
| NFR-2 (scorecard data tenant-scoped + RLS) | TC-REC-ISO-015 (EF query filters today; RLS extension point) |
| NFR-3 (scorecard form mobile-responsive 360px+) | TC-REC-006-13 |
| NFR-4 (scorecard data in analytics aggregations; sub-second up to 1000 scorecards/tenant) | TC-REC-006-12, TC-REC-ISO-015 (aggregate tenant-scoped) (full analytics owned by US-REC-009) |

## Business Rule Coverage (US-REC-006)

| BR | Covered By |
|----|------------|
| BR-1 (only the assigned interviewer can submit) | TC-REC-006-06 |
| BR-2 (evaluation criteria configurable per tenant; defaults provided) | TC-REC-006-01 (defaults used); full per-tenant config owned by tenant module config S35.2.9 |
| BR-3 (overall recommendation mandatory) | TC-REC-006-07 |
| BR-4 (immutable after lock period; in-window edits create version history) | TC-REC-006-08 |
| BR-5 (cannot view others' scorecards until own submitted; anti-bias) | TC-REC-006-05 |
| BR-6 (submitted scorecard gates advancement to Offer, US-REC-004 FR-1) | TC-REC-006-11 |

## Conditional / Deferred (US-REC-006)

- **Recruiter notification depends on Notification System S25 + Hangfire (FR-5):** TC-REC-006-01/12 assert the queued in-app/outbox entry + non-blocking dispatch; if delivery is a LOG-ONLY stub, assert the outbox/log record. CONDITIONAL, not a gap.
- **Audit trail depends on the Audit Logging module (FR-7):** TC-REC-006-01/08 assert the submission/edit audit entry; if the cross-cutting Audit module is not yet integrated, assert the in-module audit hook/record. CONDITIONAL, not a gap.
- **Version history (BR-4):** TC-REC-006-08 asserts in-window edit + version-history capture IF version history is implemented; if not yet implemented, the case asserts the edit + audit at minimum and notes version history as pending -- it must NOT be skipped or weakened to "edits not tracked".
- **Per-tenant criteria configuration (BR-2/FR-1):** evaluation criteria are configured via tenant module config S35.2.9; TC-REC-006-01 exercises the defaults (Technical Skills, Communication, Problem Solving, Cultural Fit). Full per-tenant criteria customization (and per-vacancy criteria) is a future enhancement per story assumptions.
- **Offer-gate evaluator (BR-6):** TC-REC-006-11 closes the seam REC-004 left CONDITIONAL on US-REC-006. If REC-004's gate evaluator is still stubbed in this increment, the case asserts the scorecard-presence query the gate consumes; it must NOT be weakened to "no gate".
- **Recommendation NOT averaged (FR-3):** the numeric average is over criterion scores only; the overall recommendation enum is kept per-interviewer and not folded into any average (asserted in TC-REC-006-03).
- **Analytics aggregation (NFR-4):** full recruitment analytics is owned by US-REC-009; TC-REC-006-12 asserts the sub-second read over up to 1000 scorecards/tenant and TC-REC-ISO-015 asserts the aggregate stays tenant-scoped.
- **EF query filters vs PostgreSQL RLS (AC-4/NFR-2):** US-REC-006 specifies RLS on the scorecard tables; the platform enforces isolation via EF Core global query filters + TenantInterceptor. TC-REC-ISO-015 describes the EF mechanism and notes RLS session-level assertion as an extension point if added.
- **ISO reuse:** TC-REC-ISO-010 (no/invalid/mismatched tenant context) and TC-REC-ISO-011 (cross-tenant write block + body-injected tenant_id) are reused for the recruitment surface; TC-REC-ISO-015 adds the interview-scorecard/criterion-rating dimension specific to US-REC-006.

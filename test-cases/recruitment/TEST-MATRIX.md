---
module: Recruitment
total_user_stories: 3
total_test_cases: 55
created: 2026-06-15
updated: 2026-06-15
status: in-progress
---

# Recruitment -- Test Matrix

> US-REC-001 (Create and Publish Job Vacancy) established `test-cases/recruitment/` -- 16 test cases (12 functional/security/perf/a11y + 4 dedicated multi-tenant isolation). US-REC-002 (Applicant Submits Application with Resume Upload) adds 21 test cases (13 functional/security/perf/a11y: TC-REC-002-01..13 + 4 dedicated multi-tenant isolation on the new `applicant` table: TC-REC-ISO-005..008). US-REC-003 (Recruiter Views Applicant Pipeline with Stage Management) adds 18 test cases (14 functional/security/perf/a11y: TC-REC-003-01..14 + 4 dedicated multi-tenant isolation on the pipeline/stage-move/stage-history operations: TC-REC-ISO-009..012). Module total: 55 test cases, 15/15 acceptance criteria covered.

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 3 (US-REC-001, US-REC-002, US-REC-003) |
| Total Test Cases | 55 (39 functional/security/perf/a11y + 12 dedicated multi-tenant isolation) |
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

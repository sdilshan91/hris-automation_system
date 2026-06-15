---
module: Recruitment
total_user_stories: 2
total_test_cases: 37
created: 2026-06-15
updated: 2026-06-15
status: in-progress
---

# Recruitment -- Test Matrix

> US-REC-001 (Create and Publish Job Vacancy) established `test-cases/recruitment/` -- 16 test cases (12 functional/security/perf/a11y + 4 dedicated multi-tenant isolation). US-REC-002 (Applicant Submits Application with Resume Upload) adds 21 test cases (13 functional/security/perf/a11y: TC-REC-002-01..13 + 4 dedicated multi-tenant isolation on the new `applicant` table: TC-REC-ISO-005..008). Module total: 37 test cases, 10/10 acceptance criteria covered.

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 2 (US-REC-001, US-REC-002) |
| Total Test Cases | 37 (25 functional/security/perf/a11y + 8 dedicated multi-tenant isolation) |
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

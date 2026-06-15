---
module: Recruitment
total_user_stories: 1
total_test_cases: 16
created: 2026-06-15
updated: 2026-06-15
status: in-progress
---

# Recruitment -- Test Matrix

> First Recruitment story. US-REC-001 (Create and Publish Job Vacancy) establishes `test-cases/recruitment/` -- 16 test cases (12 functional/security/perf/a11y + 4 dedicated multi-tenant isolation), 5/5 acceptance criteria covered.

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 1 (US-REC-001) |
| Total Test Cases | 16 (12 functional/security/perf/a11y + 4 dedicated multi-tenant isolation) |
| Critical Priority | 5 (TC-REC-001-01, -08, -09, TC-REC-ISO-001, -002, -003) |
| High Priority | 8 |
| Medium Priority | 1 (TC-REC-001-07) |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Conditional/Deferred Test Cases | EF-query-filter-vs-PostgreSQL-RLS: US-REC-001 AC-4/NFR-2 say PostgreSQL RLS on the `vacancy` table; this platform currently enforces isolation via EF Core global query filters + TenantInterceptor. TC-REC-ISO-001/003 describe the EF mechanism and note the RLS session-level assertion as an extension point if an RLS policy is added on `vacancy`. Vacancy list/detail Redis cache (NFR-1): not assumed wired -- TC-REC-ISO-004 Step 1 verifies the DB-backed read path now and documents the tenant-scoped key design `tenant:{tenantId}:vacancies:...`, marking the cache portion conditional. Public careers page toggle (FR-4/BR-5) depends on the tenant module configuration (S35.2.9); the per-vacancy exclusion + tenant-level disable paths are verified in TC-REC-001-10. Audit-trail assertions (AC-3/FR-7) depend on the Audit logging module exposing vacancy create/update/publish/close entries (verified in TC-REC-001-03/04). Applicant-related steps (AC-5/BR-3 in TC-REC-001-04) assume applicant records exist; the full applicant lifecycle is owned by later Recruitment stories. |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-REC-001 | Create and Publish Job Vacancy | TC-REC-001-01, TC-REC-001-02, TC-REC-001-03, TC-REC-001-04, TC-REC-001-05, TC-REC-001-06, TC-REC-001-07, TC-REC-001-08, TC-REC-001-09, TC-REC-001-10, TC-REC-001-11, TC-REC-001-12 | 12 |
| Cross-cutting (REC-001) | Multi-tenant isolation (mandatory) | TC-REC-ISO-001, TC-REC-ISO-002, TC-REC-ISO-003, TC-REC-ISO-004 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional / E2E (REC-001) | TC-REC-001-01, TC-REC-001-02, TC-REC-001-03, TC-REC-001-04, TC-REC-001-05, TC-REC-001-06, TC-REC-001-07, TC-REC-001-10 | 8 |
| Security (REC-001) | TC-REC-001-08 (authz BR-1), TC-REC-001-09 (XSS NFR-4), TC-REC-001-10 (public exposure), TC-REC-ISO-001, TC-REC-ISO-002, TC-REC-ISO-003, TC-REC-ISO-004 | 7 |
| Performance (REC-001) | TC-REC-001-11 (list <= 400ms P95 @ 500 vacancies) | 1 |
| Accessibility / Cross-browser (REC-001) | TC-REC-001-12 (form + public careers page WCAG 2.1 AA, responsive 360px-4K) | 1 |

(Note: TC-REC-001-10 is counted under Functional in the AC mapping and under Security in the type distribution because it validates both a functional public-listing flow and a public-exposure access control. TC-REC-001-07 carries the boundary tag while being functionally typed. TC-REC-001-11 also carries the multi-tenant isolation tag, verifying tenant-scoped results under load.)

## Acceptance Criteria Coverage (US-REC-001)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Save as Draft -> vacancy persisted with status `Draft`, visible only to Recruitment.Read.All in the same tenant; tenant_id from session | TC-REC-001-01, TC-REC-001-07, TC-REC-001-08 |
| AC-2 | Publish -> status `Open`, appears on internal listing, and on the public careers page if tenant has it enabled | TC-REC-001-01, TC-REC-001-02, TC-REC-001-05, TC-REC-001-06, TC-REC-001-10 |
| AC-3 | Edit an Open vacancy -> updated, audit log entry created, reflected on internal + public listings | TC-REC-001-03 |
| AC-4 | Tenant B sees zero of Tenant A's vacancies; tenant isolation enforced | TC-REC-ISO-001, TC-REC-ISO-002, TC-REC-ISO-003, TC-REC-ISO-004 |
| AC-5 | Close -> status `Closed`, no new applications, existing applicants remain in stage | TC-REC-001-04, TC-REC-001-06 |

## Functional Requirement Coverage (US-REC-001)

| FR | Covered By |
|----|------------|
| FR-1 (creation form fields incl. title max 200, headcount >= 1, rich text desc) | TC-REC-001-01, TC-REC-001-05, TC-REC-001-07 |
| FR-2 (statuses Draft/Open/On Hold/Closed/Cancelled + transitions) | TC-REC-001-01, TC-REC-001-04, TC-REC-001-06 |
| FR-3 (attach tenant pipeline stages, default if none) | TC-REC-001-01 |
| FR-4 (publish to public careers page if tenant-enabled) | TC-REC-001-02, TC-REC-001-10 |
| FR-5 (unique SEO-friendly URL slug) | TC-REC-001-02, TC-REC-ISO-004 |
| FR-6 (bulk status changes) | Deferred to a later Recruitment story (bulk-close UI); single-status transitions verified in TC-REC-001-04/06 |
| FR-7 (audit all create/update/publish/close actions) | TC-REC-001-03, TC-REC-001-04 |

## Non-Functional Requirement Coverage (US-REC-001)

| NFR | Covered By |
|-----|------------|
| NFR-1 (list <= 400ms P95 @ 500 vacancies) | TC-REC-001-11 |
| NFR-2 (tenant-scoped via tenant_id + RLS defense-in-depth) | TC-REC-ISO-001, TC-REC-ISO-002, TC-REC-ISO-003, TC-REC-ISO-004 (EF query filters today, RLS noted as extension point) |
| NFR-3 (responsive 360px-4K) | TC-REC-001-12 |
| NFR-4 (rich-text HTML sanitization, anti-XSS) | TC-REC-001-09 |
| NFR-5 (public careers page accessible without auth + WCAG 2.1 AA) | TC-REC-001-10, TC-REC-001-12 |

## Business Rule Coverage (US-REC-001)

| BR | Covered By |
|----|------------|
| BR-1 (only Recruitment.Create.All / Manage.All create or edit vacancies) | TC-REC-001-08 |
| BR-2 (cannot publish without title/department/job title/hiring manager/headcount/description) | TC-REC-001-05 |
| BR-3 (closing does not delete/reject existing applicants) | TC-REC-001-04 |
| BR-4 (headcount = max positions to fill, integer >= 1) | TC-REC-001-07 |
| BR-5 (public careers toggle tenant-level; per-vacancy exclusion) | TC-REC-001-10, TC-REC-ISO-004 |

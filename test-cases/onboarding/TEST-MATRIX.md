---
module: Onboarding / Offboarding
total_user_stories: 1
total_test_cases: 16
created: 2026-06-17
updated: 2026-06-17
status: in-progress
---

# Onboarding / Offboarding -- Test Matrix

> US-ONB-001 (Create Onboarding Checklist Template) is the FIRST Onboarding story and establishes `test-cases/onboarding/` (dir + this TEST-MATRIX + the root Onboarding section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-ONB-001-01..12) + 4 dedicated multi-tenant isolation (TC-ONB-ISO-001..004). The Onboarding module reuses the per-story-suffix functional ID scheme from Recruitment/Payroll/Admin Console (TC-ONB-{NNN}-XX) with a separate running ISO counter (TC-ONB-ISO-NNN) starting at 001. All 5 acceptance criteria of US-ONB-001 are covered.
>
> PLATFORM ACCURACY / DEFERRED: AC-5 and NFR-2 specify PostgreSQL RLS as a tenant-isolation layer. This codebase enforces isolation via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**, NOT Postgres RLS — RLS is a deferred platform extension point (same family as Auth/Leave/Payroll/Admin). The isolation tests (TC-ONB-ISO-001..004) are written against the EF query-filter + interceptor mechanism in force today; the story's "raw SQL without app.current_tenant_id returns zero rows" RLS expectation is documented as CONDITIONAL/deferred (TC-ONB-ISO-003 step 4). The cross-tenant ID-injection test asserts **404, not 403** (existence not disclosed, TC-ONB-ISO-002). Cache-key scoping (TC-ONB-ISO-004) is asserted as tenant-keyed; if no distributed cache is wired yet, it asserts the equivalent always-tenant-filtered property and flags `onboarding:templates:{tenant_id}` as the target key shape. NFR-1 (<= 500 ms P95) requires a performance-representative environment.
>
> STORY MISMATCH worth flagging to the caller: AC-5/NFR-2 name PostgreSQL RLS as an active isolation layer — only the app (ITenantContext) + EF (query filter / TenantInterceptor) layers exist today; reword RLS as future hardening (consistent with how prior modules were handled).

## Coverage by Test Case

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ONB-001-01 | Create template with multiple categories/tasks; persists with correct tenant_id | E2E | Critical | AC-1, AC-2, AC-4, FR-1/2/3/4/5/8, BR-2/3 | Happy path |
| TC-ONB-001-02 | Universal template (empty dept/job-title scope) saved | Functional | High | AC-1, AC-2, FR-4/3/5 | Happy / boundary |
| TC-ONB-001-03 | Duplicate template name rejected (same name OK across tenants) | Functional | Critical | AC-3, BR-1 | Negative |
| TC-ONB-001-04 | Zero-task template rejected (server-side) | Functional | Critical | AC-2, BR-2 | Negative / boundary |
| TC-ONB-001-05 | Negative due_offset_days rejected; 0 accepted | Functional | High | AC-2, BR-3 | Negative / boundary |
| TC-ONB-001-06 | template_name length boundaries (3/200) + due_offset 0 | Functional | Medium | AC-1, AC-2, BR-3 | Boundary |
| TC-ONB-001-07 | Onboarding.Manage required; 403/401 deny, no create | Security | Critical | AC-1, AC-2, FR-5 | Negative / security |
| TC-ONB-001-08 | XSS/SQLi payloads in free-text fields neutralized | Security | High | AC-1, AC-2, FR-2/3 | Negative / security |
| TC-ONB-001-09 | Clone template — new id, tasks duplicated, independent | Functional | High | AC-2, FR-6/3/5/8, BR-1 | Happy path |
| TC-ONB-001-10 | Deactivate/reactivate — soft toggle, removed from assign list | Functional | High | AC-2, FR-7, BR-4 | Functional |
| TC-ONB-001-11 | Create API <= 500 ms P95 | Performance | High | AC-2, NFR-1 | Performance |
| TC-ONB-001-12 | Keyboard-navigable reorder + up/down alt + responsive 360-4K | Accessibility | Medium | AC-1, AC-2, FR-1, NFR-4/3 | Accessibility / cross-browser |
| TC-ONB-ISO-001 | Tenant A cannot see Tenant B templates (cross-tenant READ block) | Security | Critical | AC-5, NFR-2 (EF), BR-1 | Multi-tenant isolation |
| TC-ONB-ISO-002 | Missing/invalid tenant context + cross-tenant ID injection -> 404 | Security | Critical | AC-5, FR-5 | Multi-tenant isolation |
| TC-ONB-ISO-003 | EF query filter blocks reads; writes tenant-stamped (RLS deferred) | Security | Critical | AC-5, FR-5, NFR-2 | Multi-tenant isolation |
| TC-ONB-ISO-004 | Onboarding cache/lookup keys tenant-scoped | Security | High | AC-5, NFR-2 | Multi-tenant isolation |

## Acceptance-Criteria Coverage (US-ONB-001)

| AC | Covered By |
|----|-----------|
| AC-1 (Create Template form: name, description, dept(s), job title(s), task builder) | TC-ONB-001-01, -02, -06, -07, -12 |
| AC-2 (save persists template + tasks with all task fields; tenant_id from session) | TC-ONB-001-01, -02, -04, -05, -06, -09, -10, -11 |
| AC-3 (duplicate name -> "A checklist template with this name already exists.") | TC-ONB-001-03 |
| AC-4 (mandatory-flagged tasks persisted and non-skippable) | TC-ONB-001-01 |
| AC-5 (cross-tenant isolation; only own tenant templates visible) | TC-ONB-ISO-001, -002, -003, -004 |

## FR / NFR / BR Coverage

| Requirement | Covered By |
|-------------|-----------|
| FR-1 (builder w/ drag-and-drop ordering) | TC-ONB-001-01, -12 |
| FR-2 (free-text categories) | TC-ONB-001-01, -08 |
| FR-3 (task fields: title/desc/category/responsible/offset/mandatory/sort) | TC-ONB-001-01, -05, -06, -08 |
| FR-4 (dept/job-title scope or universal) | TC-ONB-001-01, -02 |
| FR-5 (tenant_id from session, never user input) | TC-ONB-001-01, -07, TC-ONB-ISO-002, -003 |
| FR-6 (clone existing template) | TC-ONB-001-09 |
| FR-7 (activate/deactivate soft toggle) | TC-ONB-001-10 |
| FR-8 (audit columns auto-populated) | TC-ONB-001-01, -09, -10 |
| NFR-1 (create API <= 500 ms P95) | TC-ONB-001-11 |
| NFR-2 (tenant isolation; RLS deferred -> EF filters) | TC-ONB-ISO-001, -002, -003, -004 |
| NFR-3 (responsive 360px-4K) | TC-ONB-001-12 |
| NFR-4 (WCAG 2.1 AA, keyboard drag-and-drop) | TC-ONB-001-12 |
| NFR-5 (audit log via SaveChangesInterceptor) | TC-ONB-001-01, -10 (audit columns) |
| BR-1 (name unique per tenant; repeats across tenants) | TC-ONB-001-03, -09, TC-ONB-ISO-001 |
| BR-2 (>= 1 task to save) | TC-ONB-001-04, -01 |
| BR-3 (due offset non-negative integer; 0 = same day) | TC-ONB-001-05, -06 |
| BR-4 (deactivated not assignable, still visible) | TC-ONB-001-10 |
| BR-5 (soft delete) | Out of scope for US-ONB-001 create flow; deletion is a separate story (flag to caller — no delete endpoint in this story) |

## Summary (US-ONB-001)

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ONB-001) |
| Total test cases | 16 (12 functional/security/perf/a11y + 4 isolation) |
| AC coverage | 5/5 |
| Functional ID range | TC-ONB-001-01 .. TC-ONB-001-12 |
| ISO ID range | TC-ONB-ISO-001 .. TC-ONB-ISO-004 |

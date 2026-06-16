---
module: Admin Console
total_user_stories: 1
total_test_cases: 16
created: 2026-06-16
updated: 2026-06-16
status: in-progress
---

# Admin Console -- Test Matrix

> US-ADM-001 (System Admin Provisions New Tenant) is the FIRST Admin Console story and establishes `test-cases/admin-console/` (dir + this TEST-MATRIX + the root Admin Console section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-ADM-001-01..12) + 4 dedicated multi-tenant isolation (TC-ADM-ISO-001..004). The Admin Console module reuses the per-story-suffix functional ID scheme from Recruitment/Payroll (TC-ADM-{NNN}-XX) with a separate running ISO counter (TC-ADM-ISO-NNN) starting at 001. All 6 acceptance criteria of US-ADM-001 are covered.
>
> PLATFORM ACCURACY / DEFERRED: AC-6 and FR-6 of US-ADM-001 specify PostgreSQL RLS + the `app.current_tenant_id` session variable. This codebase enforces tenant isolation via **EF Core global query filters (read) + `TenantInterceptor` (write stamping)**, NOT Postgres RLS -- RLS is a deferred platform extension point. The isolation test cases (TC-ADM-001-09, TC-ADM-ISO-001..004) are written against the EF query-filter mechanism in force today; the story's "raw SQL without app.current_tenant_id returns zero rows" RLS-verification hint (FR-6) is documented as CONDITIONAL/deferred. The cross-tenant ID-injection test asserts **404, not 403** per the story Test Hints (existence not disclosed). FR-7 Redis tenant-config cache (TC-ADM-ISO-004) is asserted as tenant-keyed; if no distributed cache layer is wired yet, the test asserts the equivalent always-tenant-filtered property and flags the Redis key as the target. Welcome-email DELIVERY (FR-4) is asserted against a test SMTP sink (the dispatch/enqueue is the assertion). NFR-1 (<60s/<5min) requires a performance-representative environment.

## Coverage by Test Case

| Test Case | Title | Type | Priority | ACs / Reqs Covered | Category |
|-----------|-------|------|----------|--------------------|----------|
| TC-ADM-001-01 | Provision new tenant end-to-end | E2E | Critical | AC-1, AC-4, FR-1/3/4/5/7, BR-3/4/5 | Happy path |
| TC-ADM-001-02 | Existing global user linked, not duplicated | Functional | High | AC-3, AC-1, FR-3/4, BR-4 | Existing-user link / boundary |
| TC-ADM-001-03 | Duplicate subdomain rejected (incl. terminated) | Functional | Critical | AC-2, FR-2, BR-2 | Negative |
| TC-ADM-001-04 | Every reserved subdomain rejected | Functional | High | AC-2, FR-2 | Negative (data-driven) |
| TC-ADM-001-05 | Invalid subdomain formats rejected | Functional | High | AC-5, FR-1 | Negative |
| TC-ADM-001-06 | Subdomain length boundaries (3 and 50) accepted | Functional | Medium | AC-5, AC-1, FR-1 | Boundary |
| TC-ADM-001-07 | trial_days = 0 -> active vs > 0 -> trial | Functional | High | AC-1, FR-1, BR-3 | Boundary |
| TC-ADM-001-08 | Only SystemAdmin may provision; SystemSupport denied | Security | Critical | BR-1, BR-5, NFR-3 | Negative / security |
| TC-ADM-001-09 | Cross-tenant ID injection -> 404 not 403 | Security | Critical | AC-6, FR-6 (EF), Test Hints | Multi-tenant isolation |
| TC-ADM-001-10 | Provisioning within < 60s / < 5min SLA | Performance | High | NFR-1, FR-3/7 | Performance |
| TC-ADM-001-11 | Create Tenant form WCAG 2.1 AA + responsive 360-4K | Accessibility | Medium | NFR-4, S8 | Accessibility |
| TC-ADM-001-12 | Idempotent provisioning -- retry never duplicates | Integration | Critical | NFR-2, FR-3/4 | Negative / idempotency |
| TC-ADM-ISO-001 | New tenant data isolated (cross-tenant READ block) | Security | Critical | AC-6, FR-6 (EF), BR-1 | Multi-tenant isolation |
| TC-ADM-ISO-002 | API rejects missing/invalid/mismatched tenant context + IDOR | Security | Critical | AC-6, BR-1 | Multi-tenant isolation |
| TC-ADM-ISO-003 | EF query filter blocks reads; writes tenant-stamped (RLS deferred) | Security | Critical | AC-6, FR-3/6 | Multi-tenant isolation |
| TC-ADM-ISO-004 | Tenant config cache key is tenant-scoped | Security | High | AC-6, FR-7 | Multi-tenant isolation |

## Acceptance-Criteria Coverage (US-ADM-001)

| AC | Covered By |
|----|-----------|
| AC-1 (full provisioning: tenant+user+user_tenant+TenantOwner+seed+lifecycle 'created'+audit+welcome email) | TC-ADM-001-01, -02, -06, -07 |
| AC-2 (duplicate + reserved subdomain rejected) | TC-ADM-001-03, -04 |
| AC-3 (existing global user linked, no duplicate) | TC-ADM-001-02 |
| AC-4 (tenant in list; lifecycle 'created'; system_audit_log) | TC-ADM-001-01 |
| AC-5 (invalid subdomain formats rejected, client + server) | TC-ADM-001-05, -06 |
| AC-6 (tenant resolution + complete data isolation) | TC-ADM-001-09, TC-ADM-ISO-001, -002, -003, -004 |

## NFR / BR Coverage

| Requirement | Covered By |
|-------------|-----------|
| NFR-1 (<60s target / <5min SLA) | TC-ADM-001-10 |
| NFR-2 (idempotent retry, no duplicates) | TC-ADM-001-12 |
| NFR-3 (audit log of provisioning incl. denied) | TC-ADM-001-01, -08 |
| NFR-4 (WCAG 2.1 AA + responsive 360-4K) | TC-ADM-001-11 |
| BR-1 (only SystemAdmin; SystemSupport denied) | TC-ADM-001-08, TC-ADM-ISO-001/002 |
| BR-2 (subdomain not reusable after termination) | TC-ADM-001-03, -12 |
| BR-3 (trial_days -> status) | TC-ADM-001-07 |
| BR-4 (owner email -> billing_email) | TC-ADM-001-01, -02 |
| BR-5 (is_system=false; system tenant not creatable) | TC-ADM-001-01, -08 |

## Summary

| Metric | Value |
|--------|-------|
| User stories covered | 1 (US-ADM-001) |
| Total test cases | 16 (12 functional/security/perf/a11y + 4 isolation) |
| AC coverage | 6/6 |
| Functional ID range | TC-ADM-001-01 .. TC-ADM-001-12 |
| ISO ID range | TC-ADM-ISO-001 .. TC-ADM-ISO-004 |

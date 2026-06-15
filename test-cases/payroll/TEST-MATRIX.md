---
module: Payroll
total_user_stories: 1
total_test_cases: 16
created: 2026-06-15
updated: 2026-06-15
status: in-progress
---

# Payroll -- Test Matrix

> US-PAY-001 (Configure Salary Structure and Components per Tenant) is the FIRST Payroll story and establishes `test-cases/payroll/` (dir + TEST-MATRIX + the root Payroll section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-PAY-001-01..12) + 4 dedicated multi-tenant isolation on the new `salary_component` / `salary_structure` / `salary_structure_component` tables (TC-PAY-ISO-001..004). The Payroll module reuses the same per-story-suffix functional ID scheme as Recruitment (TC-PAY-{NNN}-XX) with a separate running ISO counter (TC-PAY-ISO-NNN). All 6 acceptance criteria of US-PAY-001 are covered.

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 1 (US-PAY-001) -- module bootstrap |
| Total Test Cases | 16 (12 functional/security/perf/a11y + 4 dedicated multi-tenant isolation) |
| US-PAY-001 Test Cases | 16 (TC-PAY-001-01..12 + TC-PAY-ISO-001..004) |
| Critical Priority | 5 (TC-PAY-001-01, TC-PAY-001-07, TC-PAY-001-08, TC-PAY-ISO-001, TC-PAY-ISO-002, TC-PAY-ISO-003) |
| High Priority | 9 (TC-PAY-001-02, -03, -04, -05, -06, -09, -10, -11, -12, TC-PAY-ISO-004) |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Acceptance Criteria Coverage | 6/6 (AC-1..AC-6) |
| Conditional/Deferred Test Cases | (1) EF-query-filter-vs-PostgreSQL-RLS: US-PAY-001 AC-6/FR-8 specify PostgreSQL RLS policies on the payroll tables; this platform enforces isolation via EF Core global query filters + TenantInterceptor. TC-PAY-ISO-001/003 describe the EF mechanism and note RLS session-level assertion as an extension point if RLS policies are later added. (2) Redis cache (NFR-1, key `tenant:{tenantId}:payroll:...`) written as a real test (TC-PAY-ISO-004, TC-PAY-001-11) -- assumes Redis available per S10; cache-invalidation-on-write asserted. (3) Historical-payslip-unchanged (AC-2, TC-PAY-001-02 step 4) and BR-7 type-change-after-finalized-run are CONDITIONAL on the Payroll Run/Payslip story existing (not yet built). (4) Audit assertions (NFR-5) reference the Audit logging module (S24); the enqueue/record is asserted, full audit-store verification owned by the Audit module. (5) Employee-assignment counting for AC-5 affected-count depends on the Core HR + salary-structure-assignment surface (a later Payroll story); TC-PAY-001-04 asserts the 409 + affected-count contract. |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-PAY-001 | Configure Salary Structure and Components per Tenant | TC-PAY-001-01, TC-PAY-001-02, TC-PAY-001-03, TC-PAY-001-04, TC-PAY-001-05, TC-PAY-001-06, TC-PAY-001-07, TC-PAY-001-08, TC-PAY-001-09, TC-PAY-001-10, TC-PAY-001-11, TC-PAY-001-12 | 12 |
| Cross-cutting (PAY-001) | Multi-tenant isolation (salary_component / salary_structure / junction) | TC-PAY-ISO-001, TC-PAY-ISO-002, TC-PAY-ISO-003, TC-PAY-ISO-004 | 4 |

## Test Case Detail

| Test Case ID | Title | Type | Priority | Category | ACs / FRs / BRs |
|--------------|-------|------|----------|----------|-----------------|
| TC-PAY-001-01 | Create component, create structure, link + reorder + activate (happy path) | E2E | Critical | Happy path | AC-1, AC-3, AC-4, FR-1, FR-2, FR-3, FR-5, BR-1, BR-2 |
| TC-PAY-001-02 | Edit component -> future runs use new def; historical payslips unchanged | Functional | High | Alternative path | AC-2, FR-1, NFR-1, NFR-5, BR-7 |
| TC-PAY-001-03 | Duplicate component code per tenant rejected; allowed in another tenant | Functional | High | Negative | AC-1, FR-1, BR-2 |
| TC-PAY-001-04 | Delete component in use -> 409 + affected-count; statutory protected | Functional | Critical | Negative | AC-5, FR-1, FR-3, BR-3 |
| TC-PAY-001-05 | Activate structure with no earning rejected (FR-5) | Functional | High | Negative | AC-3, FR-2, FR-3, FR-5 |
| TC-PAY-001-06 | numeric(18,2) precision; deduction > gross (BR-4); pagination max 100 | Functional | High | Boundary | AC-1, AC-3, FR-1, FR-3, NFR-3, BR-4 |
| TC-PAY-001-07 | Formula rejects circular refs + invalid syntax; safe eval only (BR-6) | Functional | Critical | Negative/Security | AC-1, AC-3, FR-1, FR-4, BR-6 |
| TC-PAY-001-08 | Authz: only Payroll.*.All / Tenant Admin configure; others 403 | Security | Critical | Security | AC-1, AC-2, AC-3, AC-4, AC-5, FR-1, FR-2, FR-3 |
| TC-PAY-001-09 | Names/formula fields resist XSS + SQL injection | Security | High | Security | AC-1, AC-3, FR-1, FR-2, FR-4, NFR-5 |
| TC-PAY-001-10 | Single default structure (BR-1) + clone structure (FR-6) | Functional | High | Negative/Alternative | AC-3, FR-2, FR-3, FR-6, BR-1 |
| TC-PAY-001-11 | Fetch all components <= 200ms P95 (NFR-2); pagination scales (NFR-3) | Performance | High | Performance | AC-1, NFR-1, NFR-2, NFR-3 |
| TC-PAY-001-12 | Slide-over + inline table + drag-reorder WCAG 2.1 AA | Accessibility | High | Accessibility | AC-1, AC-3, AC-4 |
| TC-PAY-ISO-001 | Tenant A cannot see/retrieve Tenant B's components/structures (read iso) | Security | Critical | Multi-tenant isolation | AC-6, FR-8 |
| TC-PAY-ISO-002 | Payroll APIs reject no/invalid/mismatched tenant context | Security | Critical | Multi-tenant isolation | AC-6, FR-8 |
| TC-PAY-ISO-003 | Cross-tenant writes blocked; tenant_id session-derived | Security | Critical | Multi-tenant isolation | AC-6, FR-1, FR-2, FR-3, FR-8 |
| TC-PAY-ISO-004 | Payroll list caches tenant-scoped (no cross-tenant cache leak) | Security | High | Multi-tenant isolation | AC-6, FR-8, NFR-1 |

## Acceptance Criteria Coverage (US-PAY-001)

| AC | Description | Covered By |
|----|-------------|-----------|
| AC-1 | Create component saved with tenant_id, visible to this tenant only | TC-PAY-001-01, -03, -06, -07, -08, -09, -11, -12, TC-PAY-ISO-001 |
| AC-2 | Edit component -> future runs use new def; history unchanged | TC-PAY-001-02, TC-PAY-001-08 |
| AC-3 | Create structure + add components with rules | TC-PAY-001-01, -05, -06, -07, -09, -10, -12 |
| AC-4 | Reorder component processing priority | TC-PAY-001-01, TC-PAY-001-12 |
| AC-5 | Prevent delete of component in use; show affected count | TC-PAY-001-04, TC-PAY-001-08 |
| AC-6 | Tenant A sees only its components; RLS-level isolation | TC-PAY-ISO-001, TC-PAY-ISO-002, TC-PAY-ISO-003, TC-PAY-ISO-004, TC-PAY-001-03 |

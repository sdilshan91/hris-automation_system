---
module: Payroll
total_user_stories: 2
total_test_cases: 32
created: 2026-06-15
updated: 2026-06-15
status: in-progress
---

# Payroll -- Test Matrix

> US-PAY-001 (Configure Salary Structure and Components per Tenant) is the FIRST Payroll story and establishes `test-cases/payroll/` (dir + TEST-MATRIX + the root Payroll section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-PAY-001-01..12) + 4 dedicated multi-tenant isolation on the new `salary_component` / `salary_structure` / `salary_structure_component` tables (TC-PAY-ISO-001..004). The Payroll module reuses the same per-story-suffix functional ID scheme as Recruitment (TC-PAY-{NNN}-XX) with a separate running ISO counter (TC-PAY-ISO-NNN). All 6 acceptance criteria of US-PAY-001 are covered.
>
> US-PAY-002 (Assign Salary Structure to Employee) adds 16 test cases: 12 functional/security/performance/accessibility (TC-PAY-002-01..12) covering CTC-driven `employee_salary_component` creation, breakdown preview, component overrides, inactive-structure rejection, CTC-sum tolerance, future-dated supersession, numeric(18,2) precision, bulk assign, authz, injection, perf SLAs, and revision history; plus 4 dedicated multi-tenant isolation on the new `employee_salary_component` / `salary_revision_history` tables (TC-PAY-ISO-005..008, continuing the ISO counter from 004). All 5 acceptance criteria of US-PAY-002 are covered.

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 2 (US-PAY-001, US-PAY-002) |
| Total Test Cases | 32 (24 functional/security/perf/a11y + 8 dedicated multi-tenant isolation) |
| US-PAY-001 Test Cases | 16 (TC-PAY-001-01..12 + TC-PAY-ISO-001..004) |
| US-PAY-002 Test Cases | 16 (TC-PAY-002-01..12 + TC-PAY-ISO-005..008) |
| Critical Priority | 11 (PAY-001-01, -07, -08, ISO-001..003; PAY-002-01, -03, -08, ISO-005..007) |
| High Priority | 21 (remaining PAY-001 + PAY-002 functional/perf/a11y + ISO-004, ISO-008) |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Acceptance Criteria Coverage | US-PAY-001 6/6 (AC-1..AC-6); US-PAY-002 5/5 (AC-1..AC-5) |
| Conditional/Deferred Test Cases | (1) EF-query-filter-vs-PostgreSQL-RLS: US-PAY-001 AC-6/FR-8 and US-PAY-002 AC-5/FR-8 specify PostgreSQL RLS policies on the payroll tables; this platform enforces isolation via EF Core global query filters + TenantInterceptor. TC-PAY-ISO-001/003/005/007 describe the EF mechanism and note RLS session-level assertion as an extension point if RLS policies are later added. (2) Redis/cache: US-PAY-001 list cache (TC-PAY-ISO-004, TC-PAY-001-11) and US-PAY-002 salary-preview cache (TC-PAY-ISO-008) -- assume cache available per S10; if preview is computed on-demand without caching today, TC-PAY-ISO-008 is CONDITIONAL and asserts no shared/global key is used. (3) Historical-payslip-unchanged (AC-2, TC-PAY-001-02 step 4) and BR-7 type-change-after-finalized-run are CONDITIONAL on the Payroll Run/Payslip story existing (not yet built). (4) Audit assertions (NFR-5/NFR-4) reference the Audit logging module (S24); the enqueue/record is asserted, full audit-store verification owned by the Audit module. (5) US-PAY-002 BR-4 (probation vs confirmed structure), BR-5 (Payroll Incomplete exclusion from runs), BR-6 (no backdating into a finalized run), and BR-7 (employer-side statutory in CTC) touch surfaces owned by later stories (US-PAY-006 statutory, US-PAY-007 adjustment, the Payroll Run story); BR-5 "Payroll Incomplete" flag is asserted in TC-PAY-002-01, the exclusion-from-runs is CONDITIONAL on the Payroll Run story. |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-PAY-001 | Configure Salary Structure and Components per Tenant | TC-PAY-001-01, TC-PAY-001-02, TC-PAY-001-03, TC-PAY-001-04, TC-PAY-001-05, TC-PAY-001-06, TC-PAY-001-07, TC-PAY-001-08, TC-PAY-001-09, TC-PAY-001-10, TC-PAY-001-11, TC-PAY-001-12 | 12 |
| Cross-cutting (PAY-001) | Multi-tenant isolation (salary_component / salary_structure / junction) | TC-PAY-ISO-001, TC-PAY-ISO-002, TC-PAY-ISO-003, TC-PAY-ISO-004 | 4 |
| US-PAY-002 | Assign Salary Structure to Employee | TC-PAY-002-01, TC-PAY-002-02, TC-PAY-002-03, TC-PAY-002-04, TC-PAY-002-05, TC-PAY-002-06, TC-PAY-002-07, TC-PAY-002-08, TC-PAY-002-09, TC-PAY-002-10, TC-PAY-002-11, TC-PAY-002-12 | 12 |
| Cross-cutting (PAY-002) | Multi-tenant isolation (employee_salary_component / salary_revision_history) | TC-PAY-ISO-005, TC-PAY-ISO-006, TC-PAY-ISO-007, TC-PAY-ISO-008 | 4 |

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
| TC-PAY-002-01 | Assign structure w/ CTC -> employee_salary_component rows from rules + breakdown preview before confirm (happy path) | E2E | Critical | Happy path | AC-1, FR-1, FR-2, FR-3, BR-1 |
| TC-PAY-002-02 | Component override saved while others stay calculated | Functional | High | Negative | AC-3, FR-1, FR-2, FR-3, FR-6 |
| TC-PAY-002-03 | Assigning inactive/deactivated structure rejected 400 | Functional | Critical | Negative | FR-7, FR-1 |
| TC-PAY-002-04 | Component sum != CTC beyond +/-1 tolerance rejected | Functional | High | Negative/Boundary | FR-6, FR-2, FR-3 |
| TC-PAY-002-05 | Future-dated assignment doesn't supersede current until date arrives | Functional | Critical | Negative/Boundary | AC-2, BR-1, BR-2, BR-3, FR-1, FR-4 |
| TC-PAY-002-06 | numeric(18,2) precision + CTC-derivation boundary values | Functional | High | Boundary | FR-1, FR-2, FR-3, FR-6 |
| TC-PAY-002-07 | Bulk assign to multiple employees w/ individual CTCs + progress indicator | Functional | High | Negative | AC-4, FR-5, FR-2, FR-6, BR-3 |
| TC-PAY-002-08 | Authz: only Payroll.*.All may assign; others 403 | Security | Critical | Security | AC-1, AC-2, AC-3, AC-4, FR-1, FR-3, FR-4, FR-5 |
| TC-PAY-002-09 | Revision history captures old/new structure + CTC + changed_by/at + reason (regression) | Functional | High | Negative | AC-2, FR-4, BR-3, NFR-4 |
| TC-PAY-002-10 | CTC/override/reason fields resist XSS + SQL injection; PII not leaked | Security | High | Security | FR-1, FR-4, FR-5, NFR-4, NFR-5 |
| TC-PAY-002-11 | Preview <= 500ms (NFR-2); bulk assign 500 emps <= 30s (NFR-1) | Performance | High | Performance | AC-1, AC-4, NFR-1, NFR-2, FR-3, FR-5 |
| TC-PAY-002-12 | Compensation tab + breakdown table + revision timeline + bulk spreadsheet WCAG 2.1 AA | Accessibility | High | Accessibility | AC-1, AC-2, AC-3, AC-4 |
| TC-PAY-ISO-005 | Tenant B cannot access Tenant A employee salary assignment/revisions (read iso) | Security | Critical | Multi-tenant isolation | AC-5, FR-8 |
| TC-PAY-ISO-006 | Salary APIs reject missing/invalid/mismatched tenant context | Security | Critical | Multi-tenant isolation | AC-5, FR-8 |
| TC-PAY-ISO-007 | Cross-tenant salary writes blocked; tenant_id session-derived (incl. bulk) | Security | Critical | Multi-tenant isolation | AC-5, FR-1, FR-5, FR-8 |
| TC-PAY-ISO-008 | Salary preview/breakdown caches tenant-scoped (no cross-tenant cache leak) | Security | High | Multi-tenant isolation | AC-5, FR-8, NFR-2 |

## Acceptance Criteria Coverage (US-PAY-001)

| AC | Description | Covered By |
|----|-------------|-----------|
| AC-1 | Create component saved with tenant_id, visible to this tenant only | TC-PAY-001-01, -03, -06, -07, -08, -09, -11, -12, TC-PAY-ISO-001 |
| AC-2 | Edit component -> future runs use new def; history unchanged | TC-PAY-001-02, TC-PAY-001-08 |
| AC-3 | Create structure + add components with rules | TC-PAY-001-01, -05, -06, -07, -09, -10, -12 |
| AC-4 | Reorder component processing priority | TC-PAY-001-01, TC-PAY-001-12 |
| AC-5 | Prevent delete of component in use; show affected count | TC-PAY-001-04, TC-PAY-001-08 |
| AC-6 | Tenant A sees only its components; RLS-level isolation | TC-PAY-ISO-001, TC-PAY-ISO-002, TC-PAY-ISO-003, TC-PAY-ISO-004, TC-PAY-001-03 |

## Acceptance Criteria Coverage (US-PAY-002)

| AC | Description | Covered By |
|----|-------------|-----------|
| AC-1 | Assign active structure w/ CTC -> employee_salary_component rows calculated from rules | TC-PAY-002-01, TC-PAY-002-06, TC-PAY-002-08, TC-PAY-002-11, TC-PAY-002-12 |
| AC-2 | Future-dated assignment saved; current remains active until date; revision history maintained | TC-PAY-002-05, TC-PAY-002-09, TC-PAY-002-08, TC-PAY-002-12 |
| AC-3 | Component override saved while others retain calculated values | TC-PAY-002-02, TC-PAY-002-08, TC-PAY-002-12 |
| AC-4 | Bulk assign to multiple employees w/ individual CTC + progress indicator | TC-PAY-002-07, TC-PAY-002-11, TC-PAY-002-08, TC-PAY-002-12 |
| AC-5 | Tenant B cannot access Tenant A employee salary assignment; RLS prevents cross-tenant access | TC-PAY-ISO-005, TC-PAY-ISO-006, TC-PAY-ISO-007, TC-PAY-ISO-008 |

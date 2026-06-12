---
module: Core HR
total_user_stories: 1
total_test_cases: 38
created: 2026-06-11
updated: 2026-06-11
status: draft
---

# Core HR -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 1 (US-CHR-004) |
| Total Test Cases | 38 |
| Critical Priority | 16 |
| High Priority | 16 |
| Medium Priority | 6 |
| Low Priority | 0 |
| Blocked Test Cases | 1 (TC-CHR-020, awaiting US-CHR-001) |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-CHR-004 | Create and Manage Departments | TC-CHR-001, TC-CHR-002, TC-CHR-003, TC-CHR-004, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-008, TC-CHR-009, TC-CHR-010, TC-CHR-011, TC-CHR-012, TC-CHR-013, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-017, TC-CHR-018, TC-CHR-019, TC-CHR-020, TC-CHR-021, TC-CHR-022, TC-CHR-023, TC-CHR-024, TC-CHR-025, TC-CHR-026, TC-CHR-027, TC-CHR-028, TC-CHR-029, TC-CHR-030, TC-CHR-031, TC-CHR-032, TC-CHR-033, TC-CHR-034 | 34 |
| Cross-cutting | Multi-tenant isolation (mandatory) | TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional | TC-CHR-001, TC-CHR-002, TC-CHR-003, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-008, TC-CHR-009, TC-CHR-010, TC-CHR-011, TC-CHR-012, TC-CHR-013, TC-CHR-017, TC-CHR-018, TC-CHR-020, TC-CHR-022, TC-CHR-023, TC-CHR-024, TC-CHR-030, TC-CHR-031, TC-CHR-033, TC-CHR-034 | 22 |
| Security | TC-CHR-004, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-019, TC-CHR-021, TC-CHR-025, TC-CHR-032, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 12 |
| Performance | TC-CHR-026, TC-CHR-027, TC-CHR-028 | 3 |
| Accessibility | TC-CHR-029 | 1 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-CHR-001, TC-CHR-002, TC-CHR-005, TC-CHR-008, TC-CHR-009, TC-CHR-011, TC-CHR-015, TC-CHR-020, TC-CHR-031, TC-CHR-033 | 10 |
| Negative Test | TC-CHR-003, TC-CHR-006, TC-CHR-007, TC-CHR-010, TC-CHR-012, TC-CHR-014, TC-CHR-016, TC-CHR-017, TC-CHR-021, TC-CHR-022, TC-CHR-023, TC-CHR-025, TC-CHR-032, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003 | 16 |
| Boundary Test | TC-CHR-013, TC-CHR-017, TC-CHR-018, TC-CHR-022, TC-CHR-024, TC-CHR-028 | 6 |
| Security Test | TC-CHR-004, TC-CHR-013, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-019, TC-CHR-021, TC-CHR-025, TC-CHR-031, TC-CHR-032, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 14 |
| Multi-Tenant Isolation | TC-CHR-004, TC-CHR-021, TC-CHR-025, TC-CHR-032, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 8 |
| Performance Test | TC-CHR-026, TC-CHR-027, TC-CHR-028 | 3 |
| Accessibility Test | TC-CHR-029, TC-CHR-030 | 2 |
| Cross-Browser Test | TC-CHR-030, TC-CHR-034 | 2 |

## Acceptance Criteria Coverage (US-CHR-004)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Add Department form with all fields | TC-CHR-001, TC-CHR-002, TC-CHR-017, TC-CHR-020 |
| AC-2 | Create department with tenant_id from session, unique name, appears in list and tree | TC-CHR-001, TC-CHR-002, TC-CHR-005, TC-CHR-017, TC-CHR-018, TC-CHR-032 |
| AC-3 | Duplicate name rejected within tenant; same name allowed cross-tenant | TC-CHR-003, TC-CHR-004, TC-CHR-022, TC-CHR-023 |
| AC-4 | Edit parent changes hierarchy; tree reflects new relationship; employees retained | TC-CHR-002, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-008, TC-CHR-009, TC-CHR-021, TC-CHR-024 |
| AC-5 | Deactivate blocked if active employees; warning message displayed | TC-CHR-010, TC-CHR-011, TC-CHR-012, TC-CHR-033 |

## Functional Requirements Coverage (US-CHR-004)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | CRUD scoped to current tenant | TC-CHR-001, TC-CHR-008, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-017, TC-CHR-023 | Direct |
| FR-2 | Unique department names within tenant | TC-CHR-003, TC-CHR-004, TC-CHR-018, TC-CHR-022, TC-CHR-023 | Direct |
| FR-3 | Hierarchical parent-child via parent_department_id | TC-CHR-002, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-009, TC-CHR-021, TC-CHR-024 | Direct |
| FR-4 | Assign department manager (FK to employee) | TC-CHR-020 (BLOCKED) | Blocked (US-CHR-001) |
| FR-5 | Prevent circular parent-child references | TC-CHR-006, TC-CHR-007 | Direct |
| FR-6 | Prevent deactivation with active employees | TC-CHR-010, TC-CHR-011, TC-CHR-012 | Direct |
| FR-7 | Soft delete; hidden from dropdowns, visible in admin | TC-CHR-011, TC-CHR-013, TC-CHR-033 | Direct |
| FR-8 | Display hierarchy as flat list and tree view | TC-CHR-001, TC-CHR-002, TC-CHR-005, TC-CHR-009, TC-CHR-024 | Direct |

## Non-Functional Requirements Coverage (US-CHR-004)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | API response time <= 400ms read, <= 800ms write (P95) | TC-CHR-026 | Direct |
| NFR-2 | Tenant-isolated via RLS and EF Core global query filters | TC-CHR-004, TC-CHR-021, TC-CHR-025, TC-CHR-032, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | Direct |
| NFR-3 | Fully responsive (360px to 4K) | TC-CHR-029, TC-CHR-030 | Direct |
| NFR-4 | Support 500 departments per tenant without degradation | TC-CHR-028 | Direct |
| NFR-5 | Audit log for create, update, deactivate | TC-CHR-001, TC-CHR-008, TC-CHR-011, TC-CHR-031 | Direct |

## Business Rules Coverage (US-CHR-004)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Names unique within tenant, may duplicate cross-tenant | TC-CHR-003, TC-CHR-004, TC-CHR-022 | Direct |
| BR-2 | Department can have at most one manager | TC-CHR-020 (BLOCKED) | Blocked (US-CHR-001) |
| BR-3 | Parent must belong to same tenant | TC-CHR-002, TC-CHR-009, TC-CHR-021 | Direct |
| BR-4 | Root departments form top level of org tree | TC-CHR-001, TC-CHR-005, TC-CHR-024 | Direct |
| BR-5 | Deactivated departments cannot be assigned to new employees | TC-CHR-011, TC-CHR-033 | Direct |
| BR-6 | Deleting parent requires reassigning/deactivating children | TC-CHR-012 | Direct |

## Blocked / Deferred Test Cases

| TC ID | Title | Blocked By | Reason |
|-------|-------|------------|--------|
| TC-CHR-020 | Assign department manager | US-CHR-001 | Manager references Employee entity from US-CHR-001 which is not yet built. FR-4 and BR-2 cannot be verified until employee management is implemented. |

---

*Note: This test matrix will be extended as additional Core HR user stories (US-CHR-001 through US-CHR-003, US-CHR-005+) are analyzed and test cases are authored. TC-CHR-020 should be unblocked once US-CHR-001 is delivered.*

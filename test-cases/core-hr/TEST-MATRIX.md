---
module: Core HR
total_user_stories: 2
total_test_cases: 71
created: 2026-06-11
updated: 2026-06-12
status: draft
---

# Core HR -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 2 (US-CHR-004, US-CHR-005) |
| Total Test Cases | 71 |
| Critical Priority | 26 |
| High Priority | 30 |
| Medium Priority | 15 |
| Low Priority | 0 |
| Blocked Test Cases | 4 (TC-CHR-020 on US-CHR-001; TC-CHR-043, TC-CHR-049, TC-CHR-063 on US-CHR-001) |
| Status | All Draft (blocked cases marked as blocked) |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-CHR-004 | Create and Manage Departments | TC-CHR-001, TC-CHR-002, TC-CHR-003, TC-CHR-004, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-008, TC-CHR-009, TC-CHR-010, TC-CHR-011, TC-CHR-012, TC-CHR-013, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-017, TC-CHR-018, TC-CHR-019, TC-CHR-020, TC-CHR-021, TC-CHR-022, TC-CHR-023, TC-CHR-024, TC-CHR-025, TC-CHR-026, TC-CHR-027, TC-CHR-028, TC-CHR-029, TC-CHR-030, TC-CHR-031, TC-CHR-032, TC-CHR-033, TC-CHR-034 | 34 |
| US-CHR-005 | Create and Manage Job Titles and Positions | TC-CHR-035, TC-CHR-036, TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-040, TC-CHR-041, TC-CHR-042, TC-CHR-043, TC-CHR-044, TC-CHR-045, TC-CHR-046, TC-CHR-047, TC-CHR-048, TC-CHR-049, TC-CHR-050, TC-CHR-051, TC-CHR-052, TC-CHR-053, TC-CHR-054, TC-CHR-055, TC-CHR-056, TC-CHR-057, TC-CHR-058, TC-CHR-059, TC-CHR-060, TC-CHR-061, TC-CHR-062, TC-CHR-063 | 29 |
| Cross-cutting (CHR-004) | Multi-tenant isolation (mandatory) | TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 4 |
| Cross-cutting (CHR-005) | Multi-tenant isolation (mandatory) | TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (CHR-004) | TC-CHR-001, TC-CHR-002, TC-CHR-003, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-008, TC-CHR-009, TC-CHR-010, TC-CHR-011, TC-CHR-012, TC-CHR-013, TC-CHR-017, TC-CHR-018, TC-CHR-020, TC-CHR-022, TC-CHR-023, TC-CHR-024, TC-CHR-030, TC-CHR-031, TC-CHR-033, TC-CHR-034 | 22 |
| Functional (CHR-005) | TC-CHR-035, TC-CHR-036, TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-040, TC-CHR-041, TC-CHR-043, TC-CHR-044, TC-CHR-045, TC-CHR-046, TC-CHR-047, TC-CHR-048, TC-CHR-049, TC-CHR-050, TC-CHR-056, TC-CHR-061, TC-CHR-062, TC-CHR-063 | 19 |
| Security (CHR-004) | TC-CHR-004, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-019, TC-CHR-021, TC-CHR-025, TC-CHR-032, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 12 |
| Security (CHR-005) | TC-CHR-042, TC-CHR-051, TC-CHR-052, TC-CHR-053, TC-CHR-054, TC-CHR-055, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 10 |
| Performance (CHR-004) | TC-CHR-026, TC-CHR-027, TC-CHR-028 | 3 |
| Performance (CHR-005) | TC-CHR-057, TC-CHR-058, TC-CHR-059 | 3 |
| Accessibility (CHR-004) | TC-CHR-029 | 1 |
| Accessibility (CHR-005) | TC-CHR-060 | 1 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-CHR-001, TC-CHR-002, TC-CHR-005, TC-CHR-008, TC-CHR-009, TC-CHR-011, TC-CHR-015, TC-CHR-020, TC-CHR-031, TC-CHR-033, TC-CHR-035, TC-CHR-036, TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-040, TC-CHR-048, TC-CHR-049, TC-CHR-050, TC-CHR-052, TC-CHR-056, TC-CHR-063 | 22 |
| Negative Test | TC-CHR-003, TC-CHR-006, TC-CHR-007, TC-CHR-010, TC-CHR-012, TC-CHR-014, TC-CHR-016, TC-CHR-017, TC-CHR-021, TC-CHR-022, TC-CHR-023, TC-CHR-025, TC-CHR-032, TC-CHR-041, TC-CHR-042, TC-CHR-043, TC-CHR-044, TC-CHR-045, TC-CHR-047, TC-CHR-051, TC-CHR-053, TC-CHR-054, TC-CHR-055, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 30 |
| Boundary Test | TC-CHR-013, TC-CHR-017, TC-CHR-018, TC-CHR-022, TC-CHR-024, TC-CHR-028, TC-CHR-046, TC-CHR-047, TC-CHR-059 | 9 |
| Security Test | TC-CHR-004, TC-CHR-013, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-019, TC-CHR-021, TC-CHR-025, TC-CHR-031, TC-CHR-032, TC-CHR-042, TC-CHR-051, TC-CHR-052, TC-CHR-053, TC-CHR-054, TC-CHR-055, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 24 |
| Multi-Tenant Isolation | TC-CHR-004, TC-CHR-021, TC-CHR-025, TC-CHR-032, TC-CHR-042, TC-CHR-055, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 14 |
| Performance Test | TC-CHR-026, TC-CHR-027, TC-CHR-028, TC-CHR-057, TC-CHR-058, TC-CHR-059 | 6 |
| Accessibility Test | TC-CHR-029, TC-CHR-030, TC-CHR-060, TC-CHR-061 | 4 |
| Cross-Browser Test | TC-CHR-030, TC-CHR-034, TC-CHR-061, TC-CHR-062 | 4 |

## Acceptance Criteria Coverage (US-CHR-004)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Add Department form with all fields | TC-CHR-001, TC-CHR-002, TC-CHR-017, TC-CHR-020 |
| AC-2 | Create department with tenant_id from session, unique name, appears in list and tree | TC-CHR-001, TC-CHR-002, TC-CHR-005, TC-CHR-017, TC-CHR-018, TC-CHR-032 |
| AC-3 | Duplicate name rejected within tenant; same name allowed cross-tenant | TC-CHR-003, TC-CHR-004, TC-CHR-022, TC-CHR-023 |
| AC-4 | Edit parent changes hierarchy; tree reflects new relationship; employees retained | TC-CHR-002, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-008, TC-CHR-009, TC-CHR-021, TC-CHR-024 |
| AC-5 | Deactivate blocked if active employees; warning message displayed | TC-CHR-010, TC-CHR-011, TC-CHR-012, TC-CHR-033 |

## Acceptance Criteria Coverage (US-CHR-005)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Job titles list page with columns: Title Name, Grade, Employee Count, Status, actions | TC-CHR-035, TC-CHR-036, TC-CHR-039, TC-CHR-049 |
| AC-2 | Create job title with tenant_id from session, unique title_name | TC-CHR-036, TC-CHR-038, TC-CHR-044 |
| AC-3 | Duplicate title name rejected within tenant; same name allowed cross-tenant | TC-CHR-041, TC-CHR-042, TC-CHR-045, TC-CHR-047 |
| AC-4 | Link job title to salary grade; grade displayed on employee profile | TC-CHR-037, TC-CHR-063 (BLOCKED) |
| AC-5 | Deactivate blocked when assigned to active employees; warning message | TC-CHR-040, TC-CHR-043 (BLOCKED) |

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

## Functional Requirements Coverage (US-CHR-005)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | CRUD operations on job titles scoped to current tenant | TC-CHR-035, TC-CHR-036, TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-040, TC-CHR-044, TC-CHR-045, TC-CHR-051, TC-CHR-052, TC-CHR-053 | Direct |
| FR-2 | Unique title_name within tenant | TC-CHR-036, TC-CHR-041, TC-CHR-042, TC-CHR-045, TC-CHR-046, TC-CHR-047 | Direct |
| FR-3 | Optionally link job title to salary grade (grade_id FK, nullable) | TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-063 (BLOCKED) | Direct (grade link testable; employee profile display blocked) |
| FR-4 | Display count of employees assigned to each job title | TC-CHR-049 (BLOCKED) | Blocked (US-CHR-001) |
| FR-5 | Soft delete; deactivated hidden from assignment dropdowns, visible in admin | TC-CHR-040, TC-CHR-048 | Direct |
| FR-6 | Employment types (Full-Time, Part-Time, Contract, Intern) as reference entity | TC-CHR-050 | Direct |
| FR-7 | Prevent deactivation of job titles with active employee assignments | TC-CHR-043 (BLOCKED) | Blocked (US-CHR-001) |

## Non-Functional Requirements Coverage (US-CHR-004)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | API response time <= 400ms read, <= 800ms write (P95) | TC-CHR-026 | Direct |
| NFR-2 | Tenant-isolated via RLS and EF Core global query filters | TC-CHR-004, TC-CHR-021, TC-CHR-025, TC-CHR-032, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | Direct |
| NFR-3 | Fully responsive (360px to 4K) | TC-CHR-029, TC-CHR-030 | Direct |
| NFR-4 | Support 500 departments per tenant without degradation | TC-CHR-028 | Direct |
| NFR-5 | Audit log for create, update, deactivate | TC-CHR-001, TC-CHR-008, TC-CHR-011, TC-CHR-031 | Direct |

## Non-Functional Requirements Coverage (US-CHR-005)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Job title CRUD API response time <= 400ms read, <= 800ms write (P95) | TC-CHR-057, TC-CHR-058 | Direct |
| NFR-2 | All job title data tenant-isolated via RLS and EF Core global query filters | TC-CHR-042, TC-CHR-054, TC-CHR-055, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | Direct |
| NFR-3 | Management page fully responsive (360px to 4K) | TC-CHR-060, TC-CHR-061, TC-CHR-062 | Direct |
| NFR-4 | Audit log entries for all create, update, deactivate operations | TC-CHR-036, TC-CHR-039, TC-CHR-040, TC-CHR-056 | Direct |

## Business Rules Coverage (US-CHR-004)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Names unique within tenant, may duplicate cross-tenant | TC-CHR-003, TC-CHR-004, TC-CHR-022 | Direct |
| BR-2 | Department can have at most one manager | TC-CHR-020 (BLOCKED) | Blocked (US-CHR-001) |
| BR-3 | Parent must belong to same tenant | TC-CHR-002, TC-CHR-009, TC-CHR-021 | Direct |
| BR-4 | Root departments form top level of org tree | TC-CHR-001, TC-CHR-005, TC-CHR-024 | Direct |
| BR-5 | Deactivated departments cannot be assigned to new employees | TC-CHR-011, TC-CHR-033 | Direct |
| BR-6 | Deleting parent requires reassigning/deactivating children | TC-CHR-012 | Direct |

## Business Rules Coverage (US-CHR-005)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Job title names unique within tenant, may duplicate cross-tenant | TC-CHR-041, TC-CHR-042, TC-CHR-045, TC-CHR-047 | Direct |
| BR-2 | A job title can exist without a linked grade | TC-CHR-038, TC-CHR-039 | Direct |
| BR-3 | Deactivated titles cannot be assigned to new employees but remain on existing records | TC-CHR-040, TC-CHR-043 (BLOCKED), TC-CHR-048 | Direct (assignment test blocked) |
| BR-4 | Job titles are tenant-specific master data; no system-wide predefined titles | TC-CHR-036, TC-CHR-042, TC-CHR-055, TC-CHR-ISO-005 | Direct |
| BR-5 | Grades, if used, are also tenant-specific entities | TC-CHR-037 | Direct |

## Blocked / Deferred Test Cases

| TC ID | Title | Blocked By | Reason |
|-------|-------|------------|--------|
| TC-CHR-020 | Assign department manager | US-CHR-001 | Manager references Employee entity from US-CHR-001 which is not yet built. FR-4 and BR-2 cannot be verified until employee management is implemented. |
| TC-CHR-043 | Deactivate job title blocked when assigned to active employees | US-CHR-001 | AC-5 and FR-7 require employees to be assignable to job titles. Cannot be executed until US-CHR-001 is delivered. |
| TC-CHR-049 | Employee count badge displays correct count per job title | US-CHR-001 | FR-4 requires employees to be assignable to job titles for the count to be meaningful. Also depends on US-CHR-003 (employee directory). |
| TC-CHR-063 | Grade linked to job title displayed on employee profile | US-CHR-001 | AC-4 (employee profile display portion) requires Employee entity and employee profile pages from US-CHR-001. |

---

*Note: This test matrix will be extended as additional Core HR user stories (US-CHR-001 through US-CHR-003, US-CHR-006+) are analyzed and test cases are authored. Blocked test cases (TC-CHR-020, TC-CHR-043, TC-CHR-049, TC-CHR-063) should be unblocked once US-CHR-001 is delivered.*

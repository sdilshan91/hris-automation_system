---
module: Core HR
total_user_stories: 3
total_test_cases: 115
created: 2026-06-11
updated: 2026-06-12
status: draft
---

# Core HR -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 3 (US-CHR-001, US-CHR-004, US-CHR-005) |
| Total Test Cases | 115 |
| Critical Priority | 42 |
| High Priority | 50 |
| Medium Priority | 23 |
| Low Priority | 0 |
| Blocked Test Cases | 0 (previously 4 -- all unblocked by US-CHR-001) |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-CHR-001 | Add New Employee with Personal Information | TC-CHR-064, TC-CHR-065, TC-CHR-066, TC-CHR-067, TC-CHR-068, TC-CHR-069, TC-CHR-070, TC-CHR-071, TC-CHR-072, TC-CHR-073, TC-CHR-074, TC-CHR-075, TC-CHR-076, TC-CHR-077, TC-CHR-078, TC-CHR-079, TC-CHR-080, TC-CHR-081, TC-CHR-082, TC-CHR-083, TC-CHR-084, TC-CHR-085, TC-CHR-086, TC-CHR-087, TC-CHR-088, TC-CHR-089, TC-CHR-090, TC-CHR-091, TC-CHR-092, TC-CHR-093, TC-CHR-094, TC-CHR-095, TC-CHR-096, TC-CHR-097, TC-CHR-098, TC-CHR-099, TC-CHR-100, TC-CHR-101, TC-CHR-102, TC-CHR-103 | 40 |
| US-CHR-004 | Create and Manage Departments | TC-CHR-001 through TC-CHR-034 | 34 |
| US-CHR-005 | Create and Manage Job Titles and Positions | TC-CHR-035 through TC-CHR-063 | 29 |
| Cross-cutting (CHR-004) | Multi-tenant isolation (mandatory) | TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 4 |
| Cross-cutting (CHR-005) | Multi-tenant isolation (mandatory) | TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 4 |
| Cross-cutting (CHR-001) | Multi-tenant isolation (mandatory) | TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (CHR-001) | TC-CHR-064, TC-CHR-065, TC-CHR-066, TC-CHR-075, TC-CHR-076, TC-CHR-077, TC-CHR-078, TC-CHR-079, TC-CHR-080, TC-CHR-081, TC-CHR-082, TC-CHR-084, TC-CHR-085, TC-CHR-086, TC-CHR-087, TC-CHR-088, TC-CHR-089, TC-CHR-090, TC-CHR-098, TC-CHR-099, TC-CHR-101, TC-CHR-102, TC-CHR-103 | 23 |
| Functional (CHR-004) | TC-CHR-001, TC-CHR-002, TC-CHR-003, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-008, TC-CHR-009, TC-CHR-010, TC-CHR-011, TC-CHR-012, TC-CHR-013, TC-CHR-017, TC-CHR-018, TC-CHR-020, TC-CHR-022, TC-CHR-023, TC-CHR-024, TC-CHR-030, TC-CHR-031, TC-CHR-033, TC-CHR-034 | 22 |
| Functional (CHR-005) | TC-CHR-035, TC-CHR-036, TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-040, TC-CHR-041, TC-CHR-043, TC-CHR-044, TC-CHR-045, TC-CHR-046, TC-CHR-047, TC-CHR-048, TC-CHR-049, TC-CHR-050, TC-CHR-056, TC-CHR-061, TC-CHR-062, TC-CHR-063 | 19 |
| Security (CHR-001) | TC-CHR-067, TC-CHR-068, TC-CHR-071, TC-CHR-072, TC-CHR-073, TC-CHR-083, TC-CHR-091, TC-CHR-092, TC-CHR-093, TC-CHR-094, TC-CHR-100, TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | 15 |
| Security (CHR-004) | TC-CHR-004, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-019, TC-CHR-021, TC-CHR-025, TC-CHR-032, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 12 |
| Security (CHR-005) | TC-CHR-042, TC-CHR-051, TC-CHR-052, TC-CHR-053, TC-CHR-054, TC-CHR-055, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 10 |
| Performance (CHR-001) | TC-CHR-095, TC-CHR-096 | 2 |
| Performance (CHR-004) | TC-CHR-026, TC-CHR-027, TC-CHR-028 | 3 |
| Performance (CHR-005) | TC-CHR-057, TC-CHR-058, TC-CHR-059 | 3 |
| Accessibility (CHR-001) | TC-CHR-097 | 1 |
| Accessibility (CHR-004) | TC-CHR-029 | 1 |
| Accessibility (CHR-005) | TC-CHR-060 | 1 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-CHR-001, TC-CHR-002, TC-CHR-005, TC-CHR-008, TC-CHR-009, TC-CHR-011, TC-CHR-015, TC-CHR-020, TC-CHR-031, TC-CHR-033, TC-CHR-035, TC-CHR-036, TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-040, TC-CHR-048, TC-CHR-049, TC-CHR-050, TC-CHR-052, TC-CHR-056, TC-CHR-063, TC-CHR-064, TC-CHR-065, TC-CHR-066, TC-CHR-068, TC-CHR-069, TC-CHR-075, TC-CHR-076, TC-CHR-079, TC-CHR-080, TC-CHR-081, TC-CHR-082, TC-CHR-084, TC-CHR-101, TC-CHR-102, TC-CHR-103 | 37 |
| Negative Test | TC-CHR-003, TC-CHR-006, TC-CHR-007, TC-CHR-010, TC-CHR-012, TC-CHR-014, TC-CHR-016, TC-CHR-017, TC-CHR-021, TC-CHR-022, TC-CHR-023, TC-CHR-025, TC-CHR-032, TC-CHR-041, TC-CHR-042, TC-CHR-043, TC-CHR-044, TC-CHR-045, TC-CHR-047, TC-CHR-051, TC-CHR-053, TC-CHR-054, TC-CHR-055, TC-CHR-067, TC-CHR-070, TC-CHR-071, TC-CHR-072, TC-CHR-073, TC-CHR-074, TC-CHR-077, TC-CHR-078, TC-CHR-083, TC-CHR-084, TC-CHR-085, TC-CHR-086, TC-CHR-087, TC-CHR-088, TC-CHR-089, TC-CHR-090, TC-CHR-091, TC-CHR-092, TC-CHR-093, TC-CHR-094, TC-CHR-103, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008, TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | 55 |
| Boundary Test | TC-CHR-013, TC-CHR-017, TC-CHR-018, TC-CHR-022, TC-CHR-024, TC-CHR-028, TC-CHR-046, TC-CHR-047, TC-CHR-059, TC-CHR-070, TC-CHR-074, TC-CHR-077, TC-CHR-078, TC-CHR-086, TC-CHR-088, TC-CHR-089 | 16 |
| Security Test | TC-CHR-004, TC-CHR-013, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-019, TC-CHR-021, TC-CHR-025, TC-CHR-031, TC-CHR-032, TC-CHR-042, TC-CHR-051, TC-CHR-052, TC-CHR-053, TC-CHR-054, TC-CHR-055, TC-CHR-068, TC-CHR-071, TC-CHR-072, TC-CHR-073, TC-CHR-083, TC-CHR-087, TC-CHR-091, TC-CHR-092, TC-CHR-093, TC-CHR-094, TC-CHR-100, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008, TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | 39 |
| Multi-Tenant Isolation | TC-CHR-004, TC-CHR-021, TC-CHR-025, TC-CHR-032, TC-CHR-042, TC-CHR-055, TC-CHR-066, TC-CHR-068, TC-CHR-069, TC-CHR-073, TC-CHR-083, TC-CHR-084, TC-CHR-087, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008, TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | 25 |
| Performance Test | TC-CHR-026, TC-CHR-027, TC-CHR-028, TC-CHR-057, TC-CHR-058, TC-CHR-059, TC-CHR-095, TC-CHR-096 | 8 |
| Accessibility Test | TC-CHR-029, TC-CHR-030, TC-CHR-060, TC-CHR-061, TC-CHR-097 | 5 |
| Cross-Browser Test | TC-CHR-030, TC-CHR-034, TC-CHR-061, TC-CHR-062, TC-CHR-098, TC-CHR-099 | 6 |

## Acceptance Criteria Coverage (US-CHR-001)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Multi-step wizard with all sections | TC-CHR-064, TC-CHR-097, TC-CHR-098, TC-CHR-101 |
| AC-2 | Create with mandatory fields, status active, auto employee_no, tenant_id from session | TC-CHR-065, TC-CHR-066, TC-CHR-076, TC-CHR-079, TC-CHR-081, TC-CHR-083, TC-CHR-085 |
| AC-3 | Duplicate email same tenant rejected; same email allowed cross-tenant | TC-CHR-067, TC-CHR-068 |
| AC-4 | Profile photo upload, tenant-isolated storage, EXIF stripped, signed URL | TC-CHR-069, TC-CHR-070, TC-CHR-071, TC-CHR-072, TC-CHR-073 |
| AC-5 | Plan employee limit reached, creation blocked | TC-CHR-074 |
| AC-6 | Custom fields persisted to JSONB and shown on profile | TC-CHR-075, TC-CHR-084 |

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
| AC-4 | Link job title to salary grade; grade displayed on employee profile | TC-CHR-037, TC-CHR-063 (UNBLOCKED by US-CHR-001) |
| AC-5 | Deactivate blocked when assigned to active employees; warning message | TC-CHR-040, TC-CHR-043 (UNBLOCKED by US-CHR-001) |

## Functional Requirements Coverage (US-CHR-001)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | Multi-step form with all sections | TC-CHR-064, TC-CHR-097 | Direct |
| FR-2 | Auto-generate unique employee_no with configurable pattern, tenant-isolated sequence | TC-CHR-065, TC-CHR-066, TC-CHR-076 | Direct |
| FR-3 | Email uniqueness within tenant scope | TC-CHR-067, TC-CHR-068 | Direct |
| FR-4 | tenant_id from session context, never from user input | TC-CHR-065, TC-CHR-083 | Direct |
| FR-5 | Plan-level employee count limits enforced | TC-CHR-074 | Direct |
| FR-6 | Profile photo upload with MIME validation, max 5 MB, EXIF stripping | TC-CHR-069, TC-CHR-070, TC-CHR-071, TC-CHR-072, TC-CHR-073 | Direct |
| FR-7 | Audit columns (created_at, created_by, updated_at, updated_by) auto-populated | TC-CHR-081 | Direct |
| FR-8 | Optional user_id FK for self-service portal | TC-CHR-082 | Direct |
| FR-9 | Tenant-configured custom fields rendered dynamically | TC-CHR-075, TC-CHR-084 | Direct |

## Functional Requirements Coverage (US-CHR-004)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | CRUD scoped to current tenant | TC-CHR-001, TC-CHR-008, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-017, TC-CHR-023 | Direct |
| FR-2 | Unique department names within tenant | TC-CHR-003, TC-CHR-004, TC-CHR-018, TC-CHR-022, TC-CHR-023 | Direct |
| FR-3 | Hierarchical parent-child via parent_department_id | TC-CHR-002, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-009, TC-CHR-021, TC-CHR-024 | Direct |
| FR-4 | Assign department manager (FK to employee) | TC-CHR-020 (UNBLOCKED by US-CHR-001) | Direct |
| FR-5 | Prevent circular parent-child references | TC-CHR-006, TC-CHR-007 | Direct |
| FR-6 | Prevent deactivation with active employees | TC-CHR-010, TC-CHR-011, TC-CHR-012 | Direct |
| FR-7 | Soft delete; hidden from dropdowns, visible in admin | TC-CHR-011, TC-CHR-013, TC-CHR-033 | Direct |
| FR-8 | Display hierarchy as flat list and tree view | TC-CHR-001, TC-CHR-002, TC-CHR-005, TC-CHR-009, TC-CHR-024 | Direct |

## Functional Requirements Coverage (US-CHR-005)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | CRUD operations on job titles scoped to current tenant | TC-CHR-035, TC-CHR-036, TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-040, TC-CHR-044, TC-CHR-045, TC-CHR-051, TC-CHR-052, TC-CHR-053 | Direct |
| FR-2 | Unique title_name within tenant | TC-CHR-036, TC-CHR-041, TC-CHR-042, TC-CHR-045, TC-CHR-046, TC-CHR-047 | Direct |
| FR-3 | Optionally link job title to salary grade (grade_id FK, nullable) | TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-063 (UNBLOCKED) | Direct |
| FR-4 | Display count of employees assigned to each job title | TC-CHR-049 (UNBLOCKED by US-CHR-001) | Direct |
| FR-5 | Soft delete; deactivated hidden from assignment dropdowns, visible in admin | TC-CHR-040, TC-CHR-048 | Direct |
| FR-6 | Employment types (Full-Time, Part-Time, Contract, Intern) as reference entity | TC-CHR-050 | Direct |
| FR-7 | Prevent deactivation of job titles with active employee assignments | TC-CHR-043 (UNBLOCKED by US-CHR-001) | Direct |

## Non-Functional Requirements Coverage (US-CHR-001)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Employee creation API response time <= 800 ms (P95) | TC-CHR-095 | Direct |
| NFR-2 | All employee data tenant-isolated via RLS and EF global query filters | TC-CHR-068, TC-CHR-073, TC-CHR-083, TC-CHR-087, TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | Direct |
| NFR-3 | Profile photo upload scanned for malware (ClamAV) | TC-CHR-072 | Direct |
| NFR-4 | Form fully responsive from 360px to 4K | TC-CHR-098, TC-CHR-099 | Direct |
| NFR-5 | Form meets WCAG 2.1 AA accessibility standards | TC-CHR-097 | Direct |
| NFR-6 | PII fields logged in audit trail when accessed | TC-CHR-100 | Direct |

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

## Business Rules Coverage (US-CHR-001)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | employee_no unique within tenant, may repeat cross-tenant | TC-CHR-065, TC-CHR-066 | Direct |
| BR-2 | email unique within tenant, may repeat cross-tenant | TC-CHR-067, TC-CHR-068 | Direct |
| BR-3 | Default status on creation is "active" unless explicitly "probation" | TC-CHR-079 | Direct |
| BR-4 | date_of_joining cannot be more than 90 days in the future | TC-CHR-077 | Direct |
| BR-5 | Emergency contact recommended but not mandatory on creation | TC-CHR-102 | Direct |
| BR-6 | Soft delete (is_deleted = true); never hard-deleted via UI | TC-CHR-080 | Direct |

## Business Rules Coverage (US-CHR-004)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Names unique within tenant, may duplicate cross-tenant | TC-CHR-003, TC-CHR-004, TC-CHR-022 | Direct |
| BR-2 | Department can have at most one manager | TC-CHR-020 (UNBLOCKED by US-CHR-001) | Direct |
| BR-3 | Parent must belong to same tenant | TC-CHR-002, TC-CHR-009, TC-CHR-021 | Direct |
| BR-4 | Root departments form top level of org tree | TC-CHR-001, TC-CHR-005, TC-CHR-024 | Direct |
| BR-5 | Deactivated departments cannot be assigned to new employees | TC-CHR-011, TC-CHR-033 | Direct |
| BR-6 | Deleting parent requires reassigning/deactivating children | TC-CHR-012 | Direct |

## Business Rules Coverage (US-CHR-005)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Job title names unique within tenant, may duplicate cross-tenant | TC-CHR-041, TC-CHR-042, TC-CHR-045, TC-CHR-047 | Direct |
| BR-2 | A job title can exist without a linked grade | TC-CHR-038, TC-CHR-039 | Direct |
| BR-3 | Deactivated titles cannot be assigned to new employees but remain on existing records | TC-CHR-040, TC-CHR-043 (UNBLOCKED), TC-CHR-048 | Direct |
| BR-4 | Job titles are tenant-specific master data; no system-wide predefined titles | TC-CHR-036, TC-CHR-042, TC-CHR-055, TC-CHR-ISO-005 | Direct |
| BR-5 | Grades, if used, are also tenant-specific entities | TC-CHR-037, TC-CHR-063 (UNBLOCKED) | Direct |

## Unblocked Test Cases (previously blocked on US-CHR-001)

| TC ID | Title | Previously Blocked By | Unblocked Date | Notes |
|-------|-------|-----------------------|----------------|-------|
| TC-CHR-020 | Assign department manager | US-CHR-001 | 2026-06-12 | Manager references Employee entity which now exists. FR-4 and BR-2 can be verified. |
| TC-CHR-043 | Deactivate job title blocked when assigned to active employees | US-CHR-001 | 2026-06-12 | AC-5 and FR-7 can now be tested with real employee assignments. |
| TC-CHR-049 | Employee count badge displays correct count per job title | US-CHR-001 | 2026-06-12 | FR-4 can now show real counts. Clickable badge to employee directory still depends on US-CHR-003. |
| TC-CHR-063 | Grade linked to job title displayed on employee profile | US-CHR-001 | 2026-06-12 | AC-4 end-to-end flow can be tested. Grade entity (Payroll) may still be partially deferred. |

---

## Coverage Summary (US-CHR-001)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 6/6 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 9/9 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 11 (4 dedicated ISO + 7 embedded) | >= 3 | PASS |
| Security Test Cases | 15/44 (34.1%) | >= 30% | PASS |
| Performance Test Cases | 2/44 | >= 1 | PASS |
| Accessibility Test Cases | 1/44 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/44 | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

## Coverage Summary (US-CHR-004)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 8/8 (100%) -- FR-4 now unblocked | >= 85% | PASS |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) -- BR-2 now unblocked | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 8 (4 dedicated + 4 embedded) | >= 3 | PASS |
| Security Test Cases | 12/38 (31.6%) | >= 30% | PASS |
| Blocked Test Cases | 0 (TC-CHR-020 unblocked) | -- | CLEAR |

## Coverage Summary (US-CHR-005)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-4 and FR-7 now unblocked | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 10 (4 dedicated + 6 embedded) | >= 3 | PASS |
| Security Test Cases | 10/33 (30.3%) | >= 30% | PASS |
| Blocked Test Cases | 0 (TC-CHR-043, TC-CHR-049, TC-CHR-063 unblocked) | -- | CLEAR |

---

*Note: This test matrix covers US-CHR-001, US-CHR-004, and US-CHR-005. It will be extended as additional Core HR user stories (US-CHR-002, US-CHR-003, US-CHR-006+) are analyzed and test cases are authored. All previously blocked test cases have been unblocked by the delivery of US-CHR-001.*

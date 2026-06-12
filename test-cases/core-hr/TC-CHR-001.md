---
id: TC-CHR-001
user_story: US-CHR-004
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-001: Create a root department successfully (happy path)

## 1. Test Objective
Verify that a Tenant Admin can create a new root department (no parent) with valid data and that the department is persisted with the correct tenant_id, appears in the department list, and an audit log entry is created.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-8
- Non-Functional Requirements: NFR-5
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- No department named "Engineering" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Department Name | Engineering | Required, unique within tenant |
| Parent Department | (none) | Root department |
| Description | Core engineering division | Optional |
| Status | Active | Default |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page at `https://acme.yourhrm.com/departments` | Department list page loads with card-based table showing existing departments (or empty state). |
| 2 | Click the "Add Department" button (top-right, `+` icon) | A slide-over panel (or modal card) appears with smooth slide-in animation (300ms ease-out) containing fields: Department Name (required), Parent Department (optional dropdown), Department Manager (optional employee picker), Description, and Status. |
| 3 | Enter "Engineering" in the Department Name field | Field accepts the input; no validation error displayed. |
| 4 | Leave Parent Department as empty/none | Field shows placeholder or "None (root department)". |
| 5 | Enter "Core engineering division" in the Description field | Field accepts the input. |
| 6 | Click "Save" / "Create" button | Loading indicator appears; button is disabled to prevent double-submit. |
| 7 | Observe API call `POST /api/v1/departments` with body `{ name: "Engineering", description: "Core engineering division" }` | Request is sent with `X-Tenant-Subdomain: acme` header (or subdomain-based routing). Response status is 201 Created. |
| 8 | Verify response body contains the new department with `department_id` (UUID), `tenant_id` matching acme's tenant ID, `name: "Engineering"`, `is_active: true`, `parent_department_id: null` | All fields present and correct. |
| 9 | Verify the department appears in the department list table | "Engineering" row is visible with Name, Parent (empty/"-"), Manager (empty/"-"), Employee Count (0), Status (Active). |
| 10 | Toggle to tree view | "Engineering" appears as a root node at the top level of the hierarchy tree. |
| 11 | Verify an audit log entry exists for the create operation | Audit record contains `action: department_created`, `entity_id` matching the new department_id, `tenant_id`, `user_id`, and timestamp. |

## 6. Postconditions
- A new department record exists in the `department` table with `tenant_id` set from session context.
- `is_active` is `true`, `is_deleted` is `false`.
- `created_at` and `created_by` are populated.
- An audit log entry of type `department_created` has been recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

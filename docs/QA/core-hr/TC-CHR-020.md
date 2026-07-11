---
id: TC-CHR-020
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: fail
created: 2026-06-11
updated: 2026-07-04
unblocked_by: US-CHR-001
regression_for: BUG-014
---

# TC-CHR-020: Assign department manager

## 1. Test Objective
Verify that a department can have a manager assigned via the `manager_employee_id` FK to the employee table, and that the manager field displays the employee's name and avatar in the department list and form. Previously BLOCKED on US-CHR-001 -- now unblocked.

**Regression scope (BUG-014, HIGH):** additionally verify that `managerId` is **tenant-validated** on both the create and update paths. A `managerId` that belongs to **another tenant's** employee (or does not exist) must be rejected with **400** — mirroring the existing `parentDepartmentId` validation (BR-2 / FR-4). Pre-fix, the cross-tenant `managerId` was accepted and persisted with HTTP 200, leaking a foreign-tenant employee reference (name/avatar) into the wrong tenant's UI.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-1 (Department Manager optional employee picker)
- Functional Requirements: FR-4
- Business Rules: BR-2 (manager must be an employee in the same tenant)
- Dependencies: US-CHR-001 (Employees) -- now available
- Defect: BUG-014 (Department `managerId` not tenant-validated; cross-tenant FK accepted)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Department "Engineering" exists.
- At least two employee records exist in "acme" tenant: "Jane Smith" and "Bob Wilson" (created via US-CHR-001 employee creation flow).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Engineering | Existing department |
| Manager Employee 1 | Jane Smith (employee_id: UUID) | Active employee in same tenant |
| Manager Employee 2 | Bob Wilson (employee_id: UUID) | Active employee in same tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments management page | Department list loads. |
| 2 | Click Edit on "Engineering" | Edit form opens. |
| 3 | Open the Department Manager employee picker field | Searchable employee autocomplete appears with avatar + name display (per UI/UX notes). Only active employees from the current tenant are listed. |
| 4 | Search for and select "Jane Smith" | Employee is selected; avatar and name are displayed in the field. |
| 5 | Click "Save" | API call `PUT /api/v1/tenant/departments/{id}` with `manager_employee_id` set to Jane's employee_id. Response is 200 OK. |
| 6 | Verify the department list shows "Jane Smith" in the Manager column for "Engineering" | Manager name (and optionally avatar) is displayed. |
| 7 | Verify database: `manager_employee_id` references Jane's employee record in the same tenant | FK is valid; employee belongs to the same tenant. |
| 8 | Edit "Engineering" again and change the manager to "Bob Wilson" | Manager is updated. Verify the old manager (Jane) is replaced. BR-2: At most one manager per department. |
| 9 | Verify the department list now shows "Bob Wilson" as manager | Manager column updated. |
| 10 | Clear the manager field and save | `manager_employee_id` is set to null. Manager column shows "-" or empty. |
| 11 | Verify database: `manager_employee_id` is null | Nullable FK confirmed. |

### 5a. BUG-014 regression arm — cross-tenant manager rejected (Multi-tenant isolation)
Preconditions: a real active employee **E_B** exists in tenant B; the acting admin operates in tenant A with department **D_A** (initially managed by same-tenant employee **E_A**).

| Step | Action | Expected Result |
|------|--------|-----------------|
| R1 | As tenant A, `PUT /api/v1/tenant/departments/{D_A}` with `managerId = E_A` (same tenant) | **200 OK**; `manager_id = E_A` persisted (control passes). |
| R2 | As tenant A, `PUT …/departments/{D_A}` with `managerId = E_B` (tenant B's employee) | **400** rejected (BR-2/FR-4); `manager_id` **unchanged** (still `E_A`, no cross-tenant FK written). |
| R3 | As tenant A, `POST …/departments` with `managerId = E_B` | **400** rejected; department not created with a foreign-tenant manager. |
| R4 | As tenant A, `PUT …/departments/{D_A}` with `managerId = null` | **200 OK**; manager cleared (clearing remains allowed). |

## 6. Postconditions
- Department manager assignment works correctly.
- At most one manager per department (BR-2).
- Manager field is nullable and can be cleared.
- `managerId` is tenant-validated: only a same-tenant employee is accepted; a cross-tenant or non-existent employee is rejected with 400 (BUG-014 closed).

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Automated Regression (BUG-014)
xUnit unit tests (EF InMemory, honours the tenant global query filter) in
`src/backend/HRM.Tests/Unit/DepartmentManagerTenantValidationTests.cs`:
- `Update_CrossTenantManagerId_IsRejected` — arm R2 (trigger; fails pre-fix, passes post-fix).
- `Update_SameTenantManagerId_Succeeds` — arm R1 (control; passes both pre- and post-fix).
- `Update_NullManagerId_IsAllowed` — arm R4 (clearing stays allowed).
- `Create_CrossTenantManagerId_IsRejected` / `Create_SameTenantManagerId_Succeeds` — arm R3 create-path trigger + control.

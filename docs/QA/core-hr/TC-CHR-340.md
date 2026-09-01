---
id: TC-CHR-340
user_story: US-CHR-004
module: Core HR
priority: medium
type: functional
status: automated
created: 2026-09-02
automated: 2026-09-02
defect:
  - ISSUE-364
---

# TC-CHR-340: Department list returns the manager display name and the ACTIVE employee count — both batched, both rendered in the list and the tree (ISSUE-364)

## 1. Test Objective
Verify that `DepartmentDto` carries the two denormalized display fields the department surfaces have always
claimed to show: **`ManagerName`** and **`EmployeeCount`** (active employees only).

`DepartmentDto` returned neither, while the FE model invented both. The result: the department list rendered
"undefined employees" and a permanently blank manager line, and the tree view — which builds from the same
flat list — inherited both blanks. Both sibling DTOs (`JobTitleDto`, `LocationDto`) already returned a count,
which is what made departments the inconsistent one.

The fix adds `ManagerName` (`DepartmentDto.cs:21`) and `EmployeeCount` (`:29`), populated with **two batched
queries** in `DepartmentService.GetAllAsync` (`DepartmentService.cs:306-323`) — a GroupBy count keyed by
department and a single manager-name lookup keyed by the distinct manager ids — then projected in `ToDto`
(`:419-420`). Batching is load-bearing: doing either per row would turn a department list into an N+1.

## 2. Related Requirements
- User Story: US-CHR-004 (Create and Manage Departments)
- Acceptance Criteria / Requirements:
  - **FR-8** — the hierarchy is displayed as **both** a flat list/table and a tree view; both are fed by the
    same flat department payload, so a missing field blanks out both surfaces.
  - **§8 UI/UX Notes** — the department list card table specifies the columns **Name, Parent, Manager,
    Employee Count, Status**; `ManagerName` and `EmployeeCount` are the two that had no backing field.
  - **BR-2** — a department has at most one manager (single `ManagerId` → single resolved name).
  - **AC-5 (display half only)** — the active-employee count is the number a user needs to see before
    deactivating a department. The **block itself is enforced server-side** by `DepartmentService`; this TC
    does not assert the block. See `department-list.component.spec.ts`
    ("delegates the AC-5 active-employee check to the server rather than blocking locally", GAP-014) — the
    client-side block was removed precisely because it only ever passed on a fixture that supplied an
    `employeeCount` the DTO did not return.
- Finding: **ISSUE-364** (department list/tree showed no manager name and no employee count)

## 3. Preconditions
- `DepartmentService` under a tenant-resolved `ITenantContext` with an EF Core InMemory database
  (`DepartmentServiceTests` harness), or the running stack with a signed-in Tenant Admin / HR Officer.
- At least one department; employees seeded with mixed `IsActive` and mixed department assignment.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department `ENG` | "Engineering" | 2 active employees + 1 inactive + 1 employee in another department |
| Department `MGD` | "Managed", `ManagerId` = Jane Smith | manager resolution case |
| Department `UNM` | "Unmanaged", no `ManagerId`, no employees | null/zero boundary |
| Manager employee | `FirstName` "Jane", `LastName` "Smith" | expected `ManagerName` = `"Jane Smith"` |
| Inactive employee | `IsActive = false`, department `ENG` | must NOT be counted |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Call `DepartmentService.GetAllAsync()` with `ENG` holding 2 active employees, 1 inactive employee, and a fourth employee in a different department. | `ENG.EmployeeCount == 2` — only **active** employees, and only those of **this** department. | `DepartmentServiceTests.GetAll_returns_the_ACTIVE_employee_count_per_department_issue364` (`:680`) |
| 2 | Call `GetAllAsync()` for a department whose `ManagerId` points at employee "Jane Smith". | `MGD.ManagerName == "Jane Smith"` (first + last name), resolved from the batched manager lookup. | `DepartmentServiceTests.GetAll_returns_the_manager_display_name_issue364` (`:695`) |
| 3 | **Boundary:** call `GetAllAsync()` for a department with no manager and no employees. | `UNM.ManagerName == null` and `UNM.EmployeeCount == 0` — an unset manager is null, never a fabricated placeholder; an empty department is 0, never `undefined`. | `DepartmentServiceTests.GetAll_leaves_manager_name_null_when_no_manager_is_set_issue364` (`:706`) |
| 4 | Inspect the query shape used to populate both fields for a list of N departments. | Exactly **two** additional queries regardless of N: one `GroupBy(DepartmentId).Count()` over active employees, one `Where(managerIds.Contains(e.Id))` name projection — never per row. | Code-verified: `DepartmentService.cs:306-323` (batched, mirroring `JobTitleService.GetAllAsync`). No dedicated query-count assertion exists — see the coverage note below. |
| 5 | **FE render (list):** load the Departments page with the departments above. | Each department card shows `{n} employee` / `{n} employees` (0 when absent, never "undefined") and the manager name, falling back to an em dash when there is no manager. | Code-verified: `department-list.component.ts:249` (`{{ dept.employeeCount ?? 0 }} …`) and `:255` (`{{ dept.managerName \|\| '—' }}`). No dedicated Karma render arm — see the coverage note below. |
| 6 | **FE render (tree):** toggle to the tree view. | The tree is built from the **same flat department list** (`buildTree(this.departments())`), so each node carries the same populated `managerName` / `employeeCount` — the two views cannot disagree. | Code-verified: `department-tree.component.ts:256` (`treeNodes` computed from `departments()`), `:295` (`buildTree`). |

## 6. Postconditions
- `GET` department list responses carry a non-null `employeeCount` (active-only) for every department and a
  `managerName` for every department that has a manager.
- The department list and the department tree both render a real manager and a real headcount; neither shows
  "undefined employees" nor a blank manager line.

## 7. Test Category Tags
- [x] Happy path (count and manager name populated)
- [x] Boundary test (no manager → null; no employees → 0; inactive employee excluded; employee of another department excluded)
- [ ] Negative test
- [ ] Security test
- [ ] Multi-tenant isolation — the batched lookups run through the tenant-filtered `DbContext`; cross-tenant department isolation is covered by the US-CHR-004 isolation arms (`DepartmentServiceTests` cross-tenant cases, `DepartmentManagerTenantValidationTests`)
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (already green — this TC binds existing arms, it does not describe a manual run):**
  the three ISSUE-364 arms in `src/backend/HRM.Tests/Unit/DepartmentServiceTests.cs:673-714`
  (`GetAll_returns_the_ACTIVE_employee_count_per_department_issue364`,
  `GetAll_returns_the_manager_display_name_issue364`,
  `GetAll_leaves_manager_name_null_when_no_manager_is_set_issue364`).
- **Fix sites:** `HRM.Application/Features/Departments/DTOs/DepartmentDto.cs:21` (`ManagerName`), `:29`
  (`EmployeeCount`); `HRM.Infrastructure/Services/DepartmentService.cs:306-323` (batched population) and
  `:419-420` (`ToDto` projection); `department-list.component.ts:249,255`;
  `department-tree.component.ts:256,295`.
- **Coverage note (honest limits of the bound arms):**
  1. The backend arms run on the **EF Core InMemory** provider, so the `GroupBy`/`ToDictionaryAsync` shapes
     are asserted for *behaviour*, not for PostgreSQL SQL translation.
  2. Steps 4-6 are **code-verified only** — there is no Karma arm asserting the list/tree render the two
     fields, and no assertion pinning the batched (non-N+1) query count. Those are documented gaps, not
     claimed coverage.

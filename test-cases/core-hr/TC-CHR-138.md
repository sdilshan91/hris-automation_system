---
id: TC-CHR-138
user_story: US-CHR-003
module: Core HR
priority: critical
type: security
status: automated
created: 2026-06-12
updated: 2026-07-04
regression_for: ISSUE-018
automated_by: HRM.Tests/Unit/EmployeeDirectoryAuthorizationTests.cs
---

# TC-CHR-138: Role-based directory visibility -- endpoint access (ISSUE-018) + field-level scope (FR-9, BR-3)

## 1. Test Objective
Verify role-based visibility of the employee directory on two levels:

- **A. Endpoint access (ISSUE-018, regression).** Every role that can legitimately see the
  directory must reach `GET /api/v1/tenant/employees/directory` -- not just holders of the literal
  `Employee.View.Own`. HR Officer / Tenant Admin (`Employee.View.All`) and Managers
  (`Employee.View.Team`) hold strict supersets of `View.Own` and must be **authorized**, while a
  caller with no `Employee.View.*` permission must be **denied**.
- **B. Field-level scope (FR-9, BR-3, BR-4).** When a user with Employee role accesses the
  directory, the API response and UI exclude sensitive fields (email, phone, dateOfJoining,
  employmentType) and show only the basic directory view (name, photo, department, job title,
  location, status).

## 2. Related Requirements
- User Story: US-CHR-003
- Finding: ISSUE-018 (HIGH) -- directory gated by single literal `Employee.View.Own`, so
  `Employee.View.All` / `Employee.View.Team` holders were 403'd despite `ResolveVisibility`
  already mapping their permission to Full/Team visibility.
- Functional Requirements: FR-9
- Business Rules: BR-3, BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Employee role is authenticated in "acme" (has `Employee.View.Own` permission only).
- 30 employees exist in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User Role | Employee | Basic directory access |
| Visible fields | employee_no, first_name, last_name, department_name, job_title_name, status, profile_photo_url, location | All roles |
| Hidden fields | email, phone, date_of_joining, employment_type | HR/Manager only |

## 5. Test Steps

### Part A -- Endpoint access authorization (ISSUE-018 regression)
Asserted at the authorization layer: the directory's required-permission set is derived from the
actual `EmployeesController.GetDirectory` `[RequirePermission(...)]` attribute (via the real
`PermissionPolicyProvider`) and evaluated by the real `PermissionAuthorizationHandler` (any-of).

| Step | Persona (permissions held) | Expected Result |
|------|----------------------------|-----------------|
| A1 | Tenant Admin / HR Officer (`Employee.View.All` only) | **Authorized** -- reaches directory (200, not 403). Pre-fix: 403. |
| A2 | Manager (`Employee.View.Team` only) | **Authorized** -- reaches directory. Pre-fix: 403. |
| A3 | Employee (`Employee.View.Own` only) | **Authorized** -- unchanged from pre-fix. |
| A4 | No `Employee.View.*` (e.g. `Leave.View.Own` only) | **Denied** -- fix widens roles, it does not open the endpoint. |

### Part B -- Field-level visibility (FR-9, BR-3, BR-4)
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Employee role in "acme" tenant | JWT contains Employee-level permissions. |
| 2 | Send `GET /api/v1/tenant/employees/directory?page=1&pageSize=20` | Response is 200 OK. |
| 3 | Inspect the response body for any employee object | Fields `email`, `phone`, `dateOfJoining`, `employmentType` are NOT present (null or absent from the JSON object). |
| 4 | Verify visible fields are present | Each employee object contains: `employeeNo`, `firstName`, `lastName`, `departmentName`, `jobTitleName`, `status`, `profilePhotoUrl`, `location`. |
| 5 | Navigate to the directory UI | Employee cards do NOT show email, phone, date_of_joining, or employment type. |
| 6 | Verify the "Export" button behavior | If export is available, the exported file also excludes sensitive fields. |

## 6. Postconditions
- Every legitimate role (Admin/HR/Manager/Employee) can reach the directory; unrelated permissions cannot.
- No sensitive data was exposed to the Employee role.

## 8. Automation Binding
- Part A (ISSUE-018): `HRM.Tests/Unit/EmployeeDirectoryAuthorizationTests.cs` --
  `Directory_ViewAllOnlyPrincipal_IsAuthorized_ISSUE018` (key regression),
  `Directory_ViewTeamOnlyPrincipal_IsAuthorized_ISSUE018`,
  `Directory_ViewOwnOnlyPrincipal_IsAuthorized_ISSUE018`,
  `Directory_NonEmployeeViewPermissionPrincipal_IsDenied_ISSUE018`,
  `Directory_RequiredPermissionSet_TracksControllerAndIncludesViewAll_ISSUE018`,
  `Directory_PreFixSingleViewOwnRequirement_WouldDenyViewAllPrincipal_ISSUE018`.
  The suite derives the required-permission set from the controller attribute, so reverting the
  guard to a single `Employee.View.Own` re-fails the View.All / View.Team arms.
- Part B (field scope) remains covered at the API/UI layer per the steps above.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

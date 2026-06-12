---
id: TC-CHR-138
user_story: US-CHR-003
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-138: Role-based field visibility -- Employee role sees basic directory only (FR-9, BR-3)

## 1. Test Objective
Verify that when a user with Employee role accesses the directory, the API response and UI exclude sensitive fields (email, phone, dateOfJoining, employmentType) and show only the basic directory view (name, photo, department, job title, location, status). This validates FR-9, BR-3, BR-4.

## 2. Related Requirements
- User Story: US-CHR-003
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
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Employee role in "acme" tenant | JWT contains Employee-level permissions. |
| 2 | Send `GET /api/v1/tenant/employees/directory?page=1&pageSize=20` | Response is 200 OK. |
| 3 | Inspect the response body for any employee object | Fields `email`, `phone`, `dateOfJoining`, `employmentType` are NOT present (null or absent from the JSON object). |
| 4 | Verify visible fields are present | Each employee object contains: `employeeNo`, `firstName`, `lastName`, `departmentName`, `jobTitleName`, `status`, `profilePhotoUrl`, `location`. |
| 5 | Navigate to the directory UI | Employee cards do NOT show email, phone, date_of_joining, or employment type. |
| 6 | Verify the "Export" button behavior | If export is available, the exported file also excludes sensitive fields. |

## 6. Postconditions
- No sensitive data was exposed to the Employee role.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

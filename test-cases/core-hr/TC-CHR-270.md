---
id: TC-CHR-270
user_story: US-CHR-011
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-270: My Team / direct reports view lists all reports with correct fields

## 1. Test Objective
Verify that a manager viewing their team dashboard sees a list of all direct reports (employees who have them as reporting manager), with name, job title, department, status, and quick-action links (view profile, approve leave). This validates AC-4 and FR-5.

## 2. Related Requirements
- User Story: US-CHR-011
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Manager M is authenticated with Manager role.
- Employees E1, E2, and E3 have `reports_to_employee_id` = M.id.
- E1 is "active", E2 is "probation", E3 is "active". All are in different departments.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager M | manager@acme.test | Has 3 direct reports |
| Employee E1 | emp1@acme.test | Active, Engineering dept, "Senior Dev" |
| Employee E2 | emp2@acme.test | Probation, Marketing dept, "Analyst" |
| Employee E3 | emp3@acme.test | Active, Finance dept, "Accountant" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Manager M navigates to the "My Team" / team dashboard page. | The page loads and displays a list/card view of direct reports. |
| 2 | Verify E1 appears in the list. | E1's card shows: avatar, name ("emp1"), job title ("Senior Dev"), department ("Engineering"), status badge ("Active" in green). |
| 3 | Verify E2 appears in the list. | E2's card shows: avatar, name, job title ("Analyst"), department ("Marketing"), status badge ("Probation" in amber). |
| 4 | Verify E3 appears in the list. | E3's card shows: avatar, name, job title ("Accountant"), department ("Finance"), status badge ("Active" in green). |
| 5 | Verify all 3 employees are shown (count). | The total count or list length is 3. |
| 6 | Hover over E1's card. | Quick-action links appear: "View Profile" and "Approve Leave". |
| 7 | Click "View Profile" on E1. | Navigates to E1's employee profile page. |
| 8 | Verify the API response: `GET /api/v1/tenant/employees/{M.id}/direct-reports`. | Returns an array of 3 employee summary objects, each containing employee_id, name, job_title, department, status, and avatar_url. |

## 6. Postconditions
- No state change; read-only view verification.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

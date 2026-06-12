---
id: TC-CHR-063
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
updated: 2026-06-12
unblocked_by: US-CHR-001
---

# TC-CHR-063: Grade linked to job title displayed on employee profile

## 1. Test Objective
Verify that when a job title with a linked salary grade is assigned to an employee, the associated grade is displayed on the employee's profile, completing the full AC-4 flow. Grade changes on the job title should be reflected on the employee profile. Previously BLOCKED on US-CHR-001 -- now unblocked.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-4
- Functional Requirements: FR-3
- Business Rules: BR-5
- Dependencies: US-CHR-001 (Employees) -- now available; Grade entity (Payroll module) may still be deferred

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A job title "Senior Developer" exists with grade "L5 - Senior" linked (grade_id populated).
- An employee "John Doe" exists and is assigned the "Senior Developer" job title (created via US-CHR-001).
- A user with Tenant Admin or HR Officer role is authenticated.
- Note: If the Grade entity is still deferred (Payroll module), this test verifies whatever grade information is available (e.g., grade_id displayed as a placeholder or "L5 - Senior" if grade name is stored/resolved).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Job Title | Senior Developer | Has grade "L5 - Senior" linked |
| Employee | John Doe | Assigned to "Senior Developer" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the employee profile of "John Doe" | Employee profile page loads. |
| 2 | Verify the Job Title field shows "Senior Developer" | Job title is correctly displayed. |
| 3 | Verify the Grade field (or label adjacent to job title) shows "L5 - Senior" | The linked grade is displayed alongside or near the job title on the profile. If the Grade entity is not yet available, verify the grade_id is displayed or a placeholder ("Grade linked") is shown. |
| 4 | Navigate to the Job Titles admin page and change "Senior Developer"'s grade from "L5 - Senior" to "L6 - Staff" | Grade is updated on the job title record. |
| 5 | Return to the employee profile of "John Doe" | Grade displayed now shows "L6 - Staff" (reflecting the updated grade on the job title). |
| 6 | Verify the grade change is reflected without needing to re-assign the job title to the employee | The employee's profile dynamically resolves the grade via the job title's current grade link. |

## 6. Postconditions
- The employee profile correctly reflects the grade associated with their job title.
- Grade changes on the job title are reflected on employee profiles in real time.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

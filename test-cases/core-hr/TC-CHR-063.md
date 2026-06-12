---
id: TC-CHR-063
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: blocked
created: 2026-06-12
---

# TC-CHR-063: Grade linked to job title displayed on employee profile [BLOCKED on US-CHR-001]

## 1. Test Objective
Verify that when a job title with a linked salary grade is assigned to an employee, the associated grade is displayed on the employee's profile, completing the full AC-4 flow. This test is BLOCKED because it requires the Employee entity from US-CHR-001.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-4
- Functional Requirements: FR-3
- Business Rules: BR-5

## 3. Preconditions
- **BLOCKED**: Requires US-CHR-001 (Employee entity) to be implemented.
- Tenant "acme" exists with status `active`.
- A job title "Senior Developer" exists with grade "L5 - Senior" linked.
- An employee exists and is assigned the "Senior Developer" job title.

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
| 3 | Verify the Grade field (or label adjacent to job title) shows "L5 - Senior" | The linked grade is displayed alongside or near the job title on the profile. |
| 4 | Change the job title's grade from "L5 - Senior" to "L6 - Staff" via the Job Titles admin page | Grade is updated on the job title record. |
| 5 | Return to the employee profile of "John Doe" | Grade displayed now shows "L6 - Staff" (reflecting the updated grade on the job title). |

## 6. Postconditions
- The employee profile correctly reflects the grade associated with their job title.
- Grade changes on the job title are reflected on employee profiles.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Blocked Status
- **Blocked By**: US-CHR-001 (Create and Manage Employees)
- **Reason**: The Employee entity does not exist yet. AC-4 requires an employee to be assigned a job title so that the associated grade can be displayed on the employee profile. This end-to-end flow cannot be tested until employee management is implemented.
- **Unblock Criteria**: US-CHR-001 is delivered, employees can be assigned `job_title_id`, and employee profile pages display job title and grade information.

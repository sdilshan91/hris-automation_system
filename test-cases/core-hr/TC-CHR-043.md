---
id: TC-CHR-043
user_story: US-CHR-005
module: Core HR
priority: critical
type: functional
status: blocked
created: 2026-06-12
---

# TC-CHR-043: Deactivate job title blocked when assigned to active employees [BLOCKED on US-CHR-001]

## 1. Test Objective
Verify that the system prevents deactivation of a job title that is currently assigned to active employees, displaying the warning message specified in AC-5: "This job title is assigned to X active employees. Reassign them before deactivating." This test is BLOCKED because it requires the Employee entity from US-CHR-001 to assign employees to job titles.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7
- Business Rules: BR-3

## 3. Preconditions
- **BLOCKED**: Requires US-CHR-001 (Employee entity) to be implemented.
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A job title "Software Engineer" exists with 3 active employees assigned to it.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Job Title | Software Engineer | Has active employees assigned |
| Assigned Employee Count | 3 | Active employees |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page | Job Titles list page loads; "Software Engineer" is visible with Employee Count badge showing "3". |
| 2 | Click the "Deactivate" action on the "Software Engineer" row | A confirmation dialog or warning message appears. |
| 3 | Observe the warning message | Warning message reads: "This job title is assigned to 3 active employees. Reassign them before deactivating." |
| 4 | Verify the deactivation action is blocked (Save/Confirm button is disabled or the action is refused) | The system does not allow proceeding with deactivation. |
| 5 | Verify API call (if made) returns an error | Response status is 409 Conflict or 422 Unprocessable Entity with the warning message. |
| 6 | Verify the job title remains active | `is_active` is still `true` in the database. |

## 6. Postconditions
- The job title "Software Engineer" remains active and unchanged.
- No audit log entry is created for a blocked deactivation.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Blocked Status
- **Blocked By**: US-CHR-001 (Create and Manage Employees)
- **Reason**: The Employee entity does not exist yet. AC-5 and FR-7 require employees to be assignable to job titles. This test case cannot be executed until employee management is implemented and employees can be assigned job titles.
- **Unblock Criteria**: US-CHR-001 is delivered and employees can be assigned `job_title_id` values.

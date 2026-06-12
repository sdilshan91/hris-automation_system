---
id: TC-CHR-043
user_story: US-CHR-005
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
updated: 2026-06-12
unblocked_by: US-CHR-001
---

# TC-CHR-043: Deactivate job title blocked when assigned to active employees

## 1. Test Objective
Verify that the system prevents deactivation of a job title that is currently assigned to active employees, displaying the warning message specified in AC-5: "This job title is assigned to X active employees. Reassign them before deactivating." Previously BLOCKED on US-CHR-001 -- now unblocked.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7
- Business Rules: BR-3
- Dependencies: US-CHR-001 (Employees) -- now available

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A job title "Software Engineer" exists.
- 3 active employees are assigned to the "Software Engineer" job title (created via US-CHR-001 employee creation flow with job_title_id pointing to "Software Engineer").

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Job Title | Software Engineer | Has active employees assigned |
| Assigned Employee Count | 3 | Active employees with this job_title_id |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page | Job Titles list page loads; "Software Engineer" is visible with Employee Count badge showing "3". |
| 2 | Click the "Deactivate" action on the "Software Engineer" row | A confirmation dialog or warning message appears. |
| 3 | Observe the warning message | Warning message reads: "This job title is assigned to 3 active employees. Reassign them before deactivating." |
| 4 | Verify the deactivation action is blocked (Save/Confirm button is disabled or the action is refused) | The system does not allow proceeding with deactivation. |
| 5 | Verify API call (if made) returns an error | Response status is 409 Conflict or 422 Unprocessable Entity with the warning message and error code `has_active_employees`. |
| 6 | Verify the job title remains active | `is_active` is still `true` in the database. |
| 7 | Reassign all 3 employees to a different job title (e.g., "Senior Engineer") | Employees updated to new job_title_id. |
| 8 | Retry deactivation of "Software Engineer" | Deactivation now succeeds (0 active employees assigned). |

## 6. Postconditions
- The job title "Software Engineer" remains active when employees are assigned.
- After reassigning all employees, deactivation succeeds.
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

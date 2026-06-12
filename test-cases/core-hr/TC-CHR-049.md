---
id: TC-CHR-049
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: blocked
created: 2026-06-12
---

# TC-CHR-049: Employee count badge displays correct count per job title [BLOCKED on US-CHR-001]

## 1. Test Objective
Verify that each job title row in the list displays an accurate employee count badge showing the number of active employees currently assigned to that title (FR-4). The badge should be clickable and navigate to the employee directory filtered by that job title (per UI/UX notes). This test is BLOCKED because it requires the Employee entity from US-CHR-001.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-4
- Business Rules: BR-3

## 3. Preconditions
- **BLOCKED**: Requires US-CHR-001 (Employee entity) to be implemented.
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- Job title "Software Engineer" has 3 active employees assigned.
- Job title "Data Scientist" has 0 employees assigned.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Job Title 1 | Software Engineer | 3 active employees |
| Job Title 2 | Data Scientist | 0 employees |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page | Job Titles list loads. |
| 2 | Verify the "Software Engineer" row shows an employee count badge of "3" | Badge displays "3". |
| 3 | Verify the "Data Scientist" row shows an employee count badge of "0" | Badge displays "0". |
| 4 | Click the employee count badge on "Software Engineer" | Navigation occurs to the employee directory page filtered by job title = "Software Engineer". |
| 5 | Verify the employee directory shows exactly 3 employees | All 3 employees assigned to "Software Engineer" are listed. |

## 6. Postconditions
- No data is modified; this is a read-only verification.

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
- **Reason**: The Employee entity does not exist yet. FR-4 requires employees to be assignable to job titles for the count to be meaningful. The employee directory (US-CHR-003) also depends on US-CHR-001.
- **Unblock Criteria**: US-CHR-001 is delivered and employees can be assigned `job_title_id` values.

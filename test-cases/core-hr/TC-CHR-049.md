---
id: TC-CHR-049
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
updated: 2026-06-12
unblocked_by: US-CHR-001
---

# TC-CHR-049: Employee count badge displays correct count per job title

## 1. Test Objective
Verify that each job title row in the list displays an accurate employee count badge showing the number of active employees currently assigned to that title (FR-4). The badge should reflect real-time counts and, when clicked, navigate to the employee directory filtered by that job title. Previously BLOCKED on US-CHR-001 -- now unblocked.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-4
- Business Rules: BR-3
- Dependencies: US-CHR-001 (Employees) -- now available; US-CHR-003 (employee directory, may still be deferred)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- Job title "Software Engineer" has 3 active employees assigned (created via US-CHR-001).
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
| 2 | Verify the "Software Engineer" row shows an employee count badge of "3" | Badge displays "3" (not "---" or "0"). The hardcoded zero stub from the deferred state is replaced with a real count. |
| 3 | Verify the "Data Scientist" row shows an employee count badge of "0" | Badge displays "0". |
| 4 | Create a new employee assigned to "Data Scientist" (via US-CHR-001 flow) | Employee created successfully. |
| 5 | Return to the Job Titles page and verify "Data Scientist" badge now shows "1" | Count updates to reflect the new assignment. |
| 6 | Soft-delete (deactivate) one of the "Software Engineer" employees | Employee is soft-deleted. |
| 7 | Verify "Software Engineer" badge now shows "2" | Count decreases to reflect only active employees. |
| 8 | Click the employee count badge on "Software Engineer" | Navigation occurs to the employee directory page filtered by job title = "Software Engineer" (if US-CHR-003 is available; otherwise, verify the click target is present but may be a no-op). |

## 6. Postconditions
- Employee count badges accurately reflect the number of active employees assigned to each job title.
- Counts update dynamically as employees are added, reassigned, or deactivated.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

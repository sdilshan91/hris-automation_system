---
id: TC-CHR-271
user_story: US-CHR-011
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-271: Bulk assign manager to 5 employees via employee directory

## 1. Test Objective
Verify that an HR Officer can select multiple employees in the employee directory, use the "Assign Manager" bulk action, select a manager in the modal, and upon confirmation all selected employees are updated with the new reporting manager, with individual audit entries logged for each employee. This validates AC-5 and FR-4.

## 2. Related Requirements
- User Story: US-CHR-011
- Acceptance Criteria: AC-5
- Functional Requirements: FR-4, FR-6
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- 5 employees (E1 through E5) exist with no manager assigned.
- Manager M exists with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employees | e1@acme.test through e5@acme.test | 5 employees, no current manager |
| Manager M | bulk.mgr@acme.test | Active employee to be assigned |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the employee directory. | The directory lists all employees with checkboxes for selection. |
| 2 | Select the checkboxes for E1, E2, E3, E4, and E5. | All 5 are visually selected. A floating action toolbar appears at the bottom of the screen. |
| 3 | Click "Assign Manager" from the floating action toolbar. | A modal opens with an employee search/autocomplete for selecting a manager. |
| 4 | Search for Manager M in the autocomplete. | Manager M appears in the results with avatar, name, department, and job title. |
| 5 | Select Manager M and click "Confirm". | A loading indicator appears. On success, a toast shows "5 employees assigned to [Manager M name]." |
| 6 | Verify each employee's record via API: `GET /api/v1/tenant/employees/{Ei.id}` for i = 1..5. | Each employee has `reports_to_employee_id` = M.id. |
| 7 | Verify Manager M's direct reports: `GET /api/v1/tenant/employees/{M.id}/direct-reports`. | The response includes all 5 employees. |
| 8 | Check employment history for each of E1 through E5. | Each employee has an individual employment history entry: "Reporting Manager changed from Not Assigned to [Manager M]". |
| 9 | Check audit log entries. | 5 separate audit log entries exist (one per employee), each with before (null) and after (Manager M) values. |

## 6. Postconditions
- All 5 employees have `reports_to_employee_id` = M.id.
- 5 individual employment history entries created.
- 5 individual audit log entries created.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

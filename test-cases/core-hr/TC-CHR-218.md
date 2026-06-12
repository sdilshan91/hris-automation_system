---
id: TC-CHR-218
user_story: US-CHR-009
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-218: Status transition form shows only valid transitions based on current status (AC-1)

## 1. Test Objective
Verify that the "Change Status" form dynamically presents only the valid outbound transitions defined by the state machine (FR-2) for each current status. Invalid transitions must not appear in the dropdown. This validates AC-1 and BR-1 (server-side state machine enforcement reflected in the UI).

## 2. Related Requirements
- User Story: US-CHR-009
- Acceptance Criteria: AC-1
- Functional Requirements: FR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employees exist in the following statuses: `active`, `probation`, `suspended`, `inactive`, `terminated`.

## 4. Test Data
| Current Status | Expected Valid Transitions | Invalid (must NOT appear) |
|----------------|---------------------------|---------------------------|
| probation | active, terminated | suspended, inactive, probation |
| active | suspended, terminated, inactive | probation, active |
| suspended | active, terminated | probation, suspended, inactive |
| inactive | active, terminated | probation, suspended, inactive |
| terminated | (none -- button should be hidden or disabled) | all statuses |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the employee with status `probation` and click "Change Status". | Dropdown shows exactly: "Active", "Terminated". No other statuses listed. |
| 2 | Close the form. Navigate to the employee with status `active` and click "Change Status". | Dropdown shows exactly: "Suspended", "Terminated", "Inactive". No "Probation" or "Active" listed. |
| 3 | Close the form. Navigate to the employee with status `suspended` and click "Change Status". | Dropdown shows exactly: "Active", "Terminated". No other statuses listed. |
| 4 | Close the form. Navigate to the employee with status `inactive` and click "Change Status". | Dropdown shows exactly: "Active", "Terminated". No other statuses listed. |
| 5 | Navigate to the employee with status `terminated`. | The "Change Status" button is either hidden or disabled. If clicked (when disabled), no dropdown appears. |
| 6 | Verify via API: `GET /api/v1/tenant/employees/{terminated-emp-id}/valid-transitions` (or equivalent). | Response returns an empty array `[]` or a 400 indicating no transitions available. |

## 6. Postconditions
- No status changes were made (this test only verifies form presentation).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

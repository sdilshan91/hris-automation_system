---
id: TC-LV-120
user_story: US-LV-006
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-120: Leave history section lists and filters past requests

## 1. Test Objective
Verify that the dashboard's leave-history section presents a filterable list of past leave requests (approved, rejected, cancelled) for the employee and that filters narrow the list correctly (FR-6).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-3
- Functional Requirements: FR-6

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated.
- Nina has historical requests: 3 Approved, 1 Rejected, 1 Cancelled across 2025-2026.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Approved (past) | 3 | -- |
| Rejected | 1 | -- |
| Cancelled | 1 | -- |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the leave-history section | All past requests (Approved/Rejected/Cancelled) are listed with type, dates, days, and status badge. |
| 2 | Filter by status = Rejected | Only the rejected request remains visible. |
| 3 | Filter by leave type (and/or year) | The list narrows to matching requests; clearing filters restores the full list. |
| 4 | Verify scope | Only the authenticated employee's own history is shown (no other employees' requests). |

## 6. Postconditions
- History list filters correctly and is scoped to the employee; read-only.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

---
id: TC-LV-109
user_story: US-LV-006
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-109: Leave Balance Dashboard loads a summary card per active leave type (happy path)

## 1. Test Objective
Verify that when the employee navigates to the Leave Balance Dashboard, `GET /api/v1/leaves/my-balance` returns one entry per active leave type and the UI renders one summary card per type showing entitlement, used, pending, balance remaining, and a visual progress bar (AC-1, FR-1, FR-2).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" is active; employee "Nina Patel" is authenticated with an active employee record.
- Tenant has at least 3 active leave types (e.g., Annual, Sick, Casual) with entitlements computed (accrual/upfront ledger entries exist).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Nina Patel | Authenticated |
| Active leave types | Annual (14), Sick (7), Casual (7) | Entitlements computed |
| Year | 2026 (current) | Default selected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Nina, navigate to the Leave Balance Dashboard | `GET /api/v1/leaves/my-balance` returns 200 with one object per active leave type, each carrying `leaveTypeId`, `leaveTypeName`, `color`, `entitlement`, `used`, `pending`, `balance`, `carryForward`, `expired`. |
| 2 | Observe the dashboard grid | One summary card is rendered per active leave type, ordered consistently (e.g., display order), each with the type name + color accent. |
| 3 | Inspect a card (e.g., Annual) | Card shows entitlement, used, pending, and balance-remaining numeric values plus a visual progress bar reflecting used vs entitlement. |
| 4 | Count cards vs active leave types | The number of cards equals the number of active leave types for the tenant; no archived/deactivated type appears in the main grid. |

## 6. Postconditions
- Dashboard displays an accurate per-type balance summary for the current leave year. No data is mutated (read-only view).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

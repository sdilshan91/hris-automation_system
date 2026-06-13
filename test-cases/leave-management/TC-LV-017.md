---
id: TC-LV-017
user_story: US-LV-001
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-017: Audit trail captures before/after JSON on configuration changes

## 1. Test Objective
Verify that every configuration change to a leave type produces an audit log entry with before/after JSON snapshots, capturing the exact field values that changed (NFR-3).

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-3
- Acceptance Criteria: AC-2

## 3. Preconditions
- Tenant "acme" has an active leave type "Annual Leave" with known configuration.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Field | Before | After |
|-------|--------|-------|
| Annual Entitlement | 20.00 | 25.00 |
| Carry Forward Limit | 5.00 | 10.00 |
| Half Day Allowed | true | false |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Record the current state of "Annual Leave" via `GET /api/v1/leave-types/{id}` | Full JSON representation captured as baseline. |
| 2 | Update "Annual Leave": annual_entitlement = 25, carry_forward_limit = 10, half_day_allowed = false | API returns 200 OK with updated values. |
| 3 | Query the audit log for this leave_type_id | Audit entry found with: `action: leave_type_updated`, `entity_type: LeaveType`, `entity_id: {leave_type_id}`, `tenant_id: acme_id`, `user_id`, `timestamp`. |
| 4 | Verify the before-snapshot in the audit record | JSON contains `{ "annual_entitlement": 20.00, "carry_forward_limit": 5.00, "half_day_allowed": true, ... }`. |
| 5 | Verify the after-snapshot in the audit record | JSON contains `{ "annual_entitlement": 25.00, "carry_forward_limit": 10.00, "half_day_allowed": false, ... }`. |
| 6 | Perform a second update: change name from "Annual Leave" to "Paid Annual Leave" | API returns 200 OK. |
| 7 | Verify a second audit entry exists with correct before/after for the name change | Before: `{ "name": "Annual Leave" }`, After: `{ "name": "Paid Annual Leave" }`. |
| 8 | Verify audit entries are ordered chronologically | Entries listed newest-first with correct timestamps. |

## 6. Postconditions
- Two audit entries exist for the two updates.
- Each entry has complete before/after JSON snapshots.
- Audit entries are tenant-scoped.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

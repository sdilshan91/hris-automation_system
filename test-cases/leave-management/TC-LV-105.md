---
id: TC-LV-105
user_story: US-LV-005
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-105: Audit log records Leave.Approved / Leave.Rejected with before/after JSON

## 1. Test Objective
Verify that every approval and rejection writes an audit log entry with `action = Leave.Approved` or `Leave.Rejected`, `resource_type = LeaveRequest`, the resource id, the actioning user, and a before/after JSON snapshot capturing the status transition (FR-7).

## 2. Related Requirements
- User Story: US-LV-005
- Functional Requirements: FR-7
- Non-Functional Requirements: NFR-3 (tenant-isolated audit)

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- One pending request R1 will be approved and another R2 will be rejected.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| R1 | Pending -> Approved | action Leave.Approved |
| R2 | Pending -> Rejected | action Leave.Rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Approve R1 | An audit entry is written: `action = Leave.Approved`, `resource_type = LeaveRequest`, `resource_id = R1`, actor = Robert. |
| 2 | Inspect R1's audit before/after JSON | `before.status = Pending`, `after.status = Approved`; relevant fields captured. |
| 3 | Reject R2 with a reason | An audit entry is written: `action = Leave.Rejected`, before `Pending`, after `Rejected`; the rejection reason is recorded. |
| 4 | Verify audit timestamps and tenant scoping | Each entry carries the correct `tenant_id`, `actioned_at`/`created_at`, and is visible only within "acme". |

## 6. Postconditions
- Two audit entries persisted with correct actions and before/after snapshots, tenant-scoped.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

---
id: TC-AUTH-045
user_story: US-AUTH-006
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-03
---

# TC-AUTH-045: Resource-level authorization blocks manager from approving non-report leave

## 1. Test Objective
Verify that the resource-level authorization handler (layer 3 of the three-layer authorization model) correctly denies a manager from approving a leave request for an employee who is not their direct report, even though the manager holds the `Leave.Approve.Team` permission. This validates that permission-level access alone is insufficient when resource-level constraints apply.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5 (layer 3: resource-based authorization)
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `manager-a@acme.com` has the "Manager" role with `Leave.Approve.Team` permission.
- Manager A manages Team A (employees: `emp-a1@acme.com`, `emp-a2@acme.com`).
- User `emp-b1@acme.com` is in Team B (managed by a different manager).
- A pending leave request `LR-001` exists for `emp-b1@acme.com` (Team B).
- A pending leave request `LR-002` exists for `emp-a1@acme.com` (Team A -- Manager A's direct report).
- The organization hierarchy / manager-report relationships are configured in the system.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager | manager-a@acme.com | Manager role, manages Team A |
| Direct report | emp-a1@acme.com | Team A, reports to Manager A |
| Non-report | emp-b1@acme.com | Team B, does NOT report to Manager A |
| Leave request (non-report) | LR-001 | Pending leave for emp-b1 |
| Leave request (direct report) | LR-002 | Pending leave for emp-a1 |
| Permission held | Leave.Approve.Team | Scoped to team/direct reports |
| Approve endpoint | POST /api/v1/tenant/leave/requests/{id}/approve | Resource-level check required |
| Tenant | acme (acme.yourhrm.com) | Active tenant |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as `manager-a@acme.com` at `acme.yourhrm.com` and obtain JWT. | JWT issued with `roles: ["Manager"]` and `permissions` including `Leave.Approve.Team`. |
| 2 | Send `POST /api/v1/tenant/leave/requests/{LR-001}/approve` (leave for `emp-b1`, NOT a direct report). | HTTP 403 Forbidden. Response body indicates: "You do not have authorization to approve leave for this employee. Resource-level access denied." |
| 3 | Verify the 403 response contains details about the authorization failure. | Response includes information that the manager-report relationship validation failed (not just a generic 403). |
| 4 | Verify the authorization failure is logged with details. | Security log entry contains: user_id (manager-a), endpoint, leave_request_id (LR-001), employee_id (emp-b1), reason: "manager-report relationship not found." |
| 5 | Send `POST /api/v1/tenant/leave/requests/{LR-002}/approve` (leave for `emp-a1`, a direct report). | HTTP 200 OK. Leave request approved. The resource-level authorization validates the manager-report relationship and permits the action. |
| 6 | Verify leave request LR-002 status is updated to "Approved". | `GET /api/v1/tenant/leave/requests/{LR-002}` returns status "Approved" with approver = manager-a. |
| 7 | Verify leave request LR-001 status remains "Pending". | `GET /api/v1/tenant/leave/requests/{LR-001}` still shows status "Pending" (the failed approval attempt did not change it). |

## 6. Postconditions
- Leave request for non-report employee remains unchanged (Pending).
- Leave request for direct report is approved.
- Authorization failure events are logged for security monitoring.
- The three-layer authorization model is validated: JWT valid (layer 1), permission present (layer 2), but resource-level check (layer 3) correctly blocks cross-team access.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

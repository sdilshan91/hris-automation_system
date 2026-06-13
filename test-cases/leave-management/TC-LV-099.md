---
id: TC-LV-099
user_story: US-LV-005
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-099: Only the designated approver can action a request -- another manager is denied

## 1. Test Objective
Verify that only the designated approver (the requester's direct manager, or the current-level approver in a multi-level workflow) can approve or reject a given request. A different manager -- even one who holds `Leave.Approve.Team` for their own team -- cannot action a request that does not belong to their team (BR-1).

## 2. Related Requirements
- User Story: US-LV-005
- Business Rules: BR-1
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" (`Leave.Approve.Team`) has direct report "Jane Smith" with a pending request R.
- Manager "Mara Cole" (`Leave.Approve.Team`) manages a different team; Jane does NOT report to Mara.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request R | Jane Smith (reports to Robert) | Pending |
| Robert | Designated approver | Allowed |
| Mara | Other team's manager | Must be denied |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Mara, `POST /api/v1/leaves/{R}/approve` | Denied -- 403 Forbidden (or 404 if scope-filtered): Mara is not Jane's designated approver (BR-1). |
| 2 | As Mara, `POST /api/v1/leaves/{R}/reject` with a reason | Denied -- 403/404; no state change to R. |
| 3 | Inspect R after Mara's attempts | `status` remains `Pending`; no ledger/history/audit entry written from Mara's attempts. |
| 4 | As Robert (the designated approver), approve R | Succeeds -- 200; R transitions to Approved with its side effects (positive control). |

## 6. Postconditions
- Mara's cross-team attempts are denied with no side effects; Robert can action R.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

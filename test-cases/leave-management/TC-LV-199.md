---
id: TC-LV-199
user_story: US-LV-010
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-199: A MANAGER cannot cancel a leave on behalf of an employee -- 403 (BR-1)

## 1. Test Objective
Verify that only the requesting employee may cancel their own leave: a manager (even the direct approver) attempting to cancel a report's leave via the cancel endpoint is denied with 403 Forbidden. Managers reject; they do not cancel on behalf (BR-1).

## 2. Related Requirements
- User Story: US-LV-010
- Business Rules: BR-1
- Non-Functional Requirements: NFR-2 (access control)
- Assumptions: Section 10 (manager rejects, employee cancels)

## 3. Preconditions
- Tenant "acme".
- Manager "Robert Lee" is authenticated and is the direct approver of employee "Jane Smith".
- Jane has a PENDING request R-pending and an APPROVED future request R-approved.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Actor | Robert Lee (manager) | not the requester |
| R-pending | Jane, Pending | not Robert's to cancel |
| R-approved | Jane, Approved future | not Robert's to cancel |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert, attempt `POST /api/v1/leaves/{R-pending}/cancel` | Denied with HTTP 403 Forbidden; the cancel action is not permitted for a non-owner. |
| 2 | As Robert, attempt `POST /api/v1/leaves/{R-approved}/cancel` | Denied with HTTP 403 Forbidden. |
| 3 | Inspect both requests | Statuses unchanged; no `cancelled_at`, no reversal ledger entry, no Cancelled approval-history row. |
| 4 | (UI) Verify Robert's queue | Robert sees Approve/Reject affordances (US-LV-005) but no "Cancel" affordance on a report's request. |

## 6. Postconditions
- Managers cannot cancel reports' leave; both requests are untouched; only the owning employee may cancel.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

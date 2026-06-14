---
id: TC-LV-198
user_story: US-LV-010
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-198: Cancellation reason is OPTIONAL for a pending leave -- cancel succeeds with or without a reason (boundary; BR-5)

## 1. Test Objective
Verify that a pending leave can be cancelled both with and without a reason (the reason is optional for pending requests), and that when a reason is supplied for a pending cancellation it is persisted on the request and approval history (BR-5, FR-2, FR-6).

## 2. Related Requirements
- User Story: US-LV-010
- Business Rules: BR-5
- Functional Requirements: FR-2, FR-6

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" has two PENDING Annual Leave requests, R-a and R-b (both future, status Pending).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| R-a | Pending | cancelled with no reason |
| R-b | Pending | cancelled with reason "Plans changed" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Cancel R-a with NO reason | Succeeds (200); `status = Cancelled`; no validation error (reason optional for pending). |
| 2 | Cancel R-b with `reason: "Plans changed"` | Succeeds (200); `status = Cancelled`; `cancellation_reason = "Plans changed"` persisted. |
| 3 | Inspect approval-history for both | Each has an `action = Cancelled`, actor = Jane; R-b's reason is recorded, R-a's reason is null/empty. |
| 4 | Query `leave_ledger` for both | No ledger rows created (both were pending -- AC-1 / FR-2). |

## 6. Postconditions
- Both pending requests are Cancelled; reason is optional and persisted only when provided; no ledger impact.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

---
id: TC-LV-197
user_story: US-LV-010
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-197: Cancellation reason is MANDATORY for an approved leave -- missing/blank reason is rejected (negative; BR-5)

## 1. Test Objective
Verify that cancelling an approved leave without a reason (omitted, empty, or whitespace-only) is rejected with a validation error, so no Cancelled transition and no reversal ledger entry occur (BR-5, FR-1).

## 2. Related Requirements
- User Story: US-LV-010
- Business Rules: BR-5
- Functional Requirements: FR-1

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" has an APPROVED future Annual Leave request R (eligible for cancellation in every respect except the missing reason).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request R | Annual Leave, future, Approved | eligible |
| Reason (a) | (omitted) | invalid |
| Reason (b) | "" | invalid |
| Reason (c) | "   " | whitespace-only, invalid |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Attempt `POST /api/v1/leaves/{R}/cancel` with no `reason` field | Rejected (HTTP 400) with a "cancellation reason is required" validation error. |
| 2 | Attempt with `reason: ""` | Rejected (same validation error). |
| 3 | Attempt with `reason: "   "` (whitespace) | Rejected -- whitespace is trimmed and treated as empty. |
| 4 | Inspect R's status and ledger | Unchanged -- `status = Approved`, no `cancelled_at`, no reversal `adjusted` row. |
| 5 | (UI) Verify the confirm dialog | The "Confirm cancellation" button stays disabled until a non-empty reason is entered for an approved request. |

## 6. Postconditions
- An approved leave cannot be cancelled without a valid reason; no state or ledger change from the rejected attempts.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

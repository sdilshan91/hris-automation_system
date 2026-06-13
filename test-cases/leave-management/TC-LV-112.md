---
id: TC-LV-112
user_story: US-LV-006
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-112: Ledger view renders all transaction types for the leave year

## 1. Test Objective
Verify that the ledger/transaction history surfaces every `leave_ledger` entry type for the leave year -- accruals, usages, adjustments, carry-forwards, and expirations -- each with the correct type badge and signed amount (AC-2, FR-3, BR-1).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated.
- For Annual Leave in 2026 the ledger contains at least one of each entry type: Accrual, Used, Adjusted, CarryForward, Expired.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Accrual | +1.17/month | Monthly accrual entries |
| Used | -5 | Approved leave |
| Adjusted | +1 | Manual HR adjustment |
| CarryForward | +2 | From 2025 |
| Expired | -0.5 | Carry-forward expiry |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Annual Leave ledger for 2026 | All entry types present in the data are listed; none are silently dropped. |
| 2 | Inspect the type badges | Accrual (e.g., green), Used (e.g., red), Adjusted (e.g., blue), CarryForward, and Expired each render a distinct, labelled badge -- color is not the sole indicator (text label present). |
| 3 | Verify amount signs and running balance | Positive entries (Accrual/CarryForward/positive Adjusted) increase balance-after; negative entries (Used/Expired/negative Adjusted) decrease it; the final balance-after reconciles to BR-1. |
| 4 | Verify the displayed balance == entitlement + carry_forward - used - expired + adjustments | The card balance derived from these ledger entries matches the BR-1 formula. |

## 6. Postconditions
- Full transaction history is visible and reconciles to the card balance; no mutation.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

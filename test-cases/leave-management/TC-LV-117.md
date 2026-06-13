---
id: TC-LV-117
user_story: US-LV-006
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-117: Year selector switches to a previous leave year (read-only)

## 1. Test Objective
Verify that the year-selector pill group lets the employee view balances and ledger for a previous leave year, and that prior-year data is read-only and reflects that year's figures (BR-5, FR-2, FR-3).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-2, FR-3
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated; current leave year 2026.
- Nina has ledger data for both 2025 and 2026 (different entitlement/used totals).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| 2026 Annual balance | 11 | Current year |
| 2025 Annual balance | 3 | Closed year |
| Years available | 2024, 2025, 2026 | Pill group |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | On the dashboard (default 2026), note the Annual card balance | balance reflects 2026 figures (e.g., 11). |
| 2 | Click the [2025] pill | `my-balance` / `my-ledger` are re-queried with `year=2025`; cards now show 2025 figures (e.g., balance 3). |
| 3 | Open the 2025 Annual ledger | Only 2025 transactions are listed; figures match the 2025 closed year. |
| 4 | Attempt any mutating action in prior-year view | No edit/apply affordance is available; the prior-year view is strictly read-only. |

## 6. Postconditions
- Prior-year balances/ledger viewable read-only; current-year view restored on switching back.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

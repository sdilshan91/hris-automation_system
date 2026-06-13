---
id: TC-LV-118
user_story: US-LV-006
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-118: Leave-year boundary respects tenant calendar vs fiscal-year configuration

## 1. Test Objective
Verify that balance aggregation and the "current leave year" honor the tenant's configured leave-year boundary (calendar year vs fiscal year), so transactions are bucketed into the correct year window (BR-4, FR-2, FR-3).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-2, FR-3
- Business Rules: BR-4

## 3. Preconditions
- Two tenants: "acme" configured as calendar year (Jan 1 - Dec 31); "globex" configured as fiscal year (Apr 1 - Mar 31).
- Each tenant has an employee with ledger entries spanning the boundary (e.g., entries dated 2026-02-15 and 2026-05-15).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme leave year | Calendar 2026 | Jan 1 - Dec 31 2026 |
| globex leave year | Fiscal 2026 | Apr 1 2026 - Mar 31 2027 |
| Entry A | 2026-02-15 | Pre-fiscal-boundary |
| Entry B | 2026-05-15 | Post-fiscal-boundary |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the acme employee, view the current-year (2026) dashboard | Both Entry A (Feb) and Entry B (May) fall within calendar 2026 and are aggregated into the 2026 balance. |
| 2 | As the globex employee, view the current fiscal-year dashboard | Entry A (2026-02-15) belongs to the PRIOR fiscal year; Entry B (2026-05-15) belongs to the current fiscal year -- only B is aggregated into the current year balance. |
| 3 | Switch globex to the prior fiscal year via the year selector | Entry A appears under the prior fiscal year window. |
| 4 | Verify the year label/range shown in the UI | The selector/year header reflects the tenant's configured boundary (calendar vs fiscal range), not a hard-coded Jan-Dec. |

## 6. Postconditions
- Aggregation windows match each tenant's configured leave-year boundary.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

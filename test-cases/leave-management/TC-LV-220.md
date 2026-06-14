---
id: TC-LV-220
user_story: US-LV-011
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-220: LOP has no entitlement/balance — it is a pure deduction mechanism (BR-1)

## 1. Test Objective
Verify BR-1: the LOP leave type carries no entitlement and no accruing balance. Assigning/creating LOP days never grants or consumes an LOP "balance"; LOP exists only to feed salary deductions. The LOP type therefore does not appear with a positive entitlement/remaining figure on balance views.

## 2. Related Requirements
- User Story: US-LV-011
- Business Rules: BR-1
- Cross-ref: US-LV-002 (entitlement engine), US-LV-006 (balance dashboard)

## 3. Preconditions
- Tenant "acme"; LOP type exists; employee "Mark Otieno" has 2 LOP days assigned.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| LOP days | 2 | assigned |
| LOP entitlement | 0 / n-a | no entitlement |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resolve effective entitlement for the LOP type (US-LV-002 engine) | LOP yields no positive entitlement (0 / not an accruing type); no accrual ledger entries are generated for LOP by the accrual job. |
| 2 | View Mark's balance dashboard (US-LV-006) | LOP is NOT shown as a positive-balance card; if surfaced at all it shows the cumulative LOP days as an informational/deduction figure, never as remaining entitlement. |
| 3 | Assign 2 more LOP days | The LOP day count for payroll increases; no "balance" is decremented (there is none) and no negative-balance limit applies. |
| 4 | Confirm payroll-facing total | lop-summary reflects the cumulative LOP days; LOP is a deduction count, not a balance. |

## 6. Postconditions
- LOP has no entitlement/balance; it functions purely as a deduction count fed to payroll.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

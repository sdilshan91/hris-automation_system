---
id: TC-LV-035
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-035: Leave year configuration -- calendar year vs fiscal year per tenant

## 1. Test Objective
Verify that entitlement rules are effective per leave year, and that the leave year start date is configurable per tenant (default: January 1). Test both calendar year (Jan-Dec) and fiscal year (e.g., Apr-Mar) configurations and verify that pro-rata calculations and rule effective dates respect the tenant's leave year setting.

## 2. Related Requirements
- User Story: US-LV-002
- Business Rules: BR-1
- Assumptions & Constraints: Section 10 (leave year start date configurable per tenant)

## 3. Preconditions
- Tenant "acme" exists with leave year start = January 1 (calendar year).
- Tenant "globex" exists with leave year start = April 1 (fiscal year).
- Both tenants have "Annual Leave" configured with 20 days/year.
- HR Officers authenticated in each respective tenant.

## 4. Test Data
| Tenant | Leave Year Start | Leave Year Period | Employee Join Date | Expected Pro-Rata |
|--------|-----------------|-------------------|--------------------|-------------------|
| acme | January 1 | 2026-01-01 to 2026-12-31 | 2026-07-01 | 10.00 (6/12) |
| globex | April 1 | 2026-04-01 to 2027-03-31 | 2026-07-01 | 15.00 (9/12) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In "acme" tenant, verify leave year configuration = January 1 | Configuration shows calendar year (Jan 1 - Dec 31). |
| 2 | In "acme" tenant, onboard Employee A with join date 2026-07-01 | Employee created. |
| 3 | Trigger accrual for "acme" Employee A | Balance = 10.00 days (6 months remaining in Jan-Dec year). |
| 4 | In "globex" tenant, verify leave year configuration = April 1 | Configuration shows fiscal year (Apr 1 - Mar 31). |
| 5 | In "globex" tenant, onboard Employee B with join date 2026-07-01 | Employee created. |
| 6 | Trigger accrual for "globex" Employee B | Balance = 15.00 days (9 months remaining in Apr 2026 - Mar 2027 year). |
| 7 | Verify that an entitlement rule with `effective_from = 2026-01-01` in "acme" applies for the 2026 calendar year | Rule is active for Jan-Dec 2026. |
| 8 | Verify that an entitlement rule with `effective_from = 2026-04-01` in "globex" applies for the 2026-2027 fiscal year | Rule is active for Apr 2026 - Mar 2027. |
| 9 | In "globex", create entitlement rule with `effective_from = 2027-04-01` and increased days (25 days) | Rule saved but not yet active (future leave year). |
| 10 | Verify current "globex" entitlement still uses the 2026-04-01 rule, not the future rule | Current balance calculations use the active rule for the current leave year. |

## 6. Postconditions
- Each tenant's leave year configuration is respected in all entitlement calculations.
- Pro-rata calculations use the tenant-specific leave year boundaries.
- Future leave year rules do not affect current year calculations.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

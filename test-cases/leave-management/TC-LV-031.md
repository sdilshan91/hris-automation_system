---
id: TC-LV-031
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-031: Part-time FTE proration -- entitlement proportional to FTE ratio (DEFERRED)

## 1. Test Objective
Verify that part-time employees receive entitlement proportional to their FTE (full-time equivalent) ratio. An employee at 0.5 FTE with a 20-day entitlement should receive 10 days.

**DEFERRED:** This test case is deferred because the Employee entity does not currently have an FTE field. The `employment_type` dimension (full-time/part-time/contract) exists on entitlement rules (FR-1), but the FTE ratio field on the employee record has not been implemented. When the FTE field is added to the Employee entity, this test case should be activated.

## 2. Related Requirements
- User Story: US-LV-002
- Business Rules: BR-2
- Functional Requirements: FR-1

## 3. Preconditions
- **DEFERRED** -- FTE field must exist on the Employee entity.
- Tenant "acme" exists with status `active`.
- Leave type "Annual Leave" exists with entitlement rule = 20 days.
- Employee "Part-Time Pat" has employment_type = "Part-Time" and FTE = 0.5.
- Employee "Full-Time Frank" has employment_type = "Full-Time" and FTE = 1.0.

## 4. Test Data
| Employee | Employment Type | FTE | Rule Days | Expected Entitlement |
|----------|----------------|-----|-----------|---------------------|
| Part-Time Pat | Part-Time | 0.50 | 20.00 | 10.00 |
| Half-Time Hana | Part-Time | 0.60 | 20.00 | 12.00 |
| Full-Time Frank | Full-Time | 1.00 | 20.00 | 20.00 |
| Minimal Mia | Part-Time | 0.20 | 20.00 | 4.00 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **DEFERRED** -- Create or verify Employee "Part-Time Pat" with FTE = 0.5, employment_type = "Part-Time" | Employee exists with FTE field populated. |
| 2 | **DEFERRED** -- Create entitlement rule for "Annual Leave" = 20 days (applies to all employment types or specifically part-time) | Rule saved. |
| 3 | **DEFERRED** -- Trigger accrual calculation for Part-Time Pat | Balance = 10.00 days (20 * 0.5 FTE). |
| 4 | **DEFERRED** -- Trigger accrual calculation for Half-Time Hana (FTE = 0.6) | Balance = 12.00 days (20 * 0.6 FTE). |
| 5 | **DEFERRED** -- Trigger accrual calculation for Full-Time Frank (FTE = 1.0) | Balance = 20.00 days (no FTE reduction). |
| 6 | **DEFERRED** -- Verify FTE proration is applied AFTER rule specificity resolution | If an override exists, FTE is applied to the override value, not the rule value. |
| 7 | **DEFERRED** -- Verify FTE proration combined with pro-rata: Part-Time Pat joining July 1 | Balance = 5.00 days (20 * 0.5 FTE * 6/12 months). |

## 6. Postconditions
- **DEFERRED** -- Part-time employees receive FTE-proportioned entitlements.
- FTE proration is multiplicative with pro-rata (mid-year join) calculations.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

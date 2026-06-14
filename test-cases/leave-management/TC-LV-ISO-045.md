---
id: TC-LV-ISO-045
user_story: US-LV-012
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-045: HR in Tenant A cannot see Tenant B data in any leave report (NFR-3, BR-1, Test Hint)

## 1. Test Objective
Verify the Test Hint / BR-1 / NFR-3: an HR Officer authenticated in Tenant A sees only Tenant A's employees and leave data in every report and analytics view; no Tenant B employee, balance, utilization, absenteeism, or trend datapoint is ever aggregated or listed. No cross-tenant aggregation occurs (system-admin cross-tenant reports are a separate concern).

## 2. Related Requirements
- User Story: US-LV-012
- Business Rules: BR-1
- Non-Functional Requirements: NFR-3
- Test Hint: tenant isolation in reports

## 3. Preconditions
- Tenant "acme" (HR Mark) and Tenant "globex" (employees with leave data), each with distinct employees, departments, and leave/ledger entries.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | caller's tenant |
| Tenant B | globex | must be invisible |
| Reports | balance-summary, utilization, absenteeism, trend | all report types |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As acme HR, run the Balance Summary report | Only acme employees and balances appear; no globex employee/balance is present. |
| 2 | Run Utilization and Absenteeism reports | Totals, averages, department breakdowns, and absentee rankings are computed over acme only — globex contributes nothing. |
| 3 | Run Trend Analysis | The 12-month/YoY series sums acme data only; no globex months/values bleed in. |
| 4 | Export each report | Exported files contain acme rows only — confirming no cross-tenant data leaks through the export path either. |

## 6. Postconditions
- Every report/analytics/export is strictly Tenant A-scoped; no cross-tenant data is visible or aggregated.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

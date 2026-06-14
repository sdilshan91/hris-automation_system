---
id: TC-LV-232
user_story: US-LV-012
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-232: Leave Balance Summary report — per-employee balance per leave type, filterable, exportable (AC-1)

## 1. Test Objective
Verify AC-1: selecting the "Leave Balance Summary" report renders a table of all (tenant) employees with their current balance for each leave type, that the table is filterable by department, job level, and employment type, and that it is exportable to CSV/Excel.

## 2. Related Requirements
- User Story: US-LV-012
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2, FR-4, FR-6
- Cross-ref: US-LV-006 (balance dashboard), US-LV-002 (entitlement engine)

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated with `Leave.Reports`/`HR.Officer`.
- ≥1 completed leave cycle of data; employees across ≥2 departments, ≥1 job level, mixed employment types.
- Leave types configured (e.g. Annual, Sick, Casual).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Report type | balance-summary | `GET /api/v1/leaves/reports/balance-summary` |
| Employees | ≥10 across 2 depts | with ledger/entitlement data |
| Leave types | Annual, Sick, Casual | columns / per-type balance |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Leave Reports and open "Leave Balance Summary" | A table renders one row per employee with a balance column per leave type (entitlement/used/remaining or remaining per type). |
| 2 | Inspect a sample employee's per-type balance | Each leave type's current balance is shown for that employee, tenant-scoped only. |
| 3 | Apply a department filter, then a job-level filter, then an employment-type filter | The table narrows to the matching employees for each filter (combinable); FR-2 filters honored server-side. |
| 4 | Click Export → CSV, then Export → Excel | A file downloads in each format with employees × leave-type balances (header + data); see TC-LV-239 for header/data assertion. |

## 6. Postconditions
- Balance Summary report lists tenant employees with per-type balances, filterable by department/job level/employment type, and exportable to CSV/Excel.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

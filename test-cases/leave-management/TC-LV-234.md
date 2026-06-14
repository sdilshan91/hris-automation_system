---
id: TC-LV-234
user_story: US-LV-012
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-234: Leave Utilization Report — totals by type, avg utilization %, department breakdown with charts (AC-2)

## 1. Test Objective
Verify AC-2: the "Leave Utilization Report" for a date range and department shows total leaves taken by leave type, an average utilization percentage, and a per-department breakdown rendered with bar/pie charts (FR-7 chart data API).

## 2. Related Requirements
- User Story: US-LV-012
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2, FR-7
- Non-Functional Requirements: NFR-4 (client-side OSS charts)

## 3. Preconditions
- Tenant "acme"; HR authenticated; approved leave + ledger data across ≥2 departments and ≥2 leave types within a known date range.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Report type | utilization | `GET /api/v1/leaves/reports/utilization` |
| Chart data | `GET /api/v1/leaves/analytics/utilization` | FR-7 aggregated payload |
| Range | 2026-01-01 .. 2026-06-30 | date filter |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Utilization Report, set the date range and department | Report returns totals of leaves taken grouped BY leave type (SUM of days / COUNT of requests). |
| 2 | Inspect the average utilization percentage summary card | An average utilization % is shown (used ÷ entitlement across the cohort); see TC-LV-235 for the exact math. |
| 3 | Inspect the department breakdown section | A per-department breakdown is listed and a bar and/or pie chart renders the same aggregated values (client-side OSS chart, NFR-4). |
| 4 | Change the department filter to a single department | Totals, average %, and charts recompute for that department only. |

## 6. Postconditions
- Utilization report shows per-type totals, average utilization %, and a department breakdown with charts for the selected range.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

---
id: TC-ATT-131
user_story: US-ATT-010
module: Attendance
priority: high
type: functional
status: pass
created: 2026-06-15
---

# TC-ATT-131: Department attendance comparison -- per-department attendance rate + color thresholds (green >90% / amber 80-90% / red <80%) + drill-down

## 1. Test Objective
Verify the departmental attendance comparison report (AC-3, FR-3): `GET /api/v1/attendance/reports/department-comparison?month=` returns the attendance rate per department for the selected period, each rate mapped to the §8 color band (green > 90%, amber 80-90%, red < 80%), with drill-down from a department to its employees. The attendance-rate math per department is verified against seeded data.

## 2. Related Requirements
- User Story: US-ATT-010
- Acceptance Criteria: AC-3 (attendance rates by department as a bar chart with drill-down)
- Functional Requirements: FR-3 (pre-built Departmental Comparison report)
- UI/UX: §8 (horizontal bar chart, color-coded: green > 90%, amber 80-90%, red < 80%)
- API: GET /api/v1/attendance/reports/department-comparison?month=2026-05

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated with `Reports.View.All`.
- Three departments with generated monthly summaries for 2026-05: Engineering (rate 95%), Sales (rate 85%), Support (rate 76%).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | period |
| Engineering | 95% | green (> 90%) |
| Sales | 85% | amber (80-90%) |
| Support | 76% | red (< 80%) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /reports/department-comparison?month=2026-05` | 200 OK; one entry per department with department name, attendance_rate, and the employee count behind the rate. |
| 2 | Verify Engineering | attendance_rate = 95.0, color band = green (> 90% per §8). |
| 3 | Verify Sales | attendance_rate = 85.0, color band = amber (80-90% inclusive per §8). |
| 4 | Verify Support | attendance_rate = 76.0, color band = red (< 80% per §8). |
| 5 | Verify the rate math | each department rate = sum(present-equivalent days) / sum(expected working days) across its employees for the period -- reconciles to the seeded summaries (not a simple headcount average). |
| 6 | Color-band boundaries | exactly 90.0% maps to amber (boundary inclusive at the low edge of green is ">90", so 90.0 is amber); exactly 80.0% maps to amber (">=80"); 79.9% maps to red -- band edges classified per the §8 thresholds. |
| 7 | Drill-down on a department (AC-3) | drilling into Support returns its per-employee attendance rows for the period (the drill-down dataset that backs the bar-chart click), tenant- and department-scoped. |
| 8 | Department with no employees / no data | returns the department with a 0 / "no data" indicator (not omitted silently or divide-by-zero). |

## 6. Postconditions
- The comparison returns correct per-department attendance rates with §8 color bands and a drill-down dataset; no data mutated.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Department + employee data sourced from Core HR; the comparison aggregates the US-ATT-007 monthly summaries by department. Empty-department handling and the exact rate denominator (expected working days vs calendar days) confirmed against the backend aggregator. **Reported to caller.**
- The color band is conveyed by more than color in the UI (the rate value + a text/icon label) -- a11y verified in TC-ATT-141 (text-not-color).
- The horizontal-bar-chart rendering + accessible alternative is in TC-ATT-141; tenant isolation (a department-comparison must never sum across tenants) in TC-ATT-ISO-013; manager team-scope in TC-ATT-137.

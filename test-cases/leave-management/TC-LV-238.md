---
id: TC-LV-238
user_story: US-LV-012
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-238: Leave Trend Analysis — 12-month monthly trends by type with year-over-year comparison (AC-4)

## 1. Test Objective
Verify AC-4: the "Leave Trend Analysis" dashboard renders line charts of monthly leave trends by leave type over the past 12 months, with a year-over-year comparison capability.

## 2. Related Requirements
- User Story: US-LV-012
- Acceptance Criteria: AC-4
- Functional Requirements: FR-1, FR-7
- Non-Functional Requirements: NFR-4 (OSS charts)

## 3. Preconditions
- Tenant "acme"; HR authenticated; ≥13 months of leave data so a prior-year comparison exists for at least some months.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Chart data | `GET /api/v1/leaves/analytics/trend` | monthly buckets by type |
| Window | last 12 months | rolling |
| YoY | current vs prior year | comparison series |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Leave Trend Analysis dashboard | Line chart(s) show monthly leave totals bucketed by leave type across the trailing 12 months (GROUP BY month, leave_type). |
| 2 | Enable the year-over-year comparison | A prior-year series is overlaid/aligned per month, allowing current vs previous year comparison. |
| 3 | Hover/tap a month data point | The per-type value (and YoY delta when enabled) is shown; mobile uses tap-for-details (no hover). |
| 4 | Verify boundary: a leave type with no data in some months | Those months render as 0 (continuous line), not gaps/errors. |

## 6. Postconditions
- Trend Analysis renders 12-month monthly trends by type with a year-over-year comparison.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

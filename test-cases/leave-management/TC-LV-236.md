---
id: TC-LV-236
user_story: US-LV-012
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-236: Absenteeism Report — top absentees (unplanned + LOP), trend lines, flagged employees (AC-3)

## 1. Test Objective
Verify AC-3: the "Absenteeism Report" for a date range lists employees with the highest absenteeism (unplanned leave + LOP), shows trend lines over the period, and flags employees exceeding the configurable threshold.

## 2. Related Requirements
- User Story: US-LV-012
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1, FR-7
- Business Rules: BR-4 (tenant-configurable threshold)
- Cross-ref: US-LV-011 (LOP)

## 3. Preconditions
- Tenant "acme"; HR authenticated; data with a mix of unplanned leave (e.g. unplanned/sick taken without notice per tenant definition) and LOP entries (US-LV-011) across several employees in the range.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Report type | absenteeism | `GET /api/v1/leaves/reports/absenteeism` |
| Chart data | `GET /api/v1/leaves/analytics/absenteeism-trend` | trend lines |
| Threshold | 3 (default) | BR-4 tenant-configurable |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Absenteeism Report and set the date range | Employees are ranked by absenteeism count = unplanned-leave days + LOP days; highest first. |
| 2 | Inspect the per-employee count composition | Both unplanned leave and LOP contribute to the absenteeism count (not just one source). |
| 3 | Inspect the trend section | Trend lines over the selected period render (FR-7 aggregated), showing absenteeism over time. |
| 4 | Inspect flagging | Employees over the tenant threshold are visually flagged (badge/highlight); see TC-LV-237 for the exact 4-vs-3 boundary. |

## 6. Postconditions
- Absenteeism report ranks unplanned+LOP absentees, shows trend lines, and flags over-threshold employees.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

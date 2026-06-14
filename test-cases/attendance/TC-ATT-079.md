---
id: TC-ATT-079
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-079: Monthly overtime report -- HR sees approved/pending/rejected overtime summarised by employee for the selected month (happy path)

## 1. Test Objective
Verify AC-5: an HR Officer requesting the monthly overtime report (`GET /api/v1/attendance/overtime/report?month=YYYY-MM`) gets a per-employee summary of approved, pending, and rejected overtime for the selected month, tenant-scoped, with sortable columns and an export action (§8).

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-5
- Functional Requirements: FR-2 (records by employee/date/minutes/status feed the report)

## 3. Preconditions
- Tenant "acme". HR Officer "Priya" authenticated with the overtime-report permission.
- For the month 2026-06: Asha has APPROVED 240 + PENDING 60; Carl has REJECTED 90 + APPROVED 120; Dana has none.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-06 | selected period |
| Asha | approved 240, pending 60, rejected 0 | |
| Carl | approved 120, pending 0, rejected 90 | |
| Dana | all zero | included or omitted per report rule |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `GET /overtime/report?month=2026-06` | 200; a per-employee summary: Asha approved=240/pending=60/rejected=0; Carl approved=120/pending=0/rejected=90; tenant-scoped to acme. |
| 2 | Verify the status breakdown columns | Each employee row shows approved, pending, and rejected totals (minutes/hours) for the month (AC-5). |
| 3 | Verify month scoping | Records dated outside 2026-06 are excluded; a record dated 2026-05-31 does not appear, 2026-06-01 does (month-boundary). |
| 4 | Request a month with no overtime | Returns an empty/zeroed summary, no error. |
| 5 | Export | The report exports (CSV/XLSX) honoring the selected month and any filters (§8 export button). |
| 6 | Sort by a column (e.g. approved desc) | Rows reorder correctly (§8 sortable columns). |

## 6. Postconditions
- HR obtains a correct, tenant-scoped monthly overtime summary by employee with status breakdown, sort, and export.

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
- Month-boundary classification uses the same date semantics as detection (UTC day per the attendance vault, pending tenant-timezone infra). If the report should use tenant-local month boundaries, that is the same deferred tenant-timezone concern noted module-wide. **Reported to caller.**
- Whether UNAPPROVED records appear in the report (e.g. as a fourth column or rolled into a needs-review view) follows the report's documented definition; assert against it and flag to the BA if unspecified.

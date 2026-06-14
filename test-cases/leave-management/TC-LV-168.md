---
id: TC-LV-168
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-168: Preview filters and dashboard line items -- carry-forward/forfeiture shown by department/employee/leave type and as dashboard entries (AC-5, Section 8; US-LV-006 integration)

## 1. Test Objective
Verify the preview report's filters (by department, employee, leave type) work as specified, and that carry-forward and expired amounts surface as separate line items on the employee leave balance dashboard (US-LV-006), with the "expiring-soon" indicator for carry-forward days (AC-5, Section 8).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-5
- UI/UX Notes: Section 8 (preview filters; dashboard line items; expiring-soon amber indicator)
- Cross-reference: US-LV-006 (leave balance dashboard line items: carryForward, expired)

## 3. Preconditions
- Tenant "acme" with multiple departments/employees/leave types; HR Officer "Priya" with leave-config permission.
- Employee "Sam" has a carry-forward of 5 Annual Leave days expiring 2027-03-31 after the year-end job has run.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Preview filters | department, employee, leave type | AC-5 |
| Dashboard line items | carryForward, expired | US-LV-006 FR-2 |
| Expiring-soon | "5 carry-forward days expiring on March 31" | amber, Section 8 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In the preview report, filter by a specific department | Only employees in that department appear, with correct projected carry-forward/forfeiture (AC-5). |
| 2 | Filter by employee and by leave type | The projection narrows accordingly; figures remain consistent with the unfiltered totals for the matching rows. |
| 3 | Open Sam's leave balance dashboard (US-LV-006) after the year-end job | Carry-forward (+5) and expired amounts appear as separate line items; carry-forward is shown in blue, expired in gray strikethrough (Section 8). |
| 4 | Verify the expiring-soon indicator | The dashboard shows an amber "5 carry-forward days expiring on March 31" indicator for the soon-to-expire carry-forward balance (Section 8). |

## 6. Postconditions
- Preview filtering works per AC-5; carry-forward and expired surface as distinct dashboard line items with an expiring-soon cue.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

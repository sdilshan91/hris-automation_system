---
id: TC-LV-131
user_story: US-LV-007
module: Leave Management
priority: critical
type: integration
status: draft
created: 2026-06-14
---

# TC-LV-131: Public holiday excluded from leave day count -- Mon-Fri spanning a Wednesday holiday = 4 days (AC-2)

## 1. Test Objective
Verify the KEY integration seam with US-LV-003: when a public holiday exists on a Wednesday, an employee applying for leave Monday-Friday (inclusive) has that Wednesday excluded from the leave day count, so the authoritative `totalDays` = 4, not 5 (AC-2, FR-6, Test Hint §11). This validates the holiday-exclusion seam that US-LV-003 (TC-LV-056) left dependent on US-LV-007.

## 2. Related Requirements
- User Story: US-LV-007
- Acceptance Criteria: AC-2
- Functional Requirements: FR-6
- Cross-reference: US-LV-003 AC-6 / FR-3 (TC-LV-056)

## 3. Preconditions
- Tenant "acme" active; employee "Sam" authenticated (Leave.Apply) with sufficient Annual Leave balance.
- A 5-day work week is configured (Sat/Sun are weekends).
- A public holiday "Mid-Week Holiday" exists on Wednesday 2026-06-17 (tenant-wide, active).
- The leave-application flow uses the DB-backed `IHolidayProvider` (`GET /api/v1/holidays?from&to` seam).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Leave start | 2026-06-15 (Mon) | -- |
| Leave end | 2026-06-19 (Fri) | inclusive |
| Public holiday | 2026-06-17 (Wed) | tenant-wide, type Public |
| Expected totalDays | 4 | 5 calendar weekdays minus 1 holiday |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit a leave request for Mon 2026-06-15 through Fri 2026-06-19 | Request accepted; the backend-computed authoritative `totalDays` = 4 (the Wednesday holiday is excluded). |
| 2 | Inspect the persisted leave_request and the ledger deduction on approval | The deducted/used amount reflects 4 days, not 5. |
| 3 | Remove/deactivate the Wednesday holiday and re-submit an equivalent request | `totalDays` reverts to 5 -- confirming the exclusion is driven by the holiday row, not a coincidence. |
| 4 | Confirm only Public holidays drive this | The Wednesday holiday's type is Public; see TC-LV-132 for restricted/optional not being auto-excluded. |

## 6. Postconditions
- The leave-day calculation excludes active public holidays within the requested range.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

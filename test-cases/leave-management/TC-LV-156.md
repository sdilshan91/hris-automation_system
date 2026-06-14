---
id: TC-LV-156
user_story: US-LV-008
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-156: Preview report matches the actual job output and commits nothing (AC-5, FR-5; Test Hint)

## 1. Test Objective
Verify the read-only carry-forward preview: `GET /api/v1/leaves/carry-forward-preview?year={year}` projects each employee's carry-forward and forfeiture amounts, the projection exactly matches what `ProcessLeaveYearEndJob` actually produces, and the preview does not create, lock, or mutate any data (AC-5, FR-5, Section 10; Test Hint).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-5
- Functional Requirements: FR-5
- Assumptions/Constraints: Section 10 (preview is read-only, does not lock or commit)
- Test Hint: Section 11 (preview matches what the actual job would produce)

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" with leave-config permission.
- A deterministic fixture: employees with known year-end unused balances and per-type `carry_forward_limit` (incl. Sam: 8 unused, limit 5 -> 5 cf / 3 forfeit).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Preview endpoint | GET /api/v1/leaves/carry-forward-preview?year=2026 | read-only |
| Sam projected carry-forward | 5 | matches AC-2 |
| Sam projected forfeiture | 3 | matches AC-2 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Priya calls the preview endpoint for year 2026 | Returns a per-employee projection: each row shows projected carry-forward and forfeiture (Sam: 5 carry-forward, 3 forfeit) (AC-5, FR-5). |
| 2 | Snapshot the ledger and tracking tables, then run the actual `ProcessLeaveYearEndJob` | The actual `carry_forward`/`expired` ledger entries match the preview row-for-row (Sam: +5 / -3) (Test Hint). |
| 3 | Diff the DB before vs immediately after the preview call (step 1) | No rows created/updated/deleted by the preview; no period lock taken (AC-5 read-only, Section 10). |
| 4 | Re-run the preview after the job | Preview reflects the now-processed state consistently (still no mutation). |

## 6. Postconditions
- Preview projection equals actual job output; preview leaves the database unchanged.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

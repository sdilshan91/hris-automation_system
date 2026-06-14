---
id: TC-LV-135
user_story: US-LV-007
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-135: CSV import flags duplicate dates as row-level validation errors (AC-3, BR-1, Test Hint)

## 1. Test Objective
Verify that the CSV import flags duplicate dates for review rather than silently skipping or creating duplicates -- both in-file duplicates (two rows same date) and DB duplicates (a row matching an existing tenant holiday). Valid rows still import; duplicate rows appear in the error report (AC-3, BR-1, Test Hint §11).

## 2. Related Requirements
- User Story: US-LV-007
- Acceptance Criteria: AC-3
- Business Rules: BR-1
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" active; HR Officer "Priya" authenticated with `Holiday.Import`.
- An existing tenant-wide holiday on 2026-01-01 already in the DB.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Row 1 | "New Year", 2026-01-01, public | DB duplicate (already exists) |
| Row 2 | "Labour Day", 2026-05-01, public | valid |
| Row 3 | "Labour Day (dup)", 2026-05-01, public | in-file duplicate of Row 2 |
| Row 4 | "Christmas", 2026-12-25, public | valid |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST `/api/v1/holidays/import` with the 4-row CSV | 200 OK with a summary: Row 2 and Row 4 created; Row 1 and Row 3 flagged as errors ("A holiday already exists on 2026-..."). |
| 2 | Inspect the error report rows | Each duplicate carries its row number + "date" field + a human-readable duplicate message. |
| 3 | GET `/api/v1/holidays?year=2026` | Exactly one holiday on 2026-01-01 (original) and one on 2026-05-01 (Row 2) -- no duplicates created. |
| 4 | Re-run the same import | Now all four rows are duplicates and reported as such; createdCount = 0 (idempotent on re-run). |

## 6. Postconditions
- No duplicate holidays exist; duplicate rows are surfaced for HR review, valid rows imported.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

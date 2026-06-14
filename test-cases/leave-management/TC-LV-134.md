---
id: TC-LV-134
user_story: US-LV-007
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-134: CSV import of a valid 20-row holiday file creates all 20 holidays (AC-3, FR-4)

## 1. Test Objective
Verify the CSV import endpoint bulk-creates holidays from a valid file (columns: name, date, type), returns a summary with the created count and zero row errors, and persists all rows tenant-scoped (AC-3, FR-4, Test Hint §11).

## 2. Related Requirements
- User Story: US-LV-007
- Acceptance Criteria: AC-3
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" active; HR Officer "Priya" authenticated with `Holiday.Import` / `Holiday.View`.
- A valid CSV with 20 distinct holidays (unique dates) for 2026.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Columns | name,date,type | header row |
| Rows | 20 valid, distinct dates | e.g. 2026-01-01 .. 2026-12-25 |
| File type | text/csv | multipart upload |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | POST `/api/v1/holidays/import` (multipart) with the 20-row CSV | 200 OK; `HolidayImportResult` reports createdCount = 20, errors = empty. |
| 2 | GET `/api/v1/holidays?year=2026` | All 20 imported holidays are listed, tenant-stamped, active. |
| 3 | Verify type mapping | Each row's `type` (public/restricted/optional) maps to the correct `HolidayType`; an invalid type value is reported as a row error (not silently defaulted). |
| 4 | Submit empty/no file | 400 Bad Request "No file uploaded." (guard). |

## 6. Postconditions
- 20 tenant-scoped holidays created from one CSV import.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

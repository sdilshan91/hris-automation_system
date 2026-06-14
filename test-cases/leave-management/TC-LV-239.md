---
id: TC-LV-239
user_story: US-LV-012
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-239: Synchronous CSV/Excel export — 100-row report has correct headers and data (AC-5, Test Hint)

## 1. Test Objective
Verify the AC-5 Test Hint: generating a 100-row report and exporting it as CSV and Excel (XLSX) produces a downloaded file containing the correct column headers and the same data shown on screen (FR-4, ClosedXML/OSS library).

## 2. Related Requirements
- User Story: US-LV-012
- Acceptance Criteria: AC-5
- Functional Requirements: FR-4
- Non-Functional Requirements: NFR-2 (≤5,000 rows synchronous)

## 3. Preconditions
- Tenant "acme"; HR authenticated; a report (e.g. Balance Summary) yielding exactly 100 rows.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Rows | 100 | under the 5,000 sync threshold |
| Formats | CSV, XLSX | FR-4 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run a report returning 100 rows and click Export → CSV | A CSV file downloads synchronously (no background job for ≤5,000 rows). |
| 2 | Open the CSV | The header row matches the on-screen columns; 100 data rows are present with values equal to the table (and respecting active filters/sort). |
| 3 | Click Export → Excel (XLSX) | An XLSX file downloads with the same headers and 100 data rows; numeric/date cells are typed correctly. |
| 4 | Verify filtered export | Apply a filter so 30 rows show, export again | The export contains only the 30 filtered rows, not all 100 (export honors current filters). |

## 6. Postconditions
- CSV and Excel exports contain correct headers and the filtered/sorted data for the 100-row report.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

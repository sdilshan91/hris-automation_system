---
id: TC-ATT-159
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: automated
created: 2026-07-19
automated: 2026-07-19
defect:
  - ISSUE-081
---

# TC-ATT-159: Monthly overtime report CSV export — BOM-encoded file with totals row; blank format defaults to CSV; unsupported format is 400 (ISSUE-081, AC-5)

## 1. Test Objective
Verify the ISSUE-081 fix on US-ATT-006 AC-5: the monthly overtime report exposes a working CSV export (`OvertimeService.ExportMonthlyReportAsync`). The export returns a UTF-8 **BOM-encoded** `text/csv` file named `overtime-report-{yyyy}-{MM}.csv` containing the per-employee rows plus a trailing **totals** row; a blank/whitespace format falls back to CSV; and a genuinely different but understood format (e.g. `xlsx`) is **rejected with 400 `unsupported_format`** rather than silently downgraded.

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-5 (HR views the monthly overtime report — summary of approved/pending/rejected overtime by employee; export button)
- Finding: ISSUE-081 (PR #371)

## 3. Preconditions
- A tenant with overtime records for the reporting month (mixed statuses).
- Uses the EF Core InMemory provider through `OvertimeService` (mirrors `ShiftServiceTests`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Report period | 2026-06 | year/month args |
| Records | Approved (120/100), Pending (60) | drive the totals row |
| UTF-8 BOM | `EF BB BF` | Excel auto-detects encoding |
| Totals row | `Total,100,60,...` | Approved=100, Pending=60 |
| Unsupported format | `xlsx` | must 400, not downgrade |

## 5. Test Steps
| Step | Action | Expected Result | Automated by |
|------|--------|-----------------|--------------|
| 1 | Call `ExportMonthlyReportAsync(2026, 6, format: null)`. | Success; `ContentType = text/csv`; `FileName = overtime-report-2026-06.csv`; content **starts with** the UTF-8 BOM; body contains the `Employee`/`John Doe` header+row and a trailing `Total,100,60,` totals row. | `OvertimeReportAndDtoTests.ExportMonthlyReport_Csv_ReturnsBomEncodedFile_WithTotalsRow` |
| 2 | Call `ExportMonthlyReportAsync(2026, 6, format: "  ")` (blank/whitespace). | Success; defaults to `ContentType = text/csv`. | `OvertimeReportAndDtoTests.ExportMonthlyReport_BlankFormat_DefaultsToCsv` |
| 3 | Call `ExportMonthlyReportAsync(2026, 6, format: "xlsx")`. | Failure; status code **400**, `ErrorCode = unsupported_format` — rejected, not silently downgraded to CSV. | `OvertimeReportAndDtoTests.ExportMonthlyReport_UnsupportedFormat_Is400` |

## 6. Postconditions
- The overtime report export produces a spreadsheet-friendly CSV with a totals row; unsupported formats are cleanly rejected.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test (blank-format default)
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## Automation & Traceability
- **Automated-by (green in the xUnit suite, EF Core InMemory through the real service):**
  - `OvertimeReportAndDtoTests.ExportMonthlyReport_Csv_ReturnsBomEncodedFile_WithTotalsRow`
  - `OvertimeReportAndDtoTests.ExportMonthlyReport_BlankFormat_DefaultsToCsv`
  - `OvertimeReportAndDtoTests.ExportMonthlyReport_UnsupportedFormat_Is400`
- Backing suite trait: `[Trait("TC", "TC-ATT-159")]`.

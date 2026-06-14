---
id: TC-ATT-087
user_story: US-ATT-007
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-087: Export the monthly summary in CSV, Excel (.xlsx), and PDF -- data accuracy and correct format (happy path)

## 1. Test Objective
Verify AC-4/FR-6: the HR Officer can export the monthly summary in CSV, Excel (.xlsx via ClosedXML), and PDF (QuestPDF) (`GET /api/v1/attendance/summary/monthly/export?month=YYYY-MM&format=csv|xlsx|pdf`); each file downloads with all summary data for the selected month, the values match the on-screen table exactly, and the file is in the correct format, honoring any active filters.

## 2. Related Requirements
- User Story: US-ATT-007
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6 (CSV, Excel via ClosedXML, PDF via QuestPDF)

## 3. Preconditions
- Tenant "acme". HR Officer "Priya" authenticated with `Attendance.Read.All`.
- Month 2026-05 summary generated with the varied dataset of TC-ATT-084 (Asha, Carl, Dana).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | 2026-05 | selected period |
| formats | csv, xlsx, pdf | all three |
| Asha row | present=20, absent=1, late=3, OT=6h, leave=1 | accuracy check |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `GET /summary/monthly/export?month=2026-05&format=csv` | A CSV downloads with a header row and one data row per employee; Asha's values match TC-ATT-084 (present=20, absent=1, late=3, OT hours=6, leave=1) exactly; Content-Type is text/csv. |
| 2 | `GET ...&format=xlsx` | A valid .xlsx (ClosedXML) downloads, opens in a spreadsheet app, has the same columns/rows/values as the CSV; Content-Type is the spreadsheet MIME type. |
| 3 | `GET ...&format=pdf` | A valid PDF (QuestPDF) downloads and renders the summary table with the same data; Content-Type is application/pdf. |
| 4 | Apply a department filter, then export | The exported file contains only the filtered department's employees (export honors active filters, §8). |
| 5 | Verify minute-accurate values | Work/overtime hours in every format match the stored minute totals converted to hours (NFR-5). |
| 6 | Request an unsupported/invalid format | Rejected with a clear validation error (not a 500). |

## 6. Postconditions
- HR obtains CSV, XLSX, and PDF exports whose data exactly matches the on-screen, tenant-scoped, filtered summary; each is in a valid file format.

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
- This TC covers the SYNCHRONOUS export path (small/medium tenant, <= 1,000 employees). The asynchronous large-export path (> 1,000 employees via Hangfire + download-link notification, FR-7) is covered by TC-ATT-095.
- Export must be tenant-scoped; cross-tenant export isolation is covered by TC-ATT-ISO-010.

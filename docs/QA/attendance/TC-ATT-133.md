---
id: TC-ATT-133
user_story: US-ATT-010
module: Attendance
priority: high
type: functional
status: pass
created: 2026-06-15
---

# TC-ATT-133: Report export -- CSV / Excel (.xlsx) / PDF download with content matching the on-screen filtered report

## 1. Test Objective
Verify report export (FR-5): `GET /api/v1/attendance/reports/custom/export?format=` (and the equivalent export for the pre-built reports) downloads the report as CSV, Excel (.xlsx), or PDF, with headers + data matching the on-screen filtered report and honoring all active filters.

## 2. Related Requirements
- User Story: US-ATT-010
- Functional Requirements: FR-5 (all reports export in CSV, Excel (.xlsx), and PDF)
- API: GET /api/v1/attendance/reports/custom/export?format=CSV|XLSX|PDF&from=&to=&...filters

## 3. Preconditions
- Tenant "acme"; HR Officer authenticated with `Reports.View.All`.
- A custom report over 2026-05-01..2026-05-14 for ~100 employees with a department filter applied (the same filter set verified in TC-ATT-132).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| format | CSV / XLSX / PDF | all three |
| range | 2026-05-01..2026-05-14 | report range |
| filter | departmentId=Engineering | must be honored in export |
| employees | ~100 | representative size |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `GET /reports/custom/export?format=CSV&from=2026-05-01&to=2026-05-14&departmentId=Engineering` | 200 OK; `Content-Type: text/csv`, `Content-Disposition: attachment` with a sensible filename; rows = the on-screen Engineering report rows, header row present. |
| 2 | `format=XLSX` | 200 OK; `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`; a valid .xlsx (e.g. ClosedXML) with the same columns/rows + a header row. |
| 3 | `format=PDF` | 200 OK; `application/pdf`; a rendered PDF (e.g. QuestPDF) with the report title, period, filter summary, and the tabular data. |
| 4 | Content parity | the exported data exactly matches the on-screen filtered report (same employees, same daily rows, same totals) -- no extra/missing rows, filters honored. |
| 5 | Unsupported `format` (e.g. `format=XML`) | 400 validation error; no file returned. |
| 6 | Export of a pre-built report (e.g. departmental / late / overtime) | the same three formats are offered and produce matching content (FR-5 applies to all reports). |
| 7 | Large export (above the sync threshold) | routed to the Hangfire async path returning a queued response with a download-link SEAM -- consistent with US-ATT-007 TC-ATT-095 (delivery DEFERRED on US-NTF). |

## 6. Postconditions
- Exports download in all three formats with content matching the filtered report; no data mutated.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Performance test
- [ ] Multi-tenant isolation
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Excel uses ClosedXML and PDF uses QuestPDF per the module precedent (US-ATT-007 TC-ATT-087); confirm the libraries against the backend export implementation. **Reported to caller.**
- Large/background export (> sync threshold) routes to a Hangfire job with a download-link notification SEAM -- delivery DEFERRED on US-NTF, blob-persistence CONDITIONAL on Blob Storage (mirrors US-ATT-007 TC-ATT-095 / US-LV-012 TC-LV-240). **Reported to caller.**
- The export must be tenant-scoped (no cross-tenant rows in any format) -- covered by TC-ATT-ISO-013; export performance (5,000-employee report) folded into TC-ATT-139.

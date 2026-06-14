---
id: TC-LV-249
user_story: US-LV-012
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-249: Synchronous export of up to 5,000 rows completes within 10s (NFR-2)

## 1. Test Objective
Verify NFR-2: a synchronous CSV/Excel export for up to 5,000 rows completes within 10 seconds; beyond 5,000 rows the export is deferred to the background (cross-ref TC-LV-240).

## 2. Related Requirements
- User Story: US-LV-012
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-4, FR-5

## 3. Preconditions
- Tenant "acme"; a report yielding exactly 5,000 rows.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Rows | 5,000 | sync ceiling |
| Target | ≤ 10,000 ms | synchronous generation |
| Formats | CSV, XLSX | FR-4 (ClosedXML) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Export a 5,000-row report to XLSX | The file is generated and downloaded synchronously within 10 s. |
| 2 | Export the same to CSV | Generated within 10 s (CSV typically faster than XLSX). |
| 3 | Boundary: 5,001 rows | Crosses into the background path (no inline 10s+ blocking; cross-ref TC-LV-240). |
| 4 | Verify memory/streaming behavior | The export streams rather than buffering the entire workbook in memory unbounded (no OOM for the 5,000-row ceiling). |

## 6. Postconditions
- Synchronous exports of ≤5,000 rows complete within 10s; larger sets defer to background.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

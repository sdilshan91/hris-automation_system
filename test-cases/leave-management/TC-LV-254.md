---
id: TC-LV-254
user_story: US-LV-012
module: Leave Management
priority: high
type: accessibility
status: draft
created: 2026-06-14
---

# TC-LV-254: Reports accessible + print-friendly; charts carry non-color cues / data labels (NFR-5, NFR-4)

## 1. Test Objective
Verify NFR-5 — reports are accessible (WCAG 2.1 AA: keyboard navigation, screen-reader labels, contrast) and printable via print-friendly CSS — and NFR-4: charts do not rely on color alone (they carry data labels, patterns, legends, or text alternatives).

## 2. Related Requirements
- User Story: US-LV-012
- Non-Functional Requirements: NFR-4, NFR-5
- UI/UX: §8 (print button, charts, table view)

## 3. Preconditions
- Tenant "acme"; HR authenticated; a report with a chart (Utilization or Trend) rendered.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Standard | WCAG 2.1 AA | a11y |
| Charts | bar/pie/line | NFR-4 OSS |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate the reports landing + a report view using keyboard only | All interactive elements (cards, filter sidebar, sort headers, export/print buttons, pagination) are reachable and operable; visible focus order is logical. |
| 2 | Use a screen reader on the results table and chart | Table has proper header semantics; the chart exposes a text alternative / data table / aria-label conveying the values (not just a canvas). |
| 3 | Inspect chart encoding | Series are distinguishable without color — via data labels, patterns/markers, or a labeled legend (NFR-4); contrast of text/UI meets AA. |
| 4 | Click Print (or print preview) | A clean print-friendly layout renders (sidebar/nav stripped, table/chart legible, no clipped columns) per the print CSS. |

## 6. Postconditions
- Reports meet WCAG 2.1 AA, are print-friendly, and charts convey data without relying on color alone.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test

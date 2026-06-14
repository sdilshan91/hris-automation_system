---
id: TC-LV-165
user_story: US-LV-008
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-165: Cross-browser compatibility for the carry-forward preview report (Section 8)

## 1. Test Objective
Verify the carry-forward preview report renders and behaves consistently across the target browsers (Chrome, Edge, Firefox, Safari): the projection table, filters, and color/text encoding for carry-forward vs expired display correctly with no layout or interaction breakage (Section 8).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-5
- UI/UX Notes: Section 8

## 3. Preconditions
- HR Officer "Priya" with leave-config permission; populated preview for tenant "acme".
- Browsers: Chrome, Edge, Firefox, Safari (latest).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browsers | Chrome, Edge, Firefox, Safari | latest |
| Filters | department, employee, leave type | per AC-5 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the preview report in each browser | The projection table and totals render identically; no broken layout or missing columns. |
| 2 | Apply the department/employee/leave-type filters in each browser | Filtering works consistently across browsers; the carry-forward/forfeiture figures update correctly. |
| 3 | Inspect color + text encoding in each browser | Carry-forward (blue) and expired (gray strikethrough) render with their text labels intact in all four browsers. |
| 4 | Resize/zoom in each browser | Responsive behavior is consistent; no browser-specific overflow or focus issues. |

## 6. Postconditions
- The preview report is visually and functionally consistent across Chrome, Edge, Firefox, and Safari.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

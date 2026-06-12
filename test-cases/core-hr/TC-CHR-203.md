---
id: TC-CHR-203
user_story: US-CHR-008
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-203: Expiry badge thresholds -- green (>30d), amber (<30d), red (<7d), red/expired

## 1. Test Objective
Verify that the document list UI renders the correct color-coded expiry badge based on the document's expiry date relative to today: green if more than 30 days away, amber if less than 30 days but more than 7 days, red if less than 7 days, and red with "Expired" label if the date has passed. Documents without an expiry date show no badge. This validates the UI/UX notes in section 8 of US-CHR-008.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-8, FR-9
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) has 5 documents with varying expiry dates.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Doc A | visa-copy.pdf | expiry_date = today + 60 days (> 30d) |
| Doc B | work-permit.pdf | expiry_date = today + 20 days (< 30d, > 7d) |
| Doc C | temp-badge.pdf | expiry_date = today + 5 days (< 7d) |
| Doc D | old-cert.pdf | expiry_date = today - 10 days (expired) |
| Doc E | contract.pdf | expiry_date = null (no expiry) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Jane Doe's Documents tab. | Document list loads with all 5 documents. |
| 2 | Inspect the expiry badge on "visa-copy.pdf" (expires in 60 days). | Badge color is **green**. Text shows the expiry date or "Expires in 60 days". |
| 3 | Inspect the expiry badge on "work-permit.pdf" (expires in 20 days). | Badge color is **amber/yellow**. Text shows the expiry date or "Expires in 20 days". |
| 4 | Inspect the expiry badge on "temp-badge.pdf" (expires in 5 days). | Badge color is **red**. Text shows the expiry date or "Expires in 5 days". |
| 5 | Inspect the expiry badge on "old-cert.pdf" (expired 10 days ago). | Badge color is **red**. Text shows "Expired" or "Expired 10 days ago". |
| 6 | Inspect "contract.pdf" (no expiry date). | No expiry badge is rendered. The expiry column is empty or shows a dash. |
| 7 | Verify badge accessibility -- screen reader announces expiry status. | Expiry badges have `aria-label` attributes with descriptive text (e.g., "Expires in 60 days" or "Expired"). |

## 6. Postconditions
- All badges render correctly based on the date thresholds.
- No data was modified.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

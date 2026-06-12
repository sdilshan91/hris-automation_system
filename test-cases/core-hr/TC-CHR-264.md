---
id: TC-CHR-264
user_story: US-CHR-010
module: Core HR
priority: high
type: accessibility
status: draft
created: 2026-06-12
---

# TC-CHR-264: WCAG 2.1 AA accessibility for bulk import wizard

## 1. Test Objective
Verify that the bulk import page meets WCAG 2.1 AA accessibility standards, including keyboard navigation, screen reader compatibility, color contrast ratios, and proper ARIA labels for the upload zone, progress bar, and error table.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-5 (responsive and accessible)

## 3. Preconditions
- Tenant "acme" exists and an HR Officer is authenticated.
- Screen reader (NVDA, VoiceOver, or equivalent) is available.
- Accessibility audit tool (axe, Lighthouse) is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the bulk import page using only the keyboard (Tab, Enter, Space, Arrow keys). | All interactive elements (template links, file picker, Import button, error table rows, download error report link) are reachable and activatable via keyboard. Focus order is logical (Step 1 -> Step 2 -> Step 3). |
| 2 | Activate the screen reader and navigate the page. | Screen reader announces: page title, step labels (e.g., "Step 1 of 3: Download Template"), download links with file type, upload zone with instructions, import button state. |
| 3 | Upload a file with errors and observe the results screen with screen reader. | Screen reader announces: summary banner text (e.g., "8 of 10 records imported"), error table with row headers (row_number, field, error), and the download error report link. |
| 4 | Verify the progress bar (for async imports) has proper ARIA attributes. | Progress bar has `role="progressbar"`, `aria-valuenow`, `aria-valuemin="0"`, `aria-valuemax="100"`, and `aria-label` describing the operation. |
| 5 | Run an automated accessibility audit (axe or Lighthouse). | No critical or serious WCAG 2.1 AA violations. Color contrast ratio >= 4.5:1 for text, >= 3:1 for large text and UI components. |
| 6 | Verify that error/success status is not conveyed by color alone. | Summary banner uses text ("imported successfully" / "records failed") in addition to green/amber/red color. Error table uses text labels. |

## 6. Postconditions
- Bulk import page passes WCAG 2.1 AA audit.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test

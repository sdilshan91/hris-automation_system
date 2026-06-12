---
id: TC-CHR-263
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-263: Responsive UI -- 360px viewport shows stacked wizard steps and file picker instead of drag-and-drop

## 1. Test Objective
Verify that the bulk import UI is fully responsive at 360px viewport width per NFR-5. The 3-step wizard stacks vertically, drag-and-drop is replaced by a file picker button, and the error table scrolls horizontally on mobile.

## 2. Related Requirements
- User Story: US-CHR-010
- Non-Functional Requirements: NFR-5
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists and an HR Officer is authenticated.
- A browser or device emulator is available at 360px viewport width.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Viewport Width | 360px | Mobile viewport |
| User Role | HR Officer | Authorized persona |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set browser viewport to 360px width. Navigate to the bulk import page. | The page loads. The 3-step cards stack vertically (not side-by-side). |
| 2 | Verify Step 1 (Download Template). | Template download links are visible and tappable. The collapsible field guide section works. |
| 3 | Verify Step 2 (Upload File). | The drag-and-drop upload zone is replaced by a prominent file picker button ("Choose File" or equivalent). No drag-and-drop zone is shown. |
| 4 | Select a small valid file via the file picker and import. | Import succeeds. Results display in Step 3. |
| 5 | Verify Step 3 with errors (use a file with some invalid rows). | The error table scrolls horizontally if columns exceed viewport width. All error data is accessible by scrolling. |
| 6 | Verify smooth transitions between steps. | Slide animation (300ms per Section 8 UI/UX Notes) is smooth and does not cause layout shift. |

## 6. Postconditions
- The import workflow is fully usable on a 360px-wide mobile viewport.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

---
id: TC-CHR-317
user_story: US-CHR-012
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-317: Cross-browser compatibility for custom fields management

## 1. Test Objective
Verify that the Custom Fields management page, creation/edit forms, reordering (drag-and-drop and arrow buttons), and custom field rendering on employee forms work correctly across Chrome, Edge, Firefox, and Safari.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with custom fields defined.
- Tenant Admin is authenticated.
- Access to Chrome (latest), Edge (latest), Firefox (latest), Safari (latest).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browsers | Chrome, Edge, Firefox, Safari | Latest versions |
| Custom Fields | 3 defined | Including dropdown, text, number types |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Custom Fields management page in each browser. | The page renders correctly with consistent layout, fonts, and spacing across all browsers. |
| 2 | Create a custom field in each browser. | The creation form works identically: tag input, type selector, save -- all functional. |
| 3 | Drag-and-drop reordering in each browser (desktop). | Drag handles work, smooth reorder animation, updated order persists. |
| 4 | Navigate to employee creation form in each browser. | Custom fields render with correct types (dropdown, text input, number input) in all browsers. |
| 5 | Verify animations (slide-over, list reorder) in each browser. | Animations play smoothly without glitches or layout shifts. |

## 6. Postconditions
- All functionality works consistently across all supported browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

---
id: TC-CHR-029
user_story: US-CHR-004
module: Core HR
priority: medium
type: accessibility
status: draft
created: 2026-06-11
---

# TC-CHR-029: Department management UI accessibility (WCAG 2.1 AA)

## 1. Test Objective
Verify that the Department management page meets WCAG 2.1 AA accessibility standards, including keyboard navigation, screen reader compatibility, color contrast, and ARIA attributes for the tree view.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-3 (responsive)
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with several departments forming a hierarchy.
- A user with Tenant Admin role is authenticated.
- Screen reader software is available (e.g., NVDA, VoiceOver).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browser | Chrome (latest) | With accessibility DevTools |
| Screen Reader | NVDA or VoiceOver | Assistive technology |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Departments page using keyboard only (Tab, Enter) | Page is reachable without mouse. Focus indicators are visible on all interactive elements. |
| 2 | Tab through the department list table | Focus moves through each row and action button in a logical order. |
| 3 | Press Enter on the "Add Department" button using keyboard | Create form/panel opens. |
| 4 | Navigate through all form fields using Tab | All fields (Name, Parent dropdown, Manager picker, Description, Status) are keyboard-accessible in order. |
| 5 | Use arrow keys in the Parent Department dropdown | Options can be navigated with arrow keys. |
| 6 | Submit the form using Enter | Form submits without requiring mouse. |
| 7 | Navigate the tree view using keyboard (arrow keys for expand/collapse, Tab for nodes) | Tree nodes are navigable. Expand/collapse works with Enter or arrow keys. Tree has `role="tree"` and nodes have `role="treeitem"`. |
| 8 | Activate a screen reader and verify page landmarks | Page has proper headings (`h1` for page title), navigation landmarks, and form labels. |
| 9 | Verify screen reader announces department names, parent relationships, and status in the table | Table has proper `<th>` headers; cells are associated with headers. |
| 10 | Verify screen reader announces tree node depth and expand/collapse state | Nodes announce "expanded" / "collapsed" state and hierarchy level. |
| 11 | Run automated contrast check (e.g., Lighthouse, axe) | All text meets 4.5:1 contrast ratio for normal text, 3:1 for large text (WCAG AA). |
| 12 | Verify the confirmation dialog for deactivation is keyboard-accessible and announced by screen reader | Dialog has `role="dialog"`, `aria-modal="true"`, and focus is trapped within the dialog. |

## 6. Postconditions
- All accessibility checks pass.
- No WCAG 2.1 AA violations found.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [ ] Cross-browser test

---
id: TC-AUTH-048
user_story: US-AUTH-006
module: Authentication
priority: medium
type: accessibility
status: draft
created: 2026-06-03
---

# TC-AUTH-048: Roles management UI accessibility (WCAG 2.1 AA)

## 1. Test Objective
Verify that the Roles management page and the custom role creation/editing form meet WCAG 2.1 AA accessibility requirements. This includes keyboard navigation through role cards and the permission tree, screen reader compatibility with ARIA attributes, focus management, and color contrast of interactive elements.

## 2. Related Requirements
- User Story: US-AUTH-006
- Acceptance Criteria: AC-1 (roles list UI), AC-2 (role creation UI)
- Functional Requirements: FR-6
- UI/UX Notes: Section 8 (card layout, permission tree, lock icon, badges)

## 3. Preconditions
- Tenant "acme" is provisioned and in `active` state.
- User `admin@acme.com` is authenticated with `Tenant Admin` role.
- The Roles management page is accessible at the expected route.
- At least one custom role and all built-in roles exist in tenant "acme".
- Browser: latest Chrome with accessibility DevTools enabled.
- Screen reader: NVDA (Windows) or VoiceOver (macOS) running.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Admin user | admin@acme.com | Tenant Admin role |
| Browser | Chrome (latest), Edge, Firefox | Cross-browser a11y check |
| Screen reader | NVDA 2024+ / VoiceOver | For ARIA verification |
| Accessibility tool | axe DevTools / Lighthouse | Automated a11y scan |
| Permission modules | Leave, Attendance, Payroll, HR, Recruitment | Permission tree groups |
| Viewports | 360px, 768px, 1024px, 1920px | Responsive a11y |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Roles management page using keyboard only (Tab, Enter). | The page is reachable without a mouse. Focus indicator is visible on interactive elements. |
| 2 | Tab through the role cards list. | Each role card receives focus in a logical order. The focus ring meets 3:1 minimum contrast ratio against the background. |
| 3 | Press Enter on a built-in role card. | The read-only permissions view opens. Screen reader announces "Built-in role, [role name], read-only". The lock icon has an `aria-label` or accompanying text ("Built-in"). |
| 4 | Press Escape to close the read-only view and Tab to a custom role card. Press Enter. | The edit form opens. Focus moves to the first editable field (role name). |
| 5 | Tab through the role creation/edit form fields: name, description, permission tree. | All form fields are reachable via Tab. Labels are programmatically associated with inputs via `for`/`id` or `aria-labelledby`. Required fields have `aria-required="true"`. |
| 6 | Navigate the permission tree using keyboard: Arrow keys to expand/collapse module groups, Space/Enter to toggle checkboxes. | Module groups expand with ArrowRight and collapse with ArrowLeft. Checkboxes toggle with Space. The tree uses `role="tree"`, `role="treeitem"`, and `aria-expanded` attributes. |
| 7 | Verify screen reader announces permission tree state changes. | When a module group is expanded, screen reader announces "expanded". When a permission checkbox is toggled, screen reader announces "checked" or "unchecked" along with the permission name. |
| 8 | Verify the "Built-in" badge on built-in role cards. | Badge has sufficient color contrast (4.5:1 for normal text). The lock icon is not purely decorative -- it has an `aria-label="Built-in role"` or equivalent. |
| 9 | Verify the 403 permission denied page accessibility. | The "You don't have permission to access this page" message is in a landmark region. The "Go to Dashboard" link is keyboard focusable and has appropriate link semantics. |
| 10 | Run an automated accessibility scan (axe DevTools or Lighthouse) on the Roles management page. | No critical or serious WCAG 2.1 AA violations. Any minor issues are documented. |
| 11 | Test at 360px viewport (mobile responsive). | Role cards stack vertically. Permission tree collapses into expandable sections. All touch targets are at least 44x44 CSS pixels. Keyboard navigation still works. |
| 12 | Verify color is not the sole means of conveying information. | Built-in vs. custom roles are distinguishable by icon + text badge, not just color. Permission states use checkmarks + color, not color alone. |
| 13 | Test with 200% browser zoom. | No content is clipped or overlapping. All text remains readable. No horizontal scrollbar on the main content. |

## 6. Postconditions
- No persistent state changes (this is an observational/navigational test).
- Accessibility audit results documented with any issues filed.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test

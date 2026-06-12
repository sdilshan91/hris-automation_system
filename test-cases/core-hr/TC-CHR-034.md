---
id: TC-CHR-034
user_story: US-CHR-004
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-034: Cross-browser compatibility (Chrome, Edge, Firefox, Safari)

## 1. Test Objective
Verify that the Department management page functions correctly across all supported browsers: Chrome, Edge, Firefox, and Safari. Core functionality (CRUD, tree view, responsive layout) must work consistently.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-3
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with departments forming a hierarchy.
- A user with Tenant Admin role is authenticated.
- All four browsers are available (latest stable versions).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Chrome | Latest stable | Primary browser |
| Edge | Latest stable | Chromium-based |
| Firefox | Latest stable | Gecko engine |
| Safari | Latest stable | WebKit engine (macOS) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In each browser: navigate to the Departments management page | Page loads with correct layout and styling. |
| 2 | In each browser: create a new department | Create form works; department is created successfully. |
| 3 | In each browser: edit a department | Edit form loads with pre-populated values; save works correctly. |
| 4 | In each browser: toggle to tree view | Tree renders with correct hierarchy; expand/collapse works. |
| 5 | In each browser: deactivate a department (zero employees) | Confirmation dialog appears and deactivation succeeds. |
| 6 | In each browser: verify slide-over panel animation (300ms ease-out) | Animation is smooth and consistent. |
| 7 | In each browser: verify searchable dropdown for Parent Department | Dropdown opens, search filters work, selection persists. |
| 8 | Document any browser-specific rendering differences | All critical functionality works; cosmetic differences (if any) are documented. |

## 6. Postconditions
- All core functionality works across all four browsers.
- Any browser-specific issues are documented.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

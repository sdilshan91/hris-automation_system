---
id: TC-CHR-191
user_story: US-CHR-007
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-191: Cross-browser compatibility for Locations management page

## 1. Test Objective
Verify that the Locations management page (list view, add/edit form, deactivation flow) works correctly across all supported browsers: Chrome, Edge, Firefox, and Safari. This validates cross-browser compatibility.

## 2. Related Requirements
- User Story: US-CHR-007
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- At least 2 locations exist in tenant "acme".
- Test browsers available: Chrome (latest), Edge (latest), Firefox (latest), Safari (latest).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Full access |
| Browsers | Chrome, Edge, Firefox, Safari | Latest stable versions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Locations page in Chrome | Page loads correctly. Card-based table is rendered. All styling (rounded-xl, shadow-sm, bg-white) is applied. |
| 2 | Create a new location in Chrome | Location created successfully. Form, dropdown, and validation work correctly. |
| 3 | Repeat steps 1-2 in Edge | Same behavior and visual rendering as Chrome. |
| 4 | Repeat steps 1-2 in Firefox | Same behavior and visual rendering. Searchable dropdowns (Time Zone, Country) work correctly with Firefox's native form controls. |
| 5 | Repeat steps 1-2 in Safari | Same behavior and visual rendering. Date/time handling and dropdown behavior are consistent. |
| 6 | Verify the Time Zone searchable dropdown in all browsers | Dropdown opens, search/filter works, selection works, keyboard navigation works consistently. |
| 7 | Verify the Country searchable dropdown with flag icons in all browsers | Flag icons render correctly. Dropdown behavior is consistent. |
| 8 | Verify form validation messages in all browsers | Inline error messages are displayed consistently across all browsers. |

## 6. Postconditions
- No browser-specific rendering or functional issues were found.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

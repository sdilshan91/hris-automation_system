---
id: TC-CHR-062
user_story: US-CHR-005
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-062: Cross-browser compatibility (Chrome, Edge, Firefox, Safari)

## 1. Test Objective
Verify that the Job Titles management page functions correctly and renders consistently across all supported browsers: Chrome, Edge, Firefox, and Safari.

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- Multiple job titles exist in the tenant.
- Latest versions of Chrome, Edge, Firefox, and Safari are available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Browser 1 | Google Chrome (latest) | Primary browser |
| Browser 2 | Microsoft Edge (latest) | Chromium-based |
| Browser 3 | Mozilla Firefox (latest) | Gecko engine |
| Browser 4 | Apple Safari (latest) | WebKit engine |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Job Titles management page in each browser | Page loads successfully in all four browsers. |
| 2 | Verify the card-based table layout renders correctly (rounded corners, shadows) | Visual appearance is consistent across browsers. |
| 3 | Click "Add Job Title" and verify the modal/slide-over opens and animates correctly | Animation is smooth in all browsers. |
| 4 | Fill out and submit the create form in each browser | Job title is created successfully from each browser. |
| 5 | Test the searchable Grade dropdown in each browser | Dropdown opens, search works, and selection is registered in all browsers. |
| 6 | Test the inline status toggle (deactivation with confirmation) in each browser | Toggle and confirmation dialog work correctly in all browsers. |
| 7 | Verify hover-revealed action icons work in each browser | Icons appear on hover in all browsers. |
| 8 | Verify the search bar filters correctly in each browser | Search produces correct results in all browsers. |

## 6. Postconditions
- The Job Titles feature is verified as cross-browser compatible.
- Any browser-specific rendering differences are documented.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

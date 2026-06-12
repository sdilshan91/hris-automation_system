---
id: TC-CHR-126
user_story: US-CHR-002
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-126: Cross-browser compatibility for employee profile page

## 1. Test Objective
Verify that the employee profile page renders and functions correctly across all supported browsers: Chrome, Edge, Firefox, and Safari.

## 2. Related Requirements
- User Story: US-CHR-002
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" exists with fully populated profile.
- Four browsers available: Chrome (latest), Edge (latest), Firefox (latest), Safari (latest).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Employee ID | {jane_doe_id} | Populated profile |
| Browsers | Chrome, Edge, Firefox, Safari | Latest versions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open Jane Doe's profile in Chrome | All sections render. Edit functionality works. Animations (fade transitions, tab indicator) play smoothly. |
| 2 | Open Jane Doe's profile in Edge | Same rendering and functionality as Chrome. No layout differences. |
| 3 | Open Jane Doe's profile in Firefox | All sections render. Edit mode transitions work. Angular Material components display correctly. |
| 4 | Open Jane Doe's profile in Safari | All sections render. Tailwind CSS classes apply correctly. No WebKit-specific layout issues. |
| 5 | In each browser: edit a field, save, verify the change persists | Edit/save cycle works identically across all browsers. |
| 6 | In each browser: verify the employment history timeline renders correctly | Timeline layout with date markers displays consistently. |

## 6. Postconditions
- Profile page works identically across all supported browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

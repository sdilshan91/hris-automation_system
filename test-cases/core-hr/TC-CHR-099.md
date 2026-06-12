---
id: TC-CHR-099
user_story: US-CHR-001
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-099: Cross-browser compatibility (Chrome, Edge, Firefox, Safari)

## 1. Test Objective
Verify that the employee creation wizard renders and functions correctly across the supported browsers: Chrome, Edge, Firefox, and Safari.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-4, NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated.
- Latest versions of Chrome, Edge, Firefox, and Safari are available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Chrome | Latest stable | Primary browser |
| Edge | Latest stable | Chromium-based |
| Firefox | Latest stable | Gecko-based |
| Safari | Latest stable | WebKit-based (macOS/iOS) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the Add Employee form in Chrome | Wizard renders correctly. All fields, transitions, and interactions work. |
| 2 | Complete the full wizard and submit in Chrome | Employee created successfully. |
| 3 | Repeat steps 1-2 in Edge | Same behavior as Chrome. |
| 4 | Repeat steps 1-2 in Firefox | Same behavior. Angular Material components render correctly. |
| 5 | Repeat steps 1-2 in Safari | Same behavior. Date pickers, file upload, and transitions work. |
| 6 | Verify the profile photo drag-and-drop works in all browsers | File upload via drag-and-drop and file picker works consistently. |
| 7 | Verify CSS transitions and animations are smooth in all browsers | Slide/fade transitions between wizard steps render correctly. |

## 6. Postconditions
- The employee creation wizard is fully functional in all supported browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

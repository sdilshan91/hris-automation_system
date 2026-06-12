---
id: TC-CHR-148
user_story: US-CHR-003
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-148: Cross-browser compatibility (Chrome, Edge, Firefox, Safari)

## 1. Test Objective
Verify that the Employee Directory page renders and functions correctly across all supported browsers: Chrome, Edge, Firefox, and Safari. All core features (search, filter, pagination, view toggle, sort, export) must work consistently.

## 2. Related Requirements
- User Story: US-CHR-003
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 25 employees exist.
- Latest stable versions of Chrome, Edge, Firefox, and Safari are available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Chrome | Latest stable | Primary browser |
| Edge | Latest stable | Chromium-based |
| Firefox | Latest stable | Gecko engine |
| Safari | Latest stable (macOS) | WebKit engine |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open Employee Directory in Chrome | Page loads correctly; cards display with correct styling, shadows, hover effects. |
| 2 | Test search, filter, pagination, sort, view toggle, and export in Chrome | All features work as specified. |
| 3 | Open Employee Directory in Edge | Same rendering and functionality as Chrome. |
| 4 | Test all features in Edge | All features work as specified. |
| 5 | Open Employee Directory in Firefox | Cards render with correct rounded corners, shadows, and transitions. |
| 6 | Test all features in Firefox | All features work; debounce timing is consistent. |
| 7 | Open Employee Directory in Safari | CSS grid layout, transitions, and hover effects render correctly. |
| 8 | Test all features in Safari | All features work; file download (export) triggers correctly. |
| 9 | Verify no console errors in any browser | Browser console shows no JavaScript errors or warnings related to the directory. |

## 6. Postconditions
- No data was modified.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

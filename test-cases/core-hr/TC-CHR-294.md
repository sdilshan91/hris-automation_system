---
id: TC-CHR-294
user_story: US-CHR-011
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-294: Cross-browser compatibility for manager assignment and My Team features

## 1. Test Objective
Verify that the manager assignment UI (selector modal, bulk assign, My Team view) renders and functions correctly across Chrome, Edge, Firefox, and Safari. This validates cross-browser compatibility under NFR-4.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee E and Manager M exist.
- Test browsers: Chrome (latest), Edge (latest), Firefox (latest), Safari (latest).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Browsers | Chrome, Edge, Firefox, Safari | Latest stable versions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In each browser: navigate to Employee E's profile and open the manager selector. | The modal/dropdown renders correctly in all 4 browsers. Search autocomplete works. |
| 2 | In each browser: assign Manager M to Employee E. | Assignment succeeds. Success toast displays. The Reporting Manager mini-card renders correctly. |
| 3 | In each browser: navigate to My Team view (as Manager). | Direct-report cards render with correct layout, avatars, and status badges across all browsers. |
| 4 | In each browser: test the bulk assign flow from the employee directory. | Checkbox selection, floating action toolbar, bulk assign modal all function without rendering issues. |
| 5 | In each browser: verify the reporting chain breadcrumb. | Breadcrumb renders horizontally with correct links across all browsers. |
| 6 | In each browser: verify no JavaScript console errors. | No browser-specific errors in the console. |

## 6. Postconditions
- Feature verified in all 4 target browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test

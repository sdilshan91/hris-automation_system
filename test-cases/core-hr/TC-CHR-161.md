---
id: TC-CHR-161
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-161: Search with no match returns no highlight and shows informative empty state

## 1. Test Objective
Verify that searching for a non-existent employee name in the org tree produces no highlight, no auto-expansion, and displays an informative "no results found" state rather than silently doing nothing. This is a negative test for AC-4 and FR-4.

## 2. Related Requirements
- User Story: US-CHR-006
- Acceptance Criteria: AC-4
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart is rendered with several departments and employees. No employee named "Xylophone" exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Search Query | Xylophone | Non-existent name |
| Expected Results | 0 | No matching employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders normally. |
| 2 | Click on the search bar | Search bar gains focus. |
| 3 | Type "Xylophone" in the search bar | Typeahead dropdown appears showing "No results found" or an empty state message. |
| 4 | Press Enter or wait for search to complete | No tree node is highlighted. No branches auto-expand. |
| 5 | Verify the tree state is unchanged | The tree remains in its current expand/collapse state; no visual changes occurred to any nodes. |
| 6 | Verify no JavaScript console errors | No errors in the browser console from the search action. |
| 7 | Clear the search bar | Search bar returns to empty state; any "no results" indicator disappears. |

## 6. Postconditions
- No data was modified.
- Tree state is unchanged from before the search.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

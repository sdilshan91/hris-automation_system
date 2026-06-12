---
id: TC-CHR-150
user_story: US-CHR-003
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-150: URL state restoration for deep-linking and browser back/forward (FR-6)

## 1. Test Objective
Verify that search, filter, sort, page, and view mode state is persisted in URL query parameters and correctly restored on page load, browser back, and browser forward. This validates FR-6.

## 2. Related Requirements
- User Story: US-CHR-003
- Functional Requirements: FR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 50 employees exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| URL state | `?search=John&departments=Engineering&page=2&pageSize=20&sort=dateOfJoining&sortDirection=desc&view=table` | Complex state |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the full URL with all parameters | Directory loads with: search "John" pre-filled, Department "Engineering" filter chip visible, page 2, sorted by date of joining descending, table view. |
| 2 | Verify all state matches the URL | Search bar shows "John"; filter chip shows "Engineering"; pagination shows page 2; sort indicator shows date of joining desc; view mode is table. |
| 3 | Change the search to "Jane" | URL updates to `?search=Jane&...` with replaceUrl (no history entry for each keystroke). |
| 4 | Navigate to page 3 | URL updates to `?...&page=3`. |
| 5 | Click browser back button | Returns to the previous state (page 2, search "Jane" or earlier state). |
| 6 | Click browser forward button | Returns to page 3 state. |
| 7 | Copy the URL and open in a new incognito tab (same auth) | The directory loads with identical state: filters, search, sort, page, view mode. |
| 8 | Share the URL with another HR Officer user | The other user sees the same filtered view. |

## 6. Postconditions
- No data was modified.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

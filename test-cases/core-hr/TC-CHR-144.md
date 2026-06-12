---
id: TC-CHR-144
user_story: US-CHR-003
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-144: Search results update within 500ms of user stopping typing (NFR-2)

## 1. Test Objective
Verify that after the user stops typing in the search bar, the directory results update within 500ms total (300ms debounce + API response). This validates NFR-2.

## 2. Related Requirements
- User Story: US-CHR-003
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 5,000 employees exist for realistic search load.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Search term | "Smith" | Common name, multiple matches |
| Debounce | 300ms | Per AC-2 |
| Max total latency | 500ms | 300ms debounce + 200ms API |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory | Full directory loads. |
| 2 | Type "Smith" in the search bar and start a timer when the last keystroke is pressed | Timer starts. |
| 3 | Observe when the search results appear | Search results are rendered within 500ms of the last keystroke. |
| 4 | Measure the debounce delay | No API call is made during the first 300ms after the last keystroke. The API call fires at approximately 300ms. |
| 5 | Measure the API response time | The API response returns within 200ms (total = debounce + API <= 500ms). |
| 6 | Repeat for 20 iterations | P95 of total latency (last keystroke to results rendered) is <= 500ms. |

## 6. Postconditions
- Performance metrics are recorded.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

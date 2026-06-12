---
id: TC-CHR-154
user_story: US-CHR-006
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-154: Search for employee at deepest level -- tree auto-expands, scrolls, and highlights

## 1. Test Objective
Verify that when the user searches for an employee in the org tree, the tree highlights and auto-scrolls to the matching node, with the path from root to that node expanded and all other branches collapsed. This validates AC-4, FR-4.

## 2. Related Requirements
- User Story: US-CHR-006
- Acceptance Criteria: AC-4
- Functional Requirements: FR-4, FR-6
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- 4-level department hierarchy: "Company" (root) -> "Engineering" -> "Backend" -> "Platform".
- Employee "Zara Zimmerman" is assigned to the "Platform" department (deepest level, level 4).
- Only top 2 levels are initially loaded (lazy loading active).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Search Query | Zara | Partial name match |
| Target Employee | Zara Zimmerman | In "Platform" department at level 4 |
| Hierarchy Path | Company > Engineering > Backend > Platform | 4-level depth |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders with top 2 levels: "Company" and "Engineering". |
| 2 | Click on the search bar at the top-right | Search bar gains focus; typeahead is ready. |
| 3 | Type "Zara" in the search bar | Typeahead dropdown appears showing "Zara Zimmerman - Platform" as a match. |
| 4 | Select "Zara Zimmerman" from the typeahead results (or press Enter) | The tree begins auto-expanding the path to "Zara Zimmerman". |
| 5 | Verify API calls for lazy-loaded nodes | API calls are made to load children of "Engineering" (to reveal "Backend") and children of "Backend" (to reveal "Platform"). |
| 6 | Verify path from root to target is expanded | "Company" -> "Engineering" -> "Backend" -> "Platform" are all expanded and visible. |
| 7 | Verify all other branches are collapsed | Any sibling branches (e.g., other root departments, other children of Engineering) are collapsed. |
| 8 | Verify the canvas auto-scrolls to the target node | The viewport pans so that "Zara Zimmerman" (or her department "Platform") is visible and centered. |
| 9 | Verify the target node is highlighted | "Zara Zimmerman" node has a visual highlight (e.g., distinct border color, glow, or background) distinguishing it from other nodes. |
| 10 | Clear the search bar | The highlight is removed; the tree remains in its expanded state. |

## 6. Postconditions
- No data was modified.
- The tree path remains expanded after search is cleared.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

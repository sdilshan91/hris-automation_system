---
id: TC-CHR-159
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-159: Inactive toggle shows inactive departments and employees

## 1. Test Objective
Verify that by default only active departments and active employees are shown in the org tree, and that a toggle can reveal inactive items. This validates BR-4.

## 2. Related Requirements
- User Story: US-CHR-006
- Business Rules: BR-4
- Functional Requirements: FR-1, FR-8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Active departments: "Engineering" (root), "Backend" (child of Engineering).
- Inactive department: "Legacy Systems" (child of Engineering, `is_active = false`).
- Active employee in Engineering: "Alice Adams".
- Inactive employee in Engineering: "Bob Baker" (`is_active = false`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Active Departments | Engineering, Backend | Visible by default |
| Inactive Department | Legacy Systems | Hidden by default |
| Active Employee | Alice Adams | Visible by default |
| Inactive Employee | Bob Baker | Hidden by default |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders. |
| 2 | Verify default state shows only active items | "Engineering" and "Backend" are visible. "Legacy Systems" is NOT visible. Employee count on "Engineering" does not include Bob Baker. |
| 3 | Locate the "Show Inactive" toggle | A toggle switch or checkbox labeled "Show Inactive" is present in the toolbar or filter area. |
| 4 | Enable the "Show Inactive" toggle | The tree re-renders. |
| 5 | Verify "Legacy Systems" now appears in the tree | "Legacy Systems" is displayed as a child of "Engineering" with a visual indicator of its inactive status (e.g., grayed-out card, "Inactive" badge). |
| 6 | Verify inactive employee Bob Baker is reflected | If the tree shows employee counts, the count for "Engineering" increases to include Bob Baker. If employee nodes are shown, Bob Baker appears with an inactive indicator. |
| 7 | Verify the API call includes the inactive parameter | API call includes a parameter like `?includeInactive=true`. |
| 8 | Disable the "Show Inactive" toggle | "Legacy Systems" disappears from the tree; employee counts revert to active-only. |

## 6. Postconditions
- No data was modified.
- Toggle state does not persist across page reloads (unless URL-parameterized).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

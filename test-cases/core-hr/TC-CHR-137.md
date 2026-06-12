---
id: TC-CHR-137
user_story: US-CHR-003
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-137: Show Archived toggle includes soft-deleted employees (BR-1)

## 1. Test Objective
Verify that soft-deleted employees (is_deleted = true) are excluded from the directory by default, and that an HR Officer can toggle "Show Archived" to include them. Non-HR roles cannot use the toggle. This validates BR-1, FR-7.

## 2. Related Requirements
- User Story: US-CHR-003
- Business Rules: BR-1
- Functional Requirements: FR-7

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 50 employees exist: 45 active (non-deleted), 5 soft-deleted (is_deleted = true).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Active employees | 45 | is_deleted = false |
| Archived employees | 5 | is_deleted = true |
| Show Archived | false (default) | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Employee Directory as HR Officer | Directory shows 45 employees. The 5 archived employees are NOT visible. |
| 2 | Toggle "Show Archived" on | Directory reloads; total count changes to 50. The 5 archived employees are visible, potentially with a visual indicator (e.g., "Archived" badge or dimmed card). |
| 3 | Verify URL includes `includeArchived=true` | The toggle state is reflected in the URL. |
| 4 | Verify API call includes `showArchived=true` | The request parameter is sent to the backend. |
| 5 | Toggle "Show Archived" off | Directory returns to 45 employees. Archived employees are hidden again. |
| 6 | Login as Employee role | Navigate to the directory. |
| 7 | Verify "Show Archived" toggle is not available | The toggle button is hidden or disabled for Employee role (only HR Officers with `Employee.View.All` can use it). |

## 6. Postconditions
- No data was modified.
- Tenant isolation is maintained: the `IgnoreQueryFilters` + explicit `WHERE tenant_id` re-application ensures no cross-tenant leakage.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

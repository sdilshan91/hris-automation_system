---
id: TC-CHR-160
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-160: Expand a leaf node with no children -- empty state, no error

## 1. Test Objective
Verify that expanding a department node that has `children_count: 0` does not trigger an API call, does not produce an error, and correctly shows an empty state or simply removes the expand indicator. This is a negative/boundary test for FR-2 and FR-6.

## 2. Related Requirements
- User Story: US-CHR-006
- Functional Requirements: FR-2, FR-6
- Acceptance Criteria: AC-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Department "QA" exists as a leaf node with `children_count: 0` and 2 direct employees.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | QA | Leaf node, no sub-departments |
| children_count | 0 | No children to load |
| Employee Count | 2 | Has employees but no sub-departments |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders with "QA" node visible. |
| 2 | Verify the "QA" node does NOT have an expand indicator | Since `children_count: 0`, no expand chevron/icon is displayed on the "QA" node. |
| 3 | Click on the "QA" node | The detail panel opens (per AC-2) showing the department info, but no sub-departments section is populated. The node does not attempt to expand children. |
| 4 | Verify no API call for children was made | The browser network log shows no request like `GET /api/v1/org-tree?parentId={qa-id}`. |
| 5 | Verify no console errors | Browser console has no JavaScript errors related to the click action. |

## 6. Postconditions
- No data was modified.
- No unnecessary API calls were made.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

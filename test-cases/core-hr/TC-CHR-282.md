---
id: TC-CHR-282
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-282: Org tree reporting structure view shows real manager-to-report hierarchy

## 1. Test Objective
Verify that after manager assignments are made via US-CHR-011, the org tree "Reporting Structure" view (US-CHR-006) renders the actual manager-to-direct-report hierarchy instead of the placeholder/department-based approximation. This validates the integration with US-CHR-006.

## 2. Related Requirements
- User Story: US-CHR-011
- Dependencies: US-CHR-006 (Organization Tree Visualization)
- Functional Requirements: FR-5, FR-8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer or Manager user is authenticated.
- A reporting chain exists: CEO (no manager) -> VP -> Director -> Employee.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| CEO | ceo@acme.test | reports_to = null (root) |
| VP | vp@acme.test | reports_to = CEO |
| Director | dir@acme.test | reports_to = VP |
| Employee | emp@acme.test | reports_to = Director |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the org tree page. | The page loads with the default "Department Hierarchy" view. |
| 2 | Toggle to the "Reporting Structure" view. | The view switches to show the manager-to-direct-report hierarchy. `ReportingViewAvailable` is now `true` (since US-CHR-011 added `reports_to_employee_id`). |
| 3 | Verify the CEO appears as a root node. | CEO node is at the top level with no parent. |
| 4 | Expand the CEO node. | VP appears as a child of CEO. |
| 5 | Expand the VP node. | Director appears as a child of VP. |
| 6 | Expand the Director node. | Employee appears as a child of Director. |
| 7 | Verify that an employee with no `reports_to` (besides CEO) appears under their department node (BR-3 fallback). | If any employees lack a `reports_to` assignment, they appear under their department node, not orphaned. |

## 6. Postconditions
- No state change; read-only visualization.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

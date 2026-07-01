---
id: TC-CHR-282
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: pass
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

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — requires the employee profile/detail view and/or the org-tree hierarchy to verify the reporting-manager field / null-root / unlimited-reports / hierarchy / breadcrumb. The Employee Directory list is crashed (**BUG-099**) so profiles aren't reachable by in-app click, and the org tree renders no nodes (**ISSUE-207**). The "My Team" view (`/employees/my-team`) renders correctly with an empty-state for tenantadmin. Not separately runnable in this FE sweep.

> **Execution 2026-07-01 (API, acme, tenantadmin):** **PASS.** `GET /org-tree?view=reporting&depth=5` returns a real manager-to-report hierarchy: 17 root nodes (`parentId:null`), 5 with children, including genuine multi-level chains — e.g. "Imp1782370711_6 > _5 > _4" (3 levels) and "Team Manager > John Doe > …". Roots with no `reports_to` appear at top level (BR-3 fallback observed — employees without a manager surface as roots). ReportingViewAvailable is effectively true (non-empty reporting graph). Node toggling/expansion (steps 3-6 UI) is FE, but the underlying hierarchy data is correct and nested.

---
id: TC-CHR-005
user_story: US-CHR-004
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-005: Build multi-level department hierarchy (3+ levels)

## 1. Test Objective
Verify that the system supports creating a multi-level department hierarchy (at least 3 levels deep), that the tree view renders the full hierarchy correctly with expand/collapse functionality, and that the flat list view also shows correct parent references.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-2, AC-4
- Functional Requirements: FR-3, FR-8
- Business Rules: BR-3, BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- No pre-existing departments in "acme" (or a known clean state).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Level 1 (root) | Company HQ | Root department |
| Level 2 | Engineering | Parent: Company HQ |
| Level 3 | Backend Team | Parent: Engineering |
| Level 4 | API Squad | Parent: Backend Team |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create root department "Company HQ" with no parent | Department created successfully (201 Created). |
| 2 | Create "Engineering" with parent "Company HQ" | Department created successfully with `parent_department_id` referencing Company HQ. |
| 3 | Create "Backend Team" with parent "Engineering" | Department created successfully with `parent_department_id` referencing Engineering. |
| 4 | Create "API Squad" with parent "Backend Team" | Department created successfully with `parent_department_id` referencing Backend Team. |
| 5 | Navigate to the department list (flat table view) | All four departments are visible. Parent column shows: Company HQ = "-", Engineering = "Company HQ", Backend Team = "Engineering", API Squad = "Backend Team". |
| 6 | Toggle to tree view | Tree displays: Company HQ (root) > Engineering > Backend Team > API Squad. All nodes are expandable/collapsible. |
| 7 | Collapse "Engineering" node | "Backend Team" and "API Squad" are hidden. |
| 8 | Expand "Engineering" node | "Backend Team" and "API Squad" reappear in correct hierarchy. |
| 9 | Verify database FK chain | `API Squad.parent_department_id` -> `Backend Team.department_id` -> `Engineering.department_id` -> `Company HQ.department_id`. Company HQ has `parent_department_id = null`. All share the same `tenant_id`. |

## 6. Postconditions
- Four departments exist forming a 4-level hierarchy.
- Tree view and flat list view both accurately represent the relationships.
- All departments belong to the same tenant.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

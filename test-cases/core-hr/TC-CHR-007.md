---
id: TC-CHR-007
user_story: US-CHR-004
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-007: Prevent circular parent-child reference (indirect cycle A->B->C->A)

## 1. Test Objective
Verify that the system detects and rejects an indirect circular reference in the department hierarchy. Per the test hint in US-CHR-004: create A -> B -> C, then attempt to set A's parent to C. The server-side validation must reject this.

## 2. Related Requirements
- User Story: US-CHR-004
- Acceptance Criteria: AC-4
- Functional Requirements: FR-3, FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- Three departments exist: "Dept A" (root), "Dept B" (parent: Dept A), "Dept C" (parent: Dept B). Hierarchy: A -> B -> C.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Dept A | Root department | parent_department_id = null |
| Dept B | Child of A | parent_department_id = A |
| Dept C | Child of B | parent_department_id = B |
| Attempted change | Set A's parent to C | Would create cycle A -> B -> C -> A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the existing hierarchy: A (root) -> B -> C | Tree view shows the correct 3-level hierarchy. |
| 2 | Click the Edit action on "Dept A" | Edit form opens with Dept A's current values (parent = none). |
| 3 | Open the Parent Department dropdown and select "Dept C" | "Dept C" is selected as the new parent. |
| 4 | Click "Save" | API call `PUT /api/v1/departments/{A_id}` is made with `parent_department_id = C_id`. |
| 5 | Verify the API returns an error (400 Bad Request or 422 Unprocessable Entity) | Response body contains a message such as "Circular reference detected: this change would create a cycle in the department hierarchy." |
| 6 | Verify the error is displayed in the UI | User sees the circular reference error clearly. |
| 7 | Verify "Dept A" remains a root department in the database | `parent_department_id` is still null. The hierarchy A -> B -> C is unchanged. |

## 6. Postconditions
- The hierarchy remains A (root) -> B -> C with no cycles.
- No database update was persisted.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

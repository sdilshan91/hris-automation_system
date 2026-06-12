---
id: TC-CHR-170
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-170: Root departments display at top; employees without manager appear under department in reporting view

## 1. Test Objective
Verify that departments with no parent are rendered as root nodes at the top of the tree (BR-2), and that in the reporting structure view, employees without a manager assignment appear under their department node rather than under any manager node (BR-3). This validates BR-2 and BR-3.

## 2. Related Requirements
- User Story: US-CHR-006
- Business Rules: BR-2, BR-3
- Functional Requirements: FR-1
- Acceptance Criteria: AC-1, AC-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Two root departments (no parent): "Engineering" and "HR".
- "Engineering" has manager "Alice Adams". "HR" has manager "Bob Baker".
- Employee "Charlie Clark" belongs to "Engineering" department but has NO manager assigned.
- Employee "Diana Dean" reports to "Alice Adams".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Root Departments | Engineering, HR | parent_department_id = null |
| Unmanaged Employee | Charlie Clark | No manager, in Engineering |
| Managed Employee | Diana Dean | Reports to Alice Adams |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page in "Department" view | Tree renders. |
| 2 | Verify "Engineering" and "HR" are at the top level | Both departments are positioned at the topmost row of the tree, as root nodes with no parent connector above them. |
| 3 | Verify no other departments appear at the root level | Only departments with `parent_department_id = null` are at the top. |
| 4 | Toggle to "Reporting Structure" view | The tree reorganizes to show manager-to-report relationships. |
| 5 | Verify "Alice Adams" appears as a top-level manager node | Alice is at the root of the reporting tree. |
| 6 | Expand "Alice Adams" node | "Diana Dean" appears as a direct report of Alice. |
| 7 | Verify "Charlie Clark" does NOT appear under any manager node | Charlie is not listed as a report of Alice or any other manager. |
| 8 | Verify "Charlie Clark" appears under the "Engineering" department node | Since Charlie has no manager, he is shown under his department node in the reporting view (per BR-3). |

## 6. Postconditions
- No data was modified.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

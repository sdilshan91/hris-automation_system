---
id: TC-CHR-024
user_story: US-CHR-004
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-11
---

# TC-CHR-024: Department hierarchy depth tolerance (10 levels)

## 1. Test Objective
Verify that the system supports department hierarchies up to 10 levels deep (as per Section 10 assumptions: "expected to be <= 10 levels for practical use") and that the tree view renders all levels correctly.

## 2. Related Requirements
- User Story: US-CHR-004
- Functional Requirements: FR-3, FR-8
- Assumptions: Section 10 (hierarchy depth <= 10 levels)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- No pre-existing departments in "acme" (or known clean state).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Level 1 | Level-01 | Root |
| Level 2 | Level-02 | Parent: Level-01 |
| ... | ... | ... |
| Level 10 | Level-10 | Parent: Level-09 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create "Level-01" as a root department | Created successfully (201). |
| 2 | Create "Level-02" with parent "Level-01" | Created successfully. |
| 3 | Continue creating departments up to "Level-10", each parented to the previous level | All 10 departments created successfully. No error or depth limit encountered. |
| 4 | Navigate to the tree view | All 10 levels are rendered with correct nesting. Each level is expandable/collapsible. |
| 5 | Expand all nodes from Level-01 down to Level-10 | Full hierarchy is visible with proper indentation. |
| 6 | Verify the flat list shows correct parent references for all 10 departments | Each department shows its immediate parent in the Parent column. |
| 7 | Attempt to create "Level-11" under "Level-10" | Either: (a) succeeds (no hard depth limit), or (b) returns a warning if a depth limit is enforced. Document actual behavior. |

## 6. Postconditions
- 10-level hierarchy exists and renders correctly.
- System performance remains acceptable at this depth.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
